using System.Text;

namespace RmdblobUnpacker.Core;

/// <summary>Raw LZ4 block decoder (no frame, no size prefix).</summary>
public static class Lz4Block
{
    public static void Decompress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        int ip = 0, op = 0;
        int oEnd = output.Length, iEnd = input.Length;
        while (op < oEnd)
        {
            if (ip >= iEnd) throw new InvalidDataException("LZ4: unexpected end of input");
            byte token = input[ip++];
            int lit = token >> 4;
            if (lit == 15)
            {
                byte b;
                do { if (ip >= iEnd) throw new InvalidDataException("LZ4: truncated length"); b = input[ip++]; lit = checked(lit + b); } while (b == 255);
            }
            if (lit > iEnd - ip || lit > oEnd - op) throw new InvalidDataException("LZ4: literal overflow");
            input.Slice(ip, lit).CopyTo(output.Slice(op));
            ip += lit; op += lit;
            if (op >= oEnd) break;
            if (iEnd - ip < 2) throw new InvalidDataException("LZ4: missing offset");
            int off = input[ip] | (input[ip + 1] << 8); ip += 2;
            if (off == 0 || off > op) throw new InvalidDataException($"LZ4: bad offset {off}");
            int ml = (token & 0xF) + 4;
            if ((token & 0xF) == 15)
            {
                byte b;
                do { if (ip >= iEnd) throw new InvalidDataException("LZ4: truncated length"); b = input[ip++]; ml = checked(ml + b); } while (b == 255);
            }
            int s = op - off;
            if (ml > oEnd - op) throw new InvalidDataException("LZ4: match overflow");
            for (int i = 0; i < ml; i++) output[op + i] = output[s + i];
            op += ml;
        }
    }

    public static byte[] Decompress(ReadOnlySpan<byte> input, int uncSize)
    {
        var outBuf = new byte[uncSize];
        Decompress(input, outBuf);
        return outBuf;
    }
}

/// <summary>Standard CRC32 (IEEE, same as zlib).</summary>
public static class Crc32
{
    static readonly uint[] Table = Build();
    static uint[] Build()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
