using System.Drawing.Imaging;
namespace RmdblobUnpacker.Core;

public static class ResourceExporter
{
    public const string Header = "path,size,pack,blob,offset,crc32,raw,output,conversion\n";
    public static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    public static string Write(TocFile toc, ResourceEntry entry, string root, CancellationToken token = default)
    {
        var (data, raw) = toc.Extract(entry);
        uint crc = raw ? 0 : Crc32.Compute(data);
        if (!raw && entry.Crc32 != 0 && crc != entry.Crc32)
            throw new InvalidDataException("CRC32 mismatch: " + entry.Key);
        string relative = Program.Sanitize(entry.Key) + (raw ? ".raw" : "");
        string dst = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.WriteAllBytes(dst, data);
        string conversion = raw ? "Unsupported storage; raw chunks only" : "Decoded original";
        if (!raw && TextureDecoder.IsDds(data))
        {
            string dds = dst.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? dst : dst + ".dds";
            if (dds != dst) File.WriteAllBytes(dds, data);
            conversion = "DDS (all surfaces/mips)";
            try
            {
                TextureDecoder.SavePng(data, dst + ".png");
                conversion += "; PNG (mip 0, surface 0)";
            }
            catch (NotSupportedException ex) { conversion += "; " + ex.Message; }
        }
        if (!raw && entry.Path.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
        {
            try { conversion += "; " + AudioConverter.ConvertWem(dst, dst + ".wav", token); }
            catch (NotSupportedException ex) { conversion += "; " + ex.Message; }
        }
        if (!raw && entry.Path.EndsWith(".binfbx", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var model = BinFbxDecoder.Decode(data, token);
                FbxWriter.Write(dst + ".fbx", model, 0, token);
                conversion += "; FBX 7.4 binary (static LOD0; UVs and material IDs; normals recalculated; no shader/texture reconstruction)";
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidDataException)
            {
                conversion += "; FBX not generated: " + ex.Message + "; original BINFBX retained";
            }
        }
        var c = entry.Chunks.FirstOrDefault();
        return $"{Csv(entry.Path)},{entry.Size},{Csv(entry.Pack)},{c.BlobIdx},{c.Offset},{crc:X8},{raw},{Csv(relative)},{Csv(conversion)}";
    }
}
