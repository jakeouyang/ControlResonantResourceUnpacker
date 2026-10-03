using System.Text;
using RmdblobUnpacker.Core;
namespace RmdblobUnpacker;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (args.Length > 0 && args[0] == "--headless")
        {
            if (args.Length < 3) { Console.Error.WriteLine("Usage: --headless <gameDir> <outDir> [pattern]"); return 2; }
            // --headless <gameDir> <outDir> [pattern]
            string gameDir = args[1];
            string outDir  = args[2];
            string pattern = args.Length >= 4 ? args[3] : "*";
            return RunHeadless(gameDir, outDir, pattern);
        }

        Application.Run(new MainForm());
        return 0;
    }

    static int RunHeadless(string gameDir, string outDir, string pattern)
    {
        try
        {
            gameDir = gameDir.Trim().Trim('"');
            outDir = outDir.Trim().Trim('"');
            pattern = pattern.Trim().Trim('"');
            string pc = FindPcDir(gameDir);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMD_DEBUG_DUMP")))
            {
                Environment.SetEnvironmentVariable("DEBUG_DUMP", "1");
            }
            if (pc == null)
            {
                Console.Error.WriteLine("ERROR: data_pack2/pc not found under " + gameDir);
                return 2;
            }
            var packs = TocFile.Discover(pc).ToList();
            Directory.CreateDirectory(outDir);
            var matcher = new Matcher(pattern);
            int exported = 0, total = 0;
            var manifest = new StringBuilder();
            manifest.Append(ResourceExporter.Header);
            foreach (var info in packs)
            {
                Console.WriteLine("parsing " + info.Pack + " ...");
                var tf = TocFile.Load(info.TocPath);
                int matchedInPack = 0;
                foreach (var e in tf.Entries)
                {
                    total++;
                    if (!matcher.Matches(e.Path)) continue;
                    matchedInPack++;
                    string row = ResourceExporter.Write(tf, e, outDir);
                    manifest.AppendLine(row);
                    exported++;
                    if (exported % 200 == 0)
                        Console.WriteLine($"  {exported} exported ...");
                }
                Console.WriteLine($"  {info.Pack}: {tf.Entries.Count} entries, {matchedInPack} matched");
            }
            File.WriteAllText(Path.Combine(outDir, "manifest.csv"), manifest.ToString());
            Console.WriteLine($"done: {exported}/{total} exported -> {outDir}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    public static string FindPcDir(string userPath)
    {
        userPath = Path.GetFullPath(userPath);
        string marker = Path.Combine("data_pack2", "pc", "base-generic.rmdtoc");
        string cur = userPath;
        for (int i = 0; i < 4 && cur != null; i++)
        {
            if (File.Exists(Path.Combine(cur, marker)))
                return Path.Combine(cur, "data_pack2", "pc");
            var parent = Directory.GetParent(cur);
            if (parent == null) break;
            cur = parent.FullName;
        }
        if (Directory.Exists(userPath))
        {
            foreach (var dir in Directory.EnumerateDirectories(userPath, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, marker)))
                    return Path.Combine(dir, "data_pack2", "pc");
            }
        }
        return null;
    }

    public static string Sanitize(string rel)
    {
        var parts = rel.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            var sb = new StringBuilder(parts[i]);
            foreach (char c in Path.GetInvalidFileNameChars()) sb.Replace(c, '_');
            parts[i] = sb.ToString();
            if (parts[i] is ".." or "." or "") parts[i] = "_";
        }
        return string.Join('/', parts);
    }
}

/// <summary>Space separated terms, each must match; supports * wildcards.</summary>
public sealed class Matcher
{
    readonly List<Func<string, bool>> _preds = new();

    public Matcher(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return;
        foreach (var raw in pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string t = raw.Trim().ToLowerInvariant();
            if (t.Contains('*'))
            {
                var rx = new System.Text.RegularExpressions.Regex(
                    "^" + System.Text.RegularExpressions.Regex.Escape(t).Replace("\\*", ".*") + "$",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                _preds.Add(s => rx.IsMatch(s));
            }
            else
            {
                _preds.Add(s => s.Contains(t, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    public bool Matches(string path)
    {
        if (_preds.Count == 0) return true;
        foreach (var p in _preds) if (!p(path)) return false;
        return true;
    }
}
