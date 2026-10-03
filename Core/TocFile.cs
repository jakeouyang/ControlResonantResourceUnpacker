using System.Text;

namespace RmdblobUnpacker.Core;

public sealed class ResourceEntry
{
    public int EntryIndex;
    public string Pack;          // toc file name without extension, e.g. "base-generic"
    public string Path;          // full virtual path (pack-relative, root segment stripped)
    public string Name => Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path;
    public string Key => Pack + "/" + Path;
    public long Size;            // uncompressed size
    public uint Crc32;           // from DMKP record
    public int RecOff;           // offset of the DMKP record inside the entries region
    public int RecLen;
    public int DirNode;
    public int DescOff;          // offset of chunk descriptors inside the descriptor table
    public int DescBytes;
    public ChunkDesc[] Chunks;
    public bool EngineCompressed; // unsupported storage types only
}

public struct ChunkDesc
{
    public long Offset;     // absolute offset inside blob file (40-bit)
    public int BlobIdx;
    public ushort Flags;
    public int UncSize;
    public int CompSize;    // 0 => stored raw
}

public sealed class BlobInfo
{
    public string TocPath;      // full path of .rmdtoc
    public string Pack;         // name without extension
    public int BlobCount;
    public int EntryCount;
}

/// <summary>Parsed .rmdtoc + associated blob files.</summary>
public sealed class TocFile
{
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    public string TocPath;
    public string Pack;
    public byte[] Body;
    public int BlobCount;
    public int Count;
    public int StrStart, StrEnd;
    public int EntOff, IdxOff, IdxSize, InfoStart;
    public string[] DirPaths;
    public List<ResourceEntry> Entries = new();
    public string[] BlobFiles;
    public byte[] Header = Array.Empty<byte>();   // first 0x1000 bytes of the .rmdtoc file

