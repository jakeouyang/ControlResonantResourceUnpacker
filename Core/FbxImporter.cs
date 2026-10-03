using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace RmdblobUnpacker.Core;

public sealed record ImportedFbxMesh(string Name, string Material, Vector3[] Positions, int[] Indices, Vector2[][] CornerUV);

public static class FbxImporter
{
    static InvalidDataException Error(string zh, string en) => new(L.En ? en : zh);
    public static ImportedFbxMesh[] Read(string filename, CancellationToken token = default)
    {
        if (!Path.GetExtension(filename).Equals(".fbx", StringComparison.OrdinalIgnoreCase)) throw Error("请选择 FBX 文件。", "Select an FBX file.");
        if (new FileInfo(filename).Length > 268435456) throw Error("FBX 文件不得超过 256 MB。", "FBX input is limited to 256 MB.");
        var assembly = typeof(FbxImporter).Assembly;
        using var stream = assembly.GetManifestResourceStream("RmdblobUnpacker.Assets.fbx-import.exe") ?? throw new IOException("Missing FBX reader");
        using var content = new MemoryStream(); stream.CopyTo(content); byte[] bytes = content.ToArray();
        string hash = Convert.ToHexString(SHA256.HashData(bytes));
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControlResonantResourceUnpacker", "tools", "fbx-" + hash);
        Directory.CreateDirectory(cache); string exe = Path.Combine(cache, "fbx-import.exe");
        if (!File.Exists(exe) || !SHA256.HashData(File.ReadAllBytes(exe)).AsSpan().SequenceEqual(SHA256.HashData(bytes)))
        {
            string tmp = exe + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, exe, true); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        string work = Path.Combine(Path.GetTempPath(), "ResonantFbx-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
        string output = Path.Combine(work, "mesh.dat");
        try
        {
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, WorkingDirectory = work };
            start.ArgumentList.Add(Path.GetFullPath(filename)); start.ArgumentList.Add(output);
            using var process = Process.Start(start) ?? throw new IOException("Could not start FBX reader");
            var stderr = process.StandardError.ReadToEndAsync(); var timer = Stopwatch.StartNew();
            try
            {
                while (!process.WaitForExit(100)) { token.ThrowIfCancellationRequested(); if (timer.Elapsed > TimeSpan.FromSeconds(45)) throw Error("FBX 解析超时，请简化模型后重试。", "FBX parsing timed out; simplify the model."); }
                token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0) throw process.ExitCode switch
                {
                    11 => Error("不支持带骨骼、蒙皮、形态键或动画曲线的 FBX。请关闭动画导出。", "Rigged, skinned, blend-shape or animated FBX files are unsupported. Disable animation export."),
                    12 => Error("模型包含非三角面。请保留导出模型的原始三角形拓扑。", "Non-triangle faces found. Preserve the exported triangle topology."),
                    14 => Error("材质槽或 UV 层数量不支持。每个网格须保留原有单一材质槽和 UV 层。", "Unsupported material/UV layout. Preserve the original single material slot and UV layers per mesh."),
                    13 => Error("模型为空或数据量超过导入限制。", "The model is empty or exceeds import limits."),
                    _ => Error("无法读取 FBX：文件可能损坏或布局不支持。", "Cannot read FBX: the file may be damaged or unsupported.")
                };
            }
            finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
            if (new FileInfo(output).Length > 536870912) throw Error("FBX 展开数据超过 512 MB。", "Decoded FBX exceeds 512 MB.");
            using var reader = new BinaryReader(File.OpenRead(output), Encoding.UTF8);
            int Count(int max) { uint n = reader.ReadUInt32(); if (n > max) throw new InvalidDataException("FBX result count out of range"); return (int)n; }
            string Text() { int n = Count(65536); byte[] s = reader.ReadBytes(n); if (s.Length != n) throw new EndOfStreamException(); return Encoding.UTF8.GetString(s); }
            float Number() { double d = reader.ReadDouble(); if (!double.IsFinite(d) || Math.Abs(d) > 10000000) throw Error("模型包含无效坐标或超出范围的数值。", "Invalid or out-of-range model coordinates."); return (float)d; }
            if (!reader.ReadBytes(8).AsSpan().SequenceEqual("RFBXIMP1"u8)) throw new InvalidDataException("Invalid FBX reader output");
            int count = Count(65536); var result = new ImportedFbxMesh[count];
            for (int i = 0; i < count; i++)
            {
                token.ThrowIfCancellationRequested(); string name = Text(), material = Text();
                int nv = Count(5000000), ni = Count(15000000), nu = Count(8);
                if ((long)nv * 24 + (long)ni * (4 + 16 * nu) > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException("Truncated FBX reader output");
                var positions = new Vector3[nv]; for (int v = 0; v < nv; v++) positions[v] = new(Number(), Number(), Number());
                var indices = new int[ni]; for (int v = 0; v < ni; v++) { indices[v] = Count(nv); if (indices[v] >= nv) throw new InvalidDataException("FBX index out of range"); }
                var uv = new Vector2[nu][]; for (int j = 0; j < nu; j++) { uv[j] = new Vector2[ni]; for (int v = 0; v < ni; v++) uv[j][v] = new(Number(), Number()); }
                result[i] = new(name, material, positions, indices, uv);
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing FBX reader data");
            return result;
        }
        finally { if (File.Exists(output)) File.Delete(output); Directory.Delete(work); }
    }
}
