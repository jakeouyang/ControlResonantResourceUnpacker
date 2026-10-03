using BCnEncoder.Decoder;
using BCnEncoder.Shared;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace RmdblobUnpacker.Core;

/// <summary>DDS containers stored as TEX. Reference: Resonant Mod Tool 0.2.9 (MIT).
/// PNG represents mip zero, first surface; DDS retains all mips, faces and HDR data.</summary>
public static class TextureDecoder
{
    public record Info(int Width, int Height, int Offset, int Dxgi, string FourCC, int Mips, bool MultipleSurfaces)
    {
        public override string ToString() => $"{Width} × {Height}, {(Dxgi != 0 ? "DXGI " + Dxgi : FourCC)}, {Mips} mip(s)";
    }
    static int U(byte[] b, int o) => checked((int)BitConverter.ToUInt32(b, o));
    public static bool IsDds(byte[] data) => data.Length >= 4 && data.AsSpan(0, 4).SequenceEqual("DDS "u8);
    public static Info Parse(byte[] data)
    {
        if (data.Length < 128 || !IsDds(data) || U(data, 4) != 124 || U(data, 76) != 32)
            throw new InvalidDataException("Invalid DDS header");
        string four = Encoding.ASCII.GetString(data, 84, 4);
        bool dx10 = four == "DX10";
        if (dx10 && data.Length < 148) throw new InvalidDataException("Truncated DX10 header");
        int w = U(data, 16), h = U(data, 12);
        if (w <= 0 || h <= 0 || w > 32768 || h > 32768) throw new InvalidDataException("Invalid texture dimensions");
        return new Info(w, h, dx10 ? 148 : 128, dx10 ? U(data, 128) : 0, four,
            Math.Max(1, U(data, 28)), U(data, 24) > 1 || (U(data, 112) & 0x200) != 0 ||
            (dx10 && (U(data, 140) > 1 || (U(data, 136) & 4) != 0)));
    }
    public static void SavePng(byte[] data, string path)
    {
        var info = Parse(data);
        if (info.Dxgi != 35) { using var bitmap = Decode(data); bitmap.Save(path, ImageFormat.Png); return; }
        // Preserve both 16-bit UNORM channels in an RGBA16 PNG (R/G, B=0, A=65535).
        int stride = checked(info.Width * 4);
        if ((U(data, 8) & 8) != 0) stride = Math.Max(stride, U(data, 20));
        if ((long)stride * info.Height > data.Length - info.Offset) throw new InvalidDataException("Truncated DDS pixels");
        using var output = File.Create(path);
        output.Write(new byte[] {137,80,78,71,13,10,26,10});
        void Chunk(string name, byte[] bytes)
        {
            var length = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            output.Write(length); var type = Encoding.ASCII.GetBytes(name); output.Write(type); output.Write(bytes);
            var crcData = new byte[4 + bytes.Length]; type.CopyTo(crcData, 0); bytes.CopyTo(crcData, 4);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, Crc32.Compute(crcData)); output.Write(length);
        }
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), info.Width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), info.Height);
        header[8] = 16; header[9] = 6; Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Optimal, true))
        {
            var row = new byte[checked(info.Width * 8 + 1)];
            for (int y = 0; y < info.Height; y++)
            {
                for (int x = 0; x < info.Width; x++)
                {
                    int src = info.Offset + y * stride + x * 4, dst = 1 + x * 8;
                    row[dst] = data[src+1]; row[dst+1] = data[src]; row[dst+2] = data[src+3]; row[dst+3] = data[src+2];
                    row[dst+6] = row[dst+7] = 255;
                }
                zlib.Write(row);
            }
        }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
    }

    public static Bitmap Decode(byte[] data)
    {
        var info = Parse(data);
        int w = info.Width, h = info.Height;
        if ((long)w * h > 67108864) throw new NotSupportedException("PNG/preview limited to 64 megapixels; DDS remains available.");
        CompressionFormat? format = info.Dxgi switch {
            70 or 71 or 72 => CompressionFormat.Bc1WithAlpha,
            73 or 74 or 75 => CompressionFormat.Bc2,
            76 or 77 or 78 => CompressionFormat.Bc3,
            79 or 80 => CompressionFormat.Bc4,
            82 or 83 => CompressionFormat.Bc5,
            97 or 98 or 99 => CompressionFormat.Bc7,
            _ => info.FourCC switch { "DXT1" => CompressionFormat.Bc1WithAlpha, "DXT3" => CompressionFormat.Bc2,
                "DXT5" => CompressionFormat.Bc3, "ATI1" or "BC4U" => CompressionFormat.Bc4,
                "ATI2" or "BC5U" => CompressionFormat.Bc5, _ => null }
        };
        var bgra = new byte[checked(w * h * 4)];
        if (format.HasValue)
        {
            int blockBytes = format == CompressionFormat.Bc1WithAlpha || format == CompressionFormat.Bc4 ? 8 : 16;
            int length = checked(((w + 3) / 4) * ((h + 3) / 4) * blockBytes);
            if (length > data.Length - info.Offset) throw new InvalidDataException("Truncated DDS pixels");
            var decoder = new BcDecoder();
            var pixels = decoder.DecodeRaw(data.AsSpan(info.Offset, length).ToArray(), w, h, format.Value);
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i]; int o = i * 4;
                bgra[o] = format == CompressionFormat.Bc4 ? c.r : format == CompressionFormat.Bc5 ? (byte)0 : c.b;
                bgra[o + 1] = format == CompressionFormat.Bc4 ? c.r : c.g;
                bgra[o + 2] = c.r; bgra[o + 3] = c.a;
            }
        }
        else
        {
            bool rgba = info.Dxgi is 27 or 28 or 29;
            bool bgr = info.Dxgi is 87 or 88 or 90 or 91 or 92 or 93;
            bool r8 = info.Dxgi is 61;
            bool rg16 = info.Dxgi == 35;
            if (!rgba && !bgr && !r8 && !rg16) throw new NotSupportedException($"PNG/preview unavailable for {info}; use the lossless DDS export.");
            int bpp = r8 ? 1 : 4;
            int stride = w * bpp;
            if ((U(data, 8) & 8) != 0) stride = Math.Max(stride, U(data, 20));
            if ((long)stride * h > data.Length - info.Offset) throw new InvalidDataException("Truncated DDS pixels");
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int src = info.Offset + y * stride + x * bpp, o = (y * w + x) * 4;
                if (rg16) { bgra[o] = 0; bgra[o + 1] = data[src + 3]; bgra[o + 2] = data[src + 1]; bgra[o + 3] = 255; continue; }
                bgra[o] = data[src + (rgba ? 2 : 0)];
                bgra[o + 1] = data[src + (r8 ? 0 : 1)];
                bgra[o + 2] = data[src + (bgr ? 2 : 0)];
                bgra[o + 3] = r8 || info.Dxgi is 88 or 92 or 93 ? (byte)255 : data[src + 3];
            }
        }
        var image = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bits = image.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, image.PixelFormat);
        try { for (int y = 0; y < h; y++) Marshal.Copy(bgra, y * w * 4, bits.Scan0 + y * bits.Stride, w * 4); }
        finally { image.UnlockBits(bits); }
        return image;
    }
}
