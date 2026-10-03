using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RmdblobUnpacker.Core;
public static class AudioConverter
{
    const string Hash = "6C4A8A3813864FEFED081BBD337DBC0AD93BF88E0B92F5DB98D7AB258B22DC6C";
    static readonly object Gate = new();
    static string _executable;
    static string Executable()
    {
        lock (Gate)
        {
            if (_executable != null && File.Exists(_executable)) return _executable;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlResonantResourceUnpacker", "tools", "vgmstream-r2117-" + Hash[..12]);
            string exe = Path.Combine(root, "vgmstream-cli.exe");
            if (!File.Exists(Path.Combine(root, ".complete")))
            {
                using var source = typeof(AudioConverter).Assembly.GetManifestResourceStream("RmdblobUnpacker.Assets.vgmstream-r2117-win64.zip") ?? throw new NotSupportedException("Audio decoder bundle missing");
                using var bytes = new MemoryStream(); source.CopyTo(bytes);
                if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())) != Hash) throw new InvalidDataException("Audio decoder bundle checksum mismatch");
                bytes.Position = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(root)!);
                string staging = root + "-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(staging);
                try
                {
                    using (var archive = new ZipArchive(bytes, ZipArchiveMode.Read, true)) archive.ExtractToDirectory(staging);
                    if (!File.Exists(Path.Combine(staging, "vgmstream-cli.exe"))) throw new InvalidDataException("Audio decoder executable missing");
                    File.WriteAllText(Path.Combine(staging, ".complete"), Hash);
                    try { Directory.Move(staging, root); }
                    catch (IOException) when (File.Exists(Path.Combine(root, ".complete"))) { }
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, true); }
            }
            return _executable = exe;
        }
    }
    public static string ConvertWem(string input, string output, CancellationToken token = default)
    {
        if (new FileInfo(input).Length == 0) return "Empty WEM placeholder; no audio samples";
        token.ThrowIfCancellationRequested();
        string temp = output + "." + Guid.NewGuid().ToString("N") + ".tmp.wav";
        try
        {
            var start = new ProcessStartInfo(Executable()) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-i", "-o", Path.GetFullPath(temp), Path.GetFullPath(input) }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Cannot launch audio decoder");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            if (!process.WaitForExit(120000)) { process.Kill(true); process.WaitForExit(); throw new NotSupportedException("WEM decode timed out (120 seconds); original WEM retained"); }
            token.ThrowIfCancellationRequested();
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            if (process.ExitCode != 0 || !File.Exists(temp)) throw new NotSupportedException("WEM decoder rejected file; original WEM retained. " + log[..Math.Min(400, log.Length)].Trim());
            using (var stream = File.OpenRead(temp))
            {
                var header = new byte[12]; stream.ReadExactly(header);
                if (!header.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !header.AsSpan(8, 4).SequenceEqual("WAVE"u8)) throw new NotSupportedException("Audio decoder did not produce RIFF/WAVE");
            }
            File.Move(temp, output, true); return "WAV decoded with vgmstream r2117 (no looping)";
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
