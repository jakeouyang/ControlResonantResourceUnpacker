using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace RmdblobUnpacker.Core;

public sealed record ModReplacement(TocFile Toc, ResourceEntry Entry, string SourceName, byte[] Data, string Validation);

public static class ModImport
{
    static InvalidDataException Error(string zh, string en) => new(L.En ? en : zh);
    public static bool Supports(ResourceEntry e) => Path.GetExtension(e.Path).ToLowerInvariant() is ".tex" or ".ttf" or ".binfbx";
    public static ModReplacement Prepare(TocFile toc, ResourceEntry entry, string filename)
    {
        if (!Supports(entry)) throw Error("仅支持替换 TEX、TTF 和受支持的静态 BINFBX。", "Only TEX, TTF and supported static BINFBX replacements are supported.");
        if (new FileInfo(filename).Length > 268435456) throw Error("导入文件不得超过 256 MB。", "Import limited to 256 MB.");
        var (original, raw) = toc.Extract(entry);
        if (raw) throw Error("原资源尚未解码，不能制作替换包。", "The original resource could not be decoded.");
        if (entry.Crc32 != 0 && Crc32.Compute(original) != entry.Crc32) throw Error("原资源 CRC 校验失败，请检查游戏文件完整性。", "Original resource CRC mismatch; check game file integrity.");
        byte[] result;
        string note;
        if (entry.Path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))
        {
            result = File.ReadAllBytes(filename); ValidateFont(result);
            note = L.En ? "TTF validated" : "TTF 校验通过";
        }
        else if(entry.Path.EndsWith(".binfbx",StringComparison.OrdinalIgnoreCase))
        {
            (result,note)=BinFbxImport.Prepare(original,filename);
        }
        else
        {
            result = Path.GetExtension(filename).Equals(".png", StringComparison.OrdinalIgnoreCase)
                ? EncodePng(original, filename) : NormalizeDds(original, File.ReadAllBytes(filename));
            note = L.En ? "Texture validated; original header preserved" : "纹理校验通过，已保留原始头";
        }
        if (result.AsSpan().SequenceEqual(original)) throw Error("导入内容与原资源相同，没有修改。", "Replacement matches the original; no changes to stage.");
        return new(toc, entry, Path.GetFileName(filename), result, note);
    }
    public static void ValidateFont(byte[] data)
    {
        if (data.Length < 12 || BinaryPrimitives.ReadUInt32BigEndian(data) != 0x10000)
            throw Error("文件不是 TrueType TTF 字体。", "File is not a TrueType TTF font.");
        int n = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        if (n < 1 || 12L + n * 16L > data.Length) throw Error("TTF 表目录不完整。", "Truncated TTF table directory.");
        var tags = new HashSet<string>();
        for (int i = 0; i < n; i++)
        {
            int o = 12 + i * 16;
            string tag = System.Text.Encoding.ASCII.GetString(data, o, 4);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 8));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(o + 12));
            if (!tags.Add(tag) || offset < 12 + n * 16 || (ulong)offset + length > (ulong)data.Length)
                throw Error("TTF 包含越界或重复的表。", "TTF has out-of-bounds or duplicate tables.");
            if (tag == "head" && (length < 54 || BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset + 12)) != 0x5F0F3CF5))
                throw Error("TTF head 表无效。", "Invalid TTF head table.");
        }
        foreach(var tag in new[]{"head","maxp","cmap","name","hhea","hmtx","glyf","loca"})
            if (!tags.Contains(tag)) throw Error("TTF 缺少必要的表：" + tag, "TTF missing required table: " + tag);
        // Ask Windows to validate the font in a private collection; never install it.
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { using var fonts = new System.Drawing.Text.PrivateFontCollection(); fonts.AddMemoryFont(handle.AddrOfPinnedObject(), data.Length);
            if (fonts.Families.Length == 0) throw Error("Windows 无法识别此字体。", "Windows cannot recognize this font."); }
        finally { handle.Free(); }
    }
    static int Family(TextureDecoder.Info info) => info.Dxgi switch
    {
        70 or 71 or 72 => 1, 73 or 74 or 75 => 2, 76 or 77 or 78 => 3,
        79 or 80 => 4, 82 or 83 => 5, 97 or 98 or 99 => 7,
        27 or 28 or 29 => 101, 87 or 90 or 91 => 102, 88 or 92 or 93 => 103,
        61 => 104, 35 => 105,
        _ => info.FourCC switch { "DXT1" => 1, "DXT3" => 2, "DXT5" => 3, "ATI1" or "BC4U" => 4, "ATI2" or "BC5U" => 5, _ => 0 }
    };
    static int LevelSize(int family, int w, int h) => family < 100
        ? checked(((w + 3) / 4) * ((h + 3) / 4) * (family is 1 or 4 ? 8 : 16))
        : checked(w * h * (family == 104 ? 1 : 4));
    public static void ValidateTexture(byte[] data)
    {
        var info = TextureDecoder.Parse(data); int family = Family(info);
        if (family == 0 || info.MultipleSurfaces || (info.Offset == 148 && BitConverter.ToInt32(data,132) != 3))
            throw Error("仅支持已识别格式的二维单表面纹理导入。", "Import requires a supported single-surface 2D texture.");
        int maxMips = 1 + (int)Math.Log2(Math.Max(info.Width, info.Height));
        if (info.Mips > maxMips) throw Error("纹理 mip 数量无效。", "Invalid texture mip count.");
        long size = 0; int w = info.Width, h = info.Height;
        for (int i = 0; i < info.Mips; i++) { size += LevelSize(family,w,h); w = Math.Max(1,w/2); h = Math.Max(1,h/2); }
        if (size != data.Length - info.Offset) throw Error("纹理像素长度与尺寸、格式或 mip 数量不一致。", "Texture payload size does not match dimensions, format and mip count.");
    }
    public static byte[] NormalizeDds(byte[] original, byte[] replacement)
    {
        ValidateTexture(original); ValidateTexture(replacement);
        var a = TextureDecoder.Parse(original); var b = TextureDecoder.Parse(replacement);
        if (a.Width != b.Width || a.Height != b.Height || a.Mips != b.Mips || Family(a) != Family(b))
            throw Error("替换纹理的尺寸、mip 数量及像素格式必须与原资源一致。", "Replacement dimensions, mip count and pixel format must match the original.");
        var result = new byte[original.Length]; original.AsSpan(0,a.Offset).CopyTo(result);
        replacement.AsSpan(b.Offset).CopyTo(result.AsSpan(a.Offset)); return result;
    }
    public static byte[] EncodePng(byte[] original, string path)
    {
        ValidateTexture(original); var info = TextureDecoder.Parse(original); int family = Family(info);
        if (family == 105) throw Error("RG16 请使用 DDS/TEX 导入，以保留 16 位通道精度。", "Import RG16 via DDS/TEX to retain 16-bit precision.");
        using var src = new Bitmap(path);
        if (src.Width != info.Width || src.Height != info.Height) throw Error("PNG 尺寸必须与原纹理一致。", "PNG dimensions must match the original texture.");
        if ((long)src.Width * src.Height > 67108864) throw Error("PNG 不得超过 6400 万像素。", "PNG exceeds 64 megapixels.");
        using var bitmap = new Bitmap(src.Width,src.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap)) { g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy; g.DrawImageUnscaled(src,0,0); }
        var rgba = new byte[checked(src.Width * src.Height * 4)];
        var bits = bitmap.LockBits(new Rectangle(0,0,src.Width,src.Height),ImageLockMode.ReadOnly,bitmap.PixelFormat);
        try { for(int y=0;y<src.Height;y++) Marshal.Copy(bits.Scan0+y*bits.Stride,rgba,y*src.Width*4,src.Width*4); }
        finally { bitmap.UnlockBits(bits); }
        for(int i=0;i<rgba.Length;i+=4) (rgba[i],rgba[i+2])=(rgba[i+2],rgba[i]);
        using var output = new MemoryStream(); output.Write(original,0,info.Offset);
        int w=info.Width,h=info.Height;
        for(int level=0;level<info.Mips;level++)
        {
            if(family<100)
            {
                var encoder = new BcEncoder(); encoder.OutputOptions.GenerateMipMaps=false;
                encoder.OutputOptions.Quality=CompressionQuality.Balanced;
                encoder.OutputOptions.Format=family switch {1=>CompressionFormat.Bc1WithAlpha,2=>CompressionFormat.Bc2,3=>CompressionFormat.Bc3,4=>CompressionFormat.Bc4,5=>CompressionFormat.Bc5,7=>CompressionFormat.Bc7,_=>throw new NotSupportedException()};
                output.Write(encoder.EncodeToRawBytes(rgba,w,h,BCnEncoder.Encoder.PixelFormat.Rgba32)[0]);
            }
            else for(int i=0;i<rgba.Length;i+=4)
            {
                if(family==104) output.WriteByte(rgba[i]);
                else if(family==101) output.Write(rgba,i,4);
                else { output.WriteByte(rgba[i+2]);output.WriteByte(rgba[i+1]);output.WriteByte(rgba[i]);output.WriteByte(family==103?(byte)255:rgba[i+3]); }
            }
            if(level+1<info.Mips)
            {
                int nw=Math.Max(1,w/2),nh=Math.Max(1,h/2);var next=new byte[nw*nh*4];
                for(int y=0;y<nh;y++)for(int x=0;x<nw;x++)for(int c=0;c<4;c++)
                { int sum=0;for(int yy=0;yy<2;yy++)for(int xx=0;xx<2;xx++)sum+=rgba[(Math.Min(h-1,y*2+yy)*w+Math.Min(w-1,x*2+xx))*4+c];next[(y*nw+x)*4+c]=(byte)(sum/4); }
                rgba=next;w=nw;h=nh;
            }
        }
        var result=output.ToArray();ValidateTexture(result);return result;
    }
}