    static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);

    public static IEnumerable<BlobInfo> Discover(string pcDir)
    {
        foreach (var f in Directory.EnumerateFiles(pcDir, "*.rmdtoc"))
        {
            var name = Path.GetFileNameWithoutExtension(f);
            int blobs = 0, entries = 0;
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read);
                var hdr = new byte[0x120];
                int n = fs.Read(hdr, 0, hdr.Length);
                if (n < 0x120 || BitConverter.ToUInt32(hdr, 0) != 0x52544F43) continue; // "RTOC" LE
                blobs = BitConverter.ToInt32(hdr, 0x14);
                entries = BitConverter.ToInt32(hdr, 0x24);
            }
            catch { continue; }
            yield return new BlobInfo { TocPath = f, Pack = name, BlobCount = blobs, EntryCount = entries };
        }
    }

    public static TocFile Load(string tocPath)
    {
        var tf = new TocFile();
        tf.TocPath = tocPath;
        tf.Pack = Path.GetFileNameWithoutExtension(tocPath);
        var raw = File.ReadAllBytes(tocPath);
        if (raw.Length < 0x110 || BitConverter.ToUInt32(raw, 0) != 0x52544F43)
            throw new InvalidDataException("not an RTOC file: " + tocPath);
        tf.Header = raw[..0x1000];

        tf.BlobCount   = (int)U32(raw, 0x14);
        int nodeCount  = (int)U32(raw, 0x1C);
        tf.InfoStart   = (int)U32(raw, 0x20);
        tf.Count       = (int)U32(raw, 0x24);
        tf.StrStart    = (int)U32(raw, 0x28);
        tf.StrEnd      = (int)U32(raw, 0x30);
        tf.EntOff      = (int)U32(raw, 0x38);
        tf.IdxOff      = (int)U32(raw, 0x50);
        tf.IdxSize     = (int)U32(raw, 0x54);
        int nBlocks    = (int)(U32(raw, 0x0C) / 16);
        int total      = tf.IdxOff + tf.IdxSize;

        // decompress body block-by-block using the header block table
        tf.Body = new byte[total];
        int ip = 0x1000, op = 0;
        for (int i = 0; i < nBlocks && op < total; i++)
        {
            int baseOff = 0x60 + i * 16;
            int unc  = (int)U32(raw, baseOff);
            int comp = (int)U32(raw, baseOff + 4);
            uint f0  = U32(raw, baseOff + 8);
            uint f1  = U32(raw, baseOff + 12);
            if (comp <= 0 || ip + comp > raw.Length) throw new InvalidDataException("bad block table");
            Lz4Block.Decompress(raw.AsSpan(ip, comp), tf.Body.AsSpan(op, unc));
            op += unc;
            int next = (int)((f1 << 8) | ((f0 >> 24) & 0xFF));
            ip = next == 0 ? ip + comp : next;
        }
        if (op != total) throw new InvalidDataException("toc body size mismatch");

        var body = tf.Body;

        // directory tree: node = 7 x u32 (parent, ?, ?, ?, ?, nameOff, nameLen)
        int nodeOff = tf.BlobCount * 24;
        var nodes = new (int parent, int no, int nl)[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            int o = nodeOff + i * 28;
            nodes[i] = ((int)U32(body, o), (int)U32(body, o + 20), (int)U32(body, o + 24));
        }
        string NodeName(int i)
        {
            var (no, nl) = (nodes[i].no, nodes[i].nl);
            // name offsets are relative to the string pool
            if (no < 0 || nl < 0 || no + nl > tf.StrEnd - tf.StrStart) return "?";
            return Utf8.GetString(body, tf.StrStart + no, nl);
        }
        tf.DirPaths = new string[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            var parts = new List<string>();
            int cur = i, guard = 0;
            while (cur >= 0 && cur < nodeCount && guard++ < 128)
            {
                if (cur == 0) break; // root = "../pc/<pack>-000.rmdblob", stripped
                parts.Add(NodeName(cur));
                cur = nodes[cur].parent;
                if (cur == i) break;
            }
            parts.Reverse();
            tf.DirPaths[i] = string.Join('/', parts);
        }

        // string pool
        var pool = body.AsMemory(tf.StrStart, tf.StrEnd - tf.StrStart);

        // Blob indices address the descriptor table, including generic/PC cross-pack paths.
        tf.BlobFiles = new string[tf.BlobCount];
        string dir = Path.GetDirectoryName(tocPath)!;
        for (int i = 0; i < tf.BlobCount; i++)
        {
            int o = i * 24;
            string relative = Utf8.GetString(pool.Span.Slice((int)U32(body, o), (int)U32(body, o + 4)));
            tf.BlobFiles[i] = Path.GetFullPath(Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        tf.Entries.Capacity = tf.Count;
        for (int k = 0; k < tf.Count; k++)
        {
            int o = tf.InfoStart + k * 32;
            int descOff = (int)U32(body, o);
            int descLen = (int)U32(body, o + 4);
            int dirNode = (int)U32(body, o + 8);
            int nameOff = (int)U32(body, o + 12);
            int nameLen = (int)U32(body, o + 16);
            long size   = U32(body, o + 20);
            int recOff  = (int)U32(body, o + 24);
            int recLen  = (int)U32(body, o + 28);

            if (dirNode < 0 || dirNode >= nodeCount) continue;
            string name = Utf8.GetString(pool.Span.Slice(nameOff, nameLen));
            string dirPath = tf.DirPaths[dirNode];
            string full = string.IsNullOrEmpty(dirPath) ? name : dirPath + "/" + name;

            var chunks = tf.ReadChunks(descOff, descLen);
            bool engine = chunks.Any(c => c.Flags != 0 && c.Flags != 0x10);

            // DMKP metadata starts after a variable count of 8-byte field descriptors.
            // BINFBX has four descriptors; most other resources have three.
            uint crc = 0;
            int record = tf.EntOff + recOff;
            if (recLen >= 16 && record >= 0 && (long)record + recLen <= body.Length &&
                body.AsSpan(record, 4).SequenceEqual("DMKP"u8))
            {
                long metadata = 8L + U32(body, record + 4) * 8L;
                if (metadata + 16 <= recLen && U32(body, record + (int)metadata) == 1 &&
                    BitConverter.ToUInt64(body, record + (int)metadata + 4) == (ulong)size)
                    crc = U32(body, record + (int)metadata + 12);
            }

            tf.Entries.Add(new ResourceEntry
            {
                EntryIndex = k,
                Pack = tf.Pack,
                Path = full,
                Size = size,
                Crc32 = crc,
                RecOff = recOff, RecLen = recLen,
                DirNode = dirNode,
                DescOff = descOff,
                DescBytes = descLen,
                Chunks = chunks,
                EngineCompressed = engine,
            });
        }
        return tf;
    }

    public ChunkDesc[] Chunks(ResourceEntry f) => ReadChunks(f.DescOff, f.DescBytes);

    ChunkDesc[] ReadChunks(int offset, int length)
    {
        if (offset < 0 || length < 0 || (long)offset + length > IdxSize)
            throw new InvalidDataException("Chunk descriptors outside table");
        var chunks = new List<ChunkDesc>();
        int end = checked(IdxOff + offset + length);
        for (int o = IdxOff + offset; o < end;)
        {
            byte storage = Body[o];
            int recordSize = ((storage & 15) + 2) * 8;
            if (o + recordSize > end) throw new InvalidDataException("Truncated chunk descriptor");
            ulong q = BitConverter.ToUInt64(Body, o);
            int unc = checked((int)U32(Body, o + 8));
            chunks.Add(new ChunkDesc {
                Offset = (long)(q >> 24), BlobIdx = (int)((q >> 8) & 0xFFFF),
                Flags = storage, UncSize = unc,
                CompSize = storage == 0 ? 0 : checked((int)U32(Body, o + 12))
            });
            o += recordSize;
        }
        return chunks.ToArray();
    }

    /// <summary>Decode stored/LZ4 chunks. Unknown storage is explicitly returned as raw.</summary>
    public (byte[] data, bool raw) Extract(ResourceEntry e)
    {
        bool raw = e.EngineCompressed;
        using var output = new MemoryStream();
        foreach (var c in e.Chunks)
        {
            if (c.BlobIdx < 0 || c.BlobIdx >= BlobFiles.Length)
                throw new InvalidDataException($"Invalid blob index {c.BlobIdx}");
            using var input = File.OpenRead(BlobFiles[c.BlobIdx]);
            input.Position = c.Offset;
            int size = c.Flags == 0 ? c.UncSize : c.CompSize;
            if (size < 0 || size > input.Length - input.Position)
                throw new InvalidDataException("Chunk extends beyond blob");
            var bytes = new byte[size];
            input.ReadExactly(bytes);
            if (!raw && c.Flags == 0x10) bytes = Lz4Block.Decompress(bytes, c.UncSize);
            output.Write(bytes);
        }
        if (!raw && output.Length != e.Size) throw new InvalidDataException("Resource size mismatch: " + e.Path);
        return (output.ToArray(), raw);
    }
}
