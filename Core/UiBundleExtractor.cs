using System.Text;

namespace RmdblobUnpacker.Core;

public sealed record UiBundleEntry(string Path, int Offset, int Size, bool External);
public sealed record UiBundle(UiBundleEntry[] Entries);
public sealed record UiBundleExport(int Svg, int Html, int JavaScript, int ExternalReferences, int OtherEmbedded)
{
    public int Files => Svg + Html + JavaScript;
}

/// <summary>Reads the verified version-2 UI bundle resource table, not Qt Designer .ui files.</summary>
public static class UiBundleExtractor
{
    const uint Magic = 0xd34db33f;
    static readonly UTF8Encoding Utf8 = new(false, true);
    static InvalidDataException Error(string message) => new("UI bundle: " + message);

    sealed class Reader
    {
        readonly byte[] data;
        public int Position;
        public Reader(byte[] data) { this.data = data; }
        public void Need(int size)
        {
            if (size < 0 || size > data.Length - Position) throw Error("truncated data");
        }
        public uint U32() { Need(4); uint n = BitConverter.ToUInt32(data, Position); Position += 4; return n; }
        public ulong U64() { Need(8); ulong n = BitConverter.ToUInt64(data, Position); Position += 8; return n; }
        public int Count(int max)
        {
            uint n = U32(); if (n > max) throw Error("section length/count out of range"); return (int)n;
        }
        public void Expect(uint value) { if (U32() != value) throw Error("unsupported or damaged layout"); }
        public int Header(uint type, uint fields)
        {
            Expect(Magic); Expect(2); int size = Count(data.Length); Expect(type); Expect(fields); return size;
        }
        public string Text()
        {
            int size = Count(32768); Need(size);
            try { string text = Utf8.GetString(data, Position, size); Position += size; return text; }
            catch (DecoderFallbackException) { throw Error("invalid UTF-8 path"); }
        }
    }

    public static UiBundle Parse(byte[] data, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var reader = new Reader(data);
        if (reader.Header(0x70635a61, 7) != data.Length) throw Error("bundle length mismatch");
        // Serialized entry-point path; the fixed size identifies its wrapper, not the variable UTF-8 string.
        if (reader.Header(0xd0b4291c, 1) != 24) throw Error("unsupported entry-point wrapper");
        reader.Expect(Magic); ValidatePath(reader.Text());
        int count = reader.Count(100000);
        if (count > (data.Length - reader.Position) / 77) throw Error("truncated resource table");
        var entries = new (string Path, ulong Offset, ulong Size, bool External)[count];
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); int start = reader.Position;
            int size = reader.Header(0x4edae845, 3);
            string path = reader.Text(); ValidatePath(path);
            if (reader.Header(0x1bfc72d5, 1) != 32) throw Error("unsupported resource reference");
            ulong reference = reader.U64(); reader.Expect(Magic);
            ulong offset = reader.U64(), length = reader.U64(); reader.Expect(Magic);
            if (reader.Position - start != size) throw Error("resource record length mismatch");
            bool external = reference != ulong.MaxValue;
            if (external && (offset != 0 || length != 0)) throw Error("external reference contains embedded data");
            entries[i] = (path, offset, length, external);
        }
        int payloadSize = reader.Count(data.Length), payload = reader.Position;
        reader.Need(payloadSize);
        // Four additional runtime sections follow the payload. They are preserved in the original .ui.
        if (payloadSize > data.Length - payload - 4 || BitConverter.ToUInt32(data, data.Length - 4) != Magic)
            throw Error("truncated bundle trailer");
        var result = new UiBundleEntry[count]; ulong cursor = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); var entry = entries[i];
            if (!entry.External)
            {
                if (entry.Offset != cursor || entry.Offset > (ulong)payloadSize || entry.Size > (ulong)payloadSize - entry.Offset)
                    throw Error("embedded resource range/coverage mismatch");
                cursor = entry.Offset + entry.Size;
                if (!names.Add(entry.Path)) throw Error("duplicate embedded output path");
            }
            result[i] = new(entry.Path, entry.External ? 0 : checked(payload + (int)entry.Offset), (int)entry.Size, entry.External);
        }
        if (cursor != (ulong)payloadSize) throw Error("unaccounted embedded payload");
        foreach (string name in names)
            for (int slash = name.LastIndexOf('/'); slash >= 0; slash = name.LastIndexOf('/', slash - 1))
                if (names.Contains(name[..slash])) throw Error("embedded file/directory path collision");
        return new(result);
    }

    static string Kind(string path) => Path.GetExtension(path).ToLowerInvariant();
    static bool Supported(string path) => Kind(path) is ".svg" or ".html" or ".js";

    public static UiBundleExport Extract(byte[] data, string destination, CancellationToken token = default)
    {
        // Finish validating the entire table and all ranges before creating any files.
        var bundle = Parse(data, token);
        string root = Path.GetFullPath(destination);
        var selected = bundle.Entries.Where(e => !e.External && Supported(e.Path)).ToArray();
        var outputs = selected.Select(e => Path.GetFullPath(Path.Combine(root, e.Path.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        foreach (string output in outputs)
        {
            token.ThrowIfCancellationRequested();
            if (!output.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw Error("output outside destination");
            CheckLinks(output);
            if (Directory.Exists(output)) throw new IOException("UI output is a directory: " + output);
        }
        for (int i = 0; i < selected.Length; i++)
        {
            token.ThrowIfCancellationRequested(); string output = outputs[i]; CheckLinks(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, data.AsSpan(selected[i].Offset, selected[i].Size).ToArray());
                token.ThrowIfCancellationRequested(); CheckLinks(output); File.Move(temp, output, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return new(selected.Count(e => Kind(e.Path) == ".svg"), selected.Count(e => Kind(e.Path) == ".html"),
            selected.Count(e => Kind(e.Path) == ".js"), bundle.Entries.Count(e => e.External),
            bundle.Entries.Count(e => !e.External && !Supported(e.Path)));
    }

    static void ValidatePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\')) throw Error("invalid resource path");
        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c))) throw Error("unsafe resource path");
            string name = part.Split('.')[0].ToUpperInvariant();
            if (name is "CON" or "PRN" or "AUX" or "NUL" || name.Length == 4 && name[3] is >= '0' and <= '9' &&
                (name.StartsWith("COM") || name.StartsWith("LPT"))) throw Error("reserved resource path");
        }
    }

    static void CheckLinks(string path)
    {
        for (string current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("UI output crosses a symbolic link/junction: " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
