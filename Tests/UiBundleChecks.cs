using System.Text;
using System.Xml;
using System.Xml.Linq;
using RmdblobUnpacker.Core;

static class UiBundleChecks
{
    const uint Magic = 0xd34db33f;
    const string SvgPath = "uiresources/game/symbols/icons/menu.svg";
    const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 36 36\"><path d=\"M0 0L36 36\"/></svg>";
    static int checks;
    static string root;
    sealed record Item(string Path, string Content, bool External = false);
    sealed record Fixture(byte[] Data, int CountAt, int RecordAt, int PathAt, int OffsetAt, int PayloadLengthAt);

    static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception(name); checks++; Console.WriteLine("PASS " + name);
    }
    static void Reject(byte[] data, string name)
    {
        string destination = Path.Combine(root, "reject-" + Guid.NewGuid().ToString("N"));
        try { UiBundleExtractor.Extract(data, destination); }
        catch (InvalidDataException) { Assert(!Directory.Exists(destination), "rejects " + name + " before writing"); return; }
        throw new Exception("Accepted " + name);
    }
    static byte[] Change(Fixture f, int at, ulong value, bool wide = false)
    {
        var data = (byte[])f.Data.Clone();
        (wide ? BitConverter.GetBytes(value) : BitConverter.GetBytes((uint)value)).CopyTo(data, at); return data;
    }
    static Fixture Build(params Item[] items)
    {
        using var stream = new MemoryStream(); using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        void Header(int size, uint type, uint fields) { w.Write(Magic); w.Write(2); w.Write(size); w.Write(type); w.Write(fields); }
        void Text(string s) { byte[] b = Encoding.UTF8.GetBytes(s); w.Write(b.Length); w.Write(b); }
        Header(0, 0x70635a61, 7); Header(24, 0xd0b4291c, 1); w.Write(Magic); Text("uiresources/game/ui/ui.html");
        int countAt = (int)stream.Position; w.Write(items.Length);
        int recordAt = (int)stream.Position, pathAt = recordAt + 24, offsetAt = 0; ulong offset = 0;
        foreach (var item in items)
        {
            Header(76 + Encoding.UTF8.GetByteCount(item.Path), 0x4edae845, 3); Text(item.Path);
            Header(32, 0x1bfc72d5, 1); w.Write(item.External ? 1234UL : ulong.MaxValue); w.Write(Magic);
            if (offsetAt == 0) offsetAt = (int)stream.Position;
            ulong size = item.External ? 0 : (ulong)Encoding.UTF8.GetByteCount(item.Content);
            w.Write(item.External ? 0UL : offset); w.Write(size); w.Write(Magic); offset += size;
        }
        int payloadAt = (int)stream.Position; w.Write((int)offset);
        foreach (var item in items.Where(x => !x.External)) w.Write(Encoding.UTF8.GetBytes(item.Content));
        // The extractor preserves rather than interprets the remaining runtime sections.
        w.Write(new byte[16]); w.Write(Magic); w.Flush();
        byte[] data = stream.ToArray(); BitConverter.GetBytes(data.Length).CopyTo(data, 8);
        return new(data, countAt, recordAt, pathAt, offsetAt, payloadAt);
    }
    static Fixture Basic() => Build(new(SvgPath, Svg), new("uiresources/game/ui/ui.html", "<!doctype html><html><body>菜单</body></html>"),
        new("uiresources/game/ui/main.js", "const title = '菜单';\r\n"), new("textures/external.png", "", true), new("uiresources/game/other.css", "body {}"));
    static string ExportOriginal(byte[] data, string output, bool raw = false)
    {
        string blob = Path.Combine(root, "fixture-" + Guid.NewGuid().ToString("N") + ".rmdblob"); File.WriteAllBytes(blob, data);
        var toc = new TocFile { BlobFiles = new[] { blob } };
        var entry = new ResourceEntry { Pack = "base-generic", Path = "data/uiresources/game/ui/ui.ui", Size = data.Length,
            Crc32 = Crc32.Compute(data), EngineCompressed = raw,
            Chunks = new[] { new ChunkDesc { Flags = (ushort)(raw ? 0x11 : 0), CompSize = data.Length, UncSize = data.Length } } };
        return ResourceExporter.Write(toc, entry, output);
    }
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--selection")) { BulkSelectionChecks.Run(args); return; }
        root = Path.Combine(Path.GetTempPath(), "ResonantUiChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var f = Basic(); string output = Path.Combine(root, "contents");
            var summary = UiBundleExtractor.Extract(f.Data, output);
            Assert(summary.Files == 3 && summary.Svg == 1 && summary.Html == 1 && summary.JavaScript == 1 && summary.ExternalReferences == 1 && summary.OtherEmbedded == 1, "embedded/exported/reference counts");
            Assert(File.ReadAllText(Path.Combine(output, SvgPath)) == Svg && Directory.GetFiles(output, "*", SearchOption.AllDirectories).Length == 3, "nested paths and exact SVG contents; no placeholder references");
            Assert(File.ReadAllBytes(Path.Combine(output, "uiresources/game/ui/main.js")).SequenceEqual(Encoding.UTF8.GetBytes("const title = '菜单';\r\n")), "UTF-8 and original line endings preserved");
            var parsed = UiBundleExtractor.Parse(f.Data); Assert(parsed.Entries.Length == 5, "all resource records parsed");
            File.WriteAllText(Path.Combine(output, "keep.txt"), "keep"); UiBundleExtractor.Extract(f.Data, output);
            Assert(File.ReadAllText(Path.Combine(output, "keep.txt")) == "keep", "repeat export preserves unrelated existing files");
            string integrated = Path.Combine(root, "integrated"), relative = "base-generic/data/uiresources/game/ui/ui.ui";
            string row = ExportOriginal(f.Data, integrated);
            Assert(File.ReadAllBytes(Path.Combine(integrated, relative)).SequenceEqual(f.Data) && File.Exists(Path.Combine(integrated, relative + ".contents", SvgPath)), "shared GUI/CLI exporter keeps original and expands UI");
            Assert(row.Contains("1 SVG, 1 HTML, 1 JavaScript") && row.Contains("1 external references"), "manifest records nested extraction and skipped references");
            string bad = Path.Combine(root, "unsupported"); string badRow = ExportOriginal("<ui version=\"4.0\"/>"u8.ToArray(), bad);
            Assert(File.Exists(Path.Combine(bad, relative)) && !Directory.Exists(Path.Combine(bad, relative + ".contents")) && badRow.Contains("original UI retained"), "unsupported UI retained with diagnostic manifest");
            string rawOut = Path.Combine(root, "raw"); ExportOriginal(f.Data, rawOut, true);
            Assert(File.Exists(Path.Combine(rawOut, relative + ".raw")) && !Directory.Exists(Path.Combine(rawOut, relative + ".raw.contents")), "unknown outer compression is not interpreted as decoded UI");
            Reject(Change(f, 0, 0), "bad signature"); Reject(Change(f, 4, 3), "unsupported version");
            Reject(Change(f, 8, 42), "wrong container size"); Reject(Change(f, f.CountAt, uint.MaxValue), "impossible record count");
            Reject(Change(f, f.RecordAt + 8, 22), "wrong record size");
            Reject(Change(f, f.OffsetAt, ulong.MaxValue, true), "overflowing embedded offset");
            Reject(Change(f, f.OffsetAt + 8, ulong.MaxValue, true), "overflowing embedded size");
            Reject(Change(f, f.OffsetAt, 1, true), "overlapping/gapped payload");
            Reject(Change(f, f.PayloadLengthAt, 1), "payload smaller than embedded resource");
            Reject(Change(f, f.PayloadLengthAt, (uint)(f.Data.Length - f.PayloadLengthAt - 7)), "payload overlaps trailer");
            Reject(Change(f, f.Data.Length - 4, 0), "bad trailer");
            var utf8 = (byte[])f.Data.Clone(); utf8[f.PathAt] = 0xff; Reject(utf8, "invalid UTF-8 path");
            for (int cut = 0; cut < f.Data.Length; cut += 19) Reject(f.Data[..cut], "truncation at " + cut);
            foreach (string path in new[] { "../outside.svg", "/absolute.svg", "C:/outside.svg", "a/../../escape.svg", "a\\escape.svg", "a//b.svg", "a/file.svg:stream", "a/file.svg.", "a/CON.svg" })
                Reject(Build(new Item(path, Svg)).Data, "unsafe path " + path);
            Reject(Build(new("a/icon.svg", Svg), new("A/ICON.SVG", Svg)).Data, "case-insensitive duplicate output");
            Reject(Build(new("a.js", ""), new("a.js/file.svg", Svg)).Data, "file/directory collision");
            string canceled = Path.Combine(root, "canceled");
            try { UiBundleExtractor.Extract(f.Data, canceled, new CancellationToken(true)); throw new Exception("Ignored cancellation"); }
            catch (OperationCanceledException) { Assert(!Directory.Exists(canceled), "cancellation before any output"); }
            CheckJunction(f);
            int gameArg = Array.IndexOf(args, "--game-dir");
            if (gameArg >= 0 && gameArg + 1 < args.Length) CheckGame(args[gameArg + 1]);
            Console.WriteLine($"SUCCESS: {checks} UI bundle checks passed");
        }
        finally { Directory.Delete(root, true); }
    }
    static void CheckJunction(Fixture f)
    {
        string outside = Path.Combine(root, "outside"), dest = Path.Combine(root, "junction");
        Directory.CreateDirectory(outside); Directory.CreateDirectory(dest);
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J");
        start.ArgumentList.Add(Path.Combine(dest, "uiresources")); start.ArgumentList.Add(outside);
        using var process = System.Diagnostics.Process.Start(start)!; process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("Could not create test junction");
        try { UiBundleExtractor.Extract(f.Data, dest); throw new Exception("Followed junction"); }
        catch (IOException) { Assert(!Directory.EnumerateFileSystemEntries(outside).Any(), "junction cannot redirect UI output outside destination"); }
        finally { Directory.Delete(Path.Combine(dest, "uiresources")); }
    }
    static void CheckGame(string game)
    {
        var toc = TocFile.Load(Path.Combine(game, "data_pack2", "pc", "base-generic.rmdtoc"));
        var entry = toc.Entries.Single(e => e.Path == "data/uiresources/game/ui/ui.ui");
        var (data, raw) = toc.Extract(entry); Assert(!raw && Crc32.Compute(data) == entry.Crc32, "real game UI CRC matches original TOC");
        string output = Path.Combine(root, "real"); string row = ResourceExporter.Write(toc, entry, output);
        string bundleRoot = Path.Combine(output, entry.Key + ".contents");
        var files = Directory.GetFiles(bundleRoot, "*", SearchOption.AllDirectories);
        Assert(files.Length == 746 && files.Count(p => p.EndsWith(".svg")) == 728 && files.Count(p => p.EndsWith(".html")) == 13 && files.Count(p => p.EndsWith(".js")) == 5, "real game exports 728 SVG + 13 HTML + 5 JavaScript");
        foreach (string path in files.Where(p => p.EndsWith(".svg")))
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            if (XDocument.Load(reader).Root?.Name.LocalName != "svg") throw new Exception("Invalid SVG: " + path);
        }
        Assert(true, "all 728 exported SVGs pass XML validation");
        Assert(File.ReadAllText(Path.Combine(bundleRoot, "uiresources/game/ui/ui.html")).StartsWith("<!doctype html>"), "real embedded HTML starts with doctype");
        var bundle = UiBundleExtractor.Parse(data);
        foreach (var embedded in bundle.Entries.Where(e => !e.External))
            if (!File.ReadAllBytes(Path.Combine(bundleRoot, embedded.Path)).AsSpan().SequenceEqual(data.AsSpan(embedded.Offset, embedded.Size))) throw new Exception("Changed embedded bytes");
        Assert(true, "all 746 exported files match original embedded bytes");
        Assert(row.Contains("2920 external references") && File.ReadAllBytes(Path.Combine(output, entry.Key)).SequenceEqual(data), "real manifest records external references and original remains intact");
    }
}
