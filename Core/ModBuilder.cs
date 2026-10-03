using System.Text;
namespace RmdblobUnpacker.Core;

/// <summary>Builds development override TOCs; source archives are never modified.</summary>
public static class ModBuilder
{
    static void Put(byte[] b,int o,uint v)=>BitConverter.GetBytes(v).CopyTo(b,o);
    static void Put64(byte[] b,int o,ulong v)=>BitConverter.GetBytes(v).CopyTo(b,o);
    static uint U(byte[] b,int o)=>BitConverter.ToUInt32(b,o);
    public static byte[] LiteralLz4(byte[] data)
    {
        using var output=new MemoryStream();output.WriteByte((byte)(Math.Min(15,data.Length)<<4));
        if(data.Length>=15){int n=data.Length-15;while(n>=255){output.WriteByte(255);n-=255;}output.WriteByte((byte)n);}
        output.Write(data);return output.ToArray();
    }
    public static string Build(string destination, IReadOnlyCollection<ModReplacement> replacements, CancellationToken token=default)
    {
        if(replacements.Count==0)throw new InvalidOperationException(L.En?"No staged replacements.":"没有已导入的修改。");
        destination=Path.GetFullPath(destination);
        if(Directory.Exists(destination)||File.Exists(destination))throw new IOException(L.En?"Output directory already exists.":"输出目录已存在。");
        foreach(var replacement in replacements)
        {
            string game=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(replacement.Toc.TocPath)!,"../.."))+Path.DirectorySeparatorChar;
            if((destination+Path.DirectorySeparatorChar).StartsWith(game,StringComparison.OrdinalIgnoreCase))
                throw new IOException(L.En?"Choose a build directory outside the game folder.":"请选择游戏目录之外的生成位置。");
        }
        string staging=destination+".building-"+Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try
        {
            string pc=Path.Combine(staging,"data_pack2","pc");Directory.CreateDirectory(pc);
            var manifest=new StringBuilder("pack,path,source,size,crc32,validation\n");
            foreach(var group in replacements.GroupBy(r=>r.Toc))
            {
                token.ThrowIfCancellationRequested();BuildPack(group.Key,group.ToList(),pc,token);
                foreach(var r in group)manifest.AppendLine($"{ResourceExporter.Csv(r.Entry.Pack)},{ResourceExporter.Csv(r.Entry.Path)},{ResourceExporter.Csv(r.SourceName)},{r.Data.Length},{Crc32.Compute(r.Data):X8},{ResourceExporter.Csv(r.Validation)}");
            }
            File.WriteAllText(Path.Combine(staging,"mod-manifest.csv"),manifest.ToString(),Encoding.UTF8);
            File.WriteAllText(Path.Combine(staging,"INSTALL.txt"),
                "CONTROL Resonant development override / 开发覆盖包\n\n"+
                "Copy the included data_pack2 folder into the matching game version's root. Back up any existing *-development.rmdtoc/.rmdblob with identical names first. Do not replace the base TOCs or blobs.\n"+
                "将 data_pack2 文件夹复制到对应游戏版本的根目录。若已有同名 development 包，请先备份；不要覆盖 base 原始包。\n\n"+
                "Development packages for the same archive conflict; combine their replacements in one build. The package depends on the original game blobs.\n"+
                "同一资源包的 development MOD 会冲突，需要合并修改后统一生成。本包依赖原游戏 blob 文件。\n\n"+
                "Offline TOC and replacement-byte validation passed. In-game loading/appearance is not automatically tested.\n"+
                "已通过离线 TOC 和替换内容回读校验；游戏内加载及效果需实际验证。\n\n"+
                (replacements.Any(r=>r.Entry.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase)) ?
                "Model edits preserve topology, materials and normal/tangent data, and update all LODs. Separate collision/physics resources are not modified. Test appearance, LOD transitions and collisions in-game before distribution.\n"+
                "模型修改保留拓扑、材质及法线/切线数据，同步全部 LOD；独立碰撞/物理资源未修改。发布前请在游戏中测试外观、LOD 切换及碰撞。\n" : ""),Encoding.UTF8);
            token.ThrowIfCancellationRequested();Directory.Move(staging,destination);return destination;
        }
        catch
        {
            // Only this uniquely-created sibling staging directory is removed.
            if(Directory.Exists(staging))Directory.Delete(staging,true);throw;
        }
    }
    static void BuildPack(TocFile toc,List<ModReplacement> entries,string pc,CancellationToken token)
    {
        if(toc.Pack.EndsWith("-development",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use the base archive, not another development archive.");
        if(toc.BlobCount>=65535)throw new InvalidDataException("Blob index limit reached");
        string name=toc.Pack+"-development-000.rmdblob";
        byte[] path=Encoding.UTF8.GetBytes("../pc/"+name);
        int stringDelta=(path.Length+7)&~7;
        int firstInsert=toc.BlobCount*24, stringInsert=toc.StrEnd+24;
        byte[] body=new byte[checked(toc.Body.Length+24+stringDelta+entries.Count*16)];
        toc.Body.AsSpan(0,firstInsert).CopyTo(body);
        toc.Body.AsSpan(firstInsert,toc.StrEnd-firstInsert).CopyTo(body.AsSpan(firstInsert+24));
        path.CopyTo(body,stringInsert);
        toc.Body.AsSpan(toc.StrEnd).CopyTo(body.AsSpan(stringInsert+stringDelta));
        // Copy the identifier fields from a valid descriptor, as in RMT v0.2.9.
        toc.Body.AsSpan(entries[0].Entry.Chunks[0].BlobIdx*24,24).CopyTo(body.AsSpan(firstInsert));
        Put(body,firstInsert,(uint)(toc.StrEnd-toc.StrStart));Put(body,firstInsert+4,(uint)path.Length);
        int info=toc.InfoStart+24, records=toc.EntOff+24+stringDelta, idx=toc.IdxOff+24+stringDelta;
        byte[] header=(byte[])toc.Header.Clone();
        Put(header,20,(uint)(toc.BlobCount+1));Put(header,24,(uint)((toc.BlobCount+1)*24));
        Put(header,32,(uint)info);Put(header,40,(uint)(toc.StrStart+24));Put(header,44,(uint)(toc.StrEnd-toc.StrStart+stringDelta));
        Put(header,48,(uint)(toc.StrEnd+24+stringDelta));Put(header,56,(uint)records);Put(header,80,(uint)idx);Put(header,84,(uint)(toc.IdxSize+entries.Count*16));
        string blobPath=Path.Combine(pc,name);
        using(var blob=File.Create(blobPath))
        for(int i=0;i<entries.Count;i++)
        {
            token.ThrowIfCancellationRequested();var r=entries[i];var e=r.Entry;
            if(!ModImport.Supports(e) || e.EntryIndex<0 || e.EntryIndex>=toc.Count || e.Chunks.Length==0)throw new InvalidDataException("Invalid replacement target");
            if(e.Path.EndsWith(".tex",StringComparison.OrdinalIgnoreCase))ModImport.ValidateTexture(r.Data);
            else if(e.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase))BinFbxDecoder.Decode(r.Data,token);
            else ModImport.ValidateFont(r.Data);
            while(blob.Position%16!=0)blob.WriteByte(0);
            long blobOffset=blob.Position;var encoded=LiteralLz4(r.Data);blob.Write(encoded);
            int desc=toc.IdxSize+i*16;
            int io=info+e.EntryIndex*32;Put(body,io,(uint)desc);Put(body,io+4,16);Put(body,io+20,(uint)r.Data.Length);
            Put64(body,idx+desc,((ulong)blobOffset<<24)|((ulong)toc.BlobCount<<8)|0x10);
            Put(body,idx+desc+8,(uint)r.Data.Length);Put(body,idx+desc+12,(uint)encoded.Length);
            int record=records+e.RecOff;
            int metadata=checked(8+(int)U(body,record+4)*8);
            if(metadata+16>e.RecLen||U(body,record+metadata)!=1||BitConverter.ToUInt64(body,record+metadata+4)!=(ulong)e.Size)
                throw new InvalidDataException("Unrecognized resource size/CRC metadata: "+e.Path);
            Put64(body,record+metadata+4,(ulong)r.Data.Length);Put(body,record+metadata+12,Crc32.Compute(r.Data));
        }
        Put64(body,firstInsert+16,(ulong)new FileInfo(blobPath).Length);
        var tocPath=Path.Combine(pc,toc.Pack+"-development.rmdtoc");
        File.WriteAllBytes(tocPath,Rebuild(header,body));
        var check=TocFile.Load(tocPath);
        foreach(var r in entries)
        {
            var rebuilt=check.Entries.Single(e=>e.Path==r.Entry.Path);
            var (data,raw)=check.Extract(rebuilt);
            if(raw||!data.AsSpan().SequenceEqual(r.Data)||rebuilt.Crc32!=Crc32.Compute(r.Data))
                throw new InvalidDataException("Development pack read-back failed: "+r.Entry.Path);
        }
        // Other entries must retain their descriptors and original blob paths.
        var changed=entries.Select(r=>r.Entry.EntryIndex).ToHashSet();
        for(int i=0;i<toc.Entries.Count;i++)
            if(!changed.Contains(toc.Entries[i].EntryIndex)&&!toc.Entries[i].Chunks.SequenceEqual(check.Entries[i].Chunks))
                throw new InvalidDataException("Unmodified resource descriptor changed");
    }
    static byte[] Rebuild(byte[] header,byte[] body)
    {
        int count=(int)U(header,12)/16;
        var blocks=new List<(int Start,int Unc,byte[] Bytes)>();int position=4096,offset=0;
        for(int i=0;i<count;i++)
        {
            int unc=i==count-1?body.Length-offset:checked((int)U(header,96+i*16));
            if(unc<0||unc>body.Length-offset)throw new InvalidDataException("Invalid metadata block sizes");
            var bytes=LiteralLz4(body.AsSpan(offset,unc).ToArray());blocks.Add((position,unc,bytes));offset+=unc;
            position=checked((position+bytes.Length+15)&~15);
        }
        var output=new byte[blocks[^1].Start+blocks[^1].Bytes.Length];
        for(int i=0;i<count;i++)
        {
            int o=96+i*16;var block=blocks[i];uint next=i+1<count?(uint)blocks[i+1].Start:0;
            Put(header,o,(uint)block.Unc);Put(header,o+4,(uint)block.Bytes.Length);
            Put(header,o+8,(U(header,o+8)&0xFFFFFF)|((next&255)<<24));Put(header,o+12,next>>8);
            block.Bytes.CopyTo(output,block.Start);
        }
        header.CopyTo(output,0);return output;
    }
}
