using System.Numerics;

namespace RmdblobUnpacker.Core;

/// <summary>Conservative round-trip edits that leave topology, normal/tangent frames and meshlet cones valid.</summary>
public static class BinFbxImport
{
    static InvalidDataException Error(string zh, string en) => new(L.En ? en : zh);
    public static (byte[] Data, string Note) Prepare(byte[] original, string filename, CancellationToken token = default)
    {
        BinFbxModel model;
        try { model = BinFbxDecoder.Decode(original, token); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        { throw Error("原模型尚不支持安全回写：" + ex.Message, "The original model is not supported for safe editing: " + ex.Message); }
        return Encode(original, model, FbxImporter.Read(filename, token), token);
    }
    public static (byte[] Data, string Note) Encode(byte[] original, BinFbxModel model, ImportedFbxMesh[] imported, CancellationToken token = default)
    {
        var source = model.Meshes.Where(m => m.Lod == 0).ToArray();
        if (source.Length != imported.Length) throw Error($"网格数量不符：原模型 {source.Length}，导入 {imported.Length}。不支持合并、删除或新增网格。", "Mesh count changed. Merging, deleting or adding meshes is unsupported.");
        var names = new HashSet<string>();
        foreach (var mesh in imported) if (!names.Add(mesh.Name)) throw Error("存在重名网格，请保留导出时的网格名称。", "Duplicate mesh names; preserve the exported names.");
        var ordered = new ImportedFbxMesh[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            ordered[i] = imported.SingleOrDefault(m => m.Name == $"LOD0_mesh{i}") ?? throw Error($"缺少网格 LOD0_mesh{i}，请勿重命名或合并网格。", $"Missing mesh LOD0_mesh{i}; do not rename or merge meshes.");
            var a = source[i]; var b = ordered[i];
            if (a.Positions.Length != b.Positions.Length || !a.Indices.AsSpan().SequenceEqual(b.Indices))
                throw Error($"网格 {i} 的顶点或三角形顺序已改变。不支持增删顶点/面、重排索引或改变拓扑。", $"Mesh {i}: vertex count or triangle order changed. Topology/index changes are unsupported.");
            if (b.Material != $"material_{a.MaterialId:X16}") throw Error($"网格 {i} 的材质槽不匹配，请保留原材质槽及名称。", $"Mesh {i}: preserve the original material slot and name.");
            if (a.UVs.Length != b.CornerUV.Length || b.CornerUV.Any(u => u.Length != b.Indices.Length)) throw Error($"网格 {i} 的 UV 层数量或布局已改变。", $"Mesh {i}: UV layer count/layout changed.");
        }
        var positionPairs = source.SelectMany((m, i) => m.Positions.Select((p, v) => (From: p, To: ordered[i].Positions[v]))).ToArray();
        var transform = Fit(positionPairs, false);
        int uvLayers = source.Max(m => m.UVs.Length); var uvTransforms = new (float Scale, Vector3 Translation)[uvLayers];
        for (int j = 0; j < uvLayers; j++)
        {
            var pairs = new List<(Vector3 From, Vector3 To)>();
            for (int i = 0; i < source.Length; i++)
                if (j < source[i].UVs.Length)
                    for (int c = 0; c < source[i].Indices.Length; c++) pairs.Add((new(source[i].UVs[j][source[i].Indices[c]], 0), new(ordered[i].CornerUV[j][c], 0)));
            uvTransforms[j] = Fit(pairs.ToArray(), true);
        }
        byte[] result = (byte[])original.Clone(); var layout = model.Layout ?? throw new InvalidDataException("Missing source model layout");
        Vector3 translation = transform.Translation; if (layout.Mirror == -1) translation.Z = -translation.Z;
        void Float(int at, float value) { if (!float.IsFinite(value) || Math.Abs(value) > 10000000) throw Error("变换后坐标越界。", "Transformed coordinate out of range."); BitConverter.GetBytes(value).CopyTo(result, at); }
        void Point(int at) { Float(at, BitConverter.ToSingle(original, at) * transform.Scale + translation.X); Float(at + 4, BitConverter.ToSingle(original, at + 4) * transform.Scale + translation.Y); Float(at + 8, BitConverter.ToSingle(original, at + 8) * transform.Scale + translation.Z); }
        bool positionChanged = transform.Scale != 1 || translation != Vector3.Zero;
        if (positionChanged)
        {
            foreach (int offset in layout.Spheres.Distinct()) { Point(offset); Float(offset + 12, BitConverter.ToSingle(original, offset + 12) * transform.Scale); }
            foreach (int offset in layout.Boxes.Distinct()) { Point(offset); Point(offset + 12); }
        }
        foreach (var stream in layout.Streams)
        {
            token.ThrowIfCancellationRequested();
            if (stream.UV.Length > uvTransforms.Length) throw Error("其他 LOD 含有额外 UV 层，暂不支持同步回写。", "Another LOD has extra UV layers and cannot be updated safely.");
            for (int v = 0; v < stream.VertexCount; v++)
            {
                if ((v & 4095) == 0) token.ThrowIfCancellationRequested();
                if (positionChanged && stream.PositionFormat == 2) Point(stream.Position + v * stream.PositionStride);
                for (int j = 0; j < stream.UV.Length; j++)
                {
                    var t = uvTransforms[j]; if (t.Scale == 1 && t.Translation == Vector3.Zero) continue;
                    int at = stream.UV[j] + v * stream.UVStride[j];
                    float u = BitConverter.ToUInt16(original, at) / 4096f, w = 1 - BitConverter.ToUInt16(original, at + 2) / 4096f;
                    double nu = Math.Round((u * t.Scale + t.Translation.X) * 4096.0);
                    double nv = Math.Round((1 - (w * t.Scale + t.Translation.Y)) * 4096.0);
                    if (nu < 0 || nu > 65535 || nv < 0 || nv > 65535) throw Error("变换后 UV 超出游戏编码范围（U：0～15.9998；V：−14.9998～1），已拒绝导入。", "Transformed UV is outside the game's representable range (U: 0..15.9998, V: -14.9998..1).");
                    BitConverter.GetBytes((ushort)nu).CopyTo(result, at); BitConverter.GetBytes((ushort)nv).CopyTo(result, at + 2);
                }
            }
        }
        // Re-read all LODs; topology, bounds and meshlet coverage are checked by the decoder.
        var check = BinFbxDecoder.Decode(result, token);
        if (check.Meshes.Length != model.Meshes.Length) throw new InvalidDataException("BINFBX round-trip changed mesh count");
        for (int i = 0; i < check.Meshes.Length; i++)
        {
            var a = model.Meshes[i]; var b = check.Meshes[i];
            if (a.MaterialId != b.MaterialId || !a.Indices.AsSpan().SequenceEqual(b.Indices)) throw new InvalidDataException("BINFBX round-trip topology/material mismatch");
            for (int v = 0; v < a.Positions.Length; v++)
            {
                Vector3 expected = a.Positions[v] * transform.Scale + transform.Translation;
                if (Vector3.Distance(expected, b.Positions[v]) > Math.Max(0.00002f, expected.Length() * 0.00002f)) throw new InvalidDataException("BINFBX round-trip position mismatch");
                for (int j = 0; j < a.UVs.Length; j++)
                {
                    var t = uvTransforms[j]; var uvExpected = a.UVs[j][v] * t.Scale + new Vector2(t.Translation.X, t.Translation.Y);
                    if (Vector2.Distance(uvExpected, b.UVs[j][v]) > 0.00036f) throw new InvalidDataException("BINFBX round-trip UV mismatch");
                }
            }
        }
        return (result, L.En ? $"Static model validated; all {model.LodCount} LODs updated. Topology, original normal/tangent frames and materials preserved. Not tested in-game."
            : $"静态模型校验通过，已同步 {model.LodCount} 个 LOD。拓扑、原法线/切线及材质保持不变；尚未游戏内验证。");
    }
    static (float Scale, Vector3 Translation) Fit((Vector3 From, Vector3 To)[] pairs, bool uv)
    {
        if (pairs.Length == 0) return (1, Vector3.Zero);
        double[] a = new double[3], b = new double[3];
        foreach (var p in pairs) { a[0] += p.From.X; a[1] += p.From.Y; a[2] += p.From.Z; b[0] += p.To.X; b[1] += p.To.Y; b[2] += p.To.Z; }
        for (int j = 0; j < 3; j++) { a[j] /= pairs.Length; b[j] /= pairs.Length; }
        double denominator = 0, numerator = 0, extent = 0;
        foreach (var p in pairs)
        {
            double x = p.From.X - a[0], y = p.From.Y - a[1], z = p.From.Z - a[2];
            double norm = x*x + y*y + z*z; denominator += norm; extent = Math.Max(extent, norm);
            numerator += x*(p.To.X-b[0]) + y*(p.To.Y-b[1]) + z*(p.To.Z-b[2]);
        }
        float scale = denominator < 1e-20 ? 1 : (float)(numerator / denominator);
        if (!float.IsFinite(scale) || scale < 0.001f || scale > 1000) throw Error("仅支持 0.001～1000 倍的正向等比缩放，不支持镜像或零缩放。", "Only positive uniform scale from 0.001 to 1000 is supported; no mirroring or zero scale.");
        if (Math.Abs(scale - 1) < 0.000001f) scale = 1;
        var translation = new Vector3((float)(b[0]-scale*a[0]), (float)(b[1]-scale*a[1]), (float)(b[2]-scale*a[2]));
        if (translation.Length() < 0.000001f) translation = Vector3.Zero;
        float tolerance = Math.Max(0.00001f, (float)Math.Sqrt(extent) * scale * 0.00002f);
        foreach (var p in pairs)
            if (!float.IsFinite(p.To.X) || !float.IsFinite(p.To.Y) || !float.IsFinite(p.To.Z) || Vector3.Distance(p.From*scale+translation,p.To)>tolerance)
                throw Error(uv ? "仅支持整层 UV 的平移和正向等比缩放。不支持拆分 UV、局部移动、旋转或非等比缩放。" : "仅支持模型整体平移和正向等比缩放。不支持局部变形、旋转、非等比缩放或分别移动子网格。",
                    uv ? "Only whole-layer UV translation/positive uniform scaling is supported; no UV splits, local edits, rotation or nonuniform scaling." : "Only whole-model translation/positive uniform scaling is supported; no deformation, rotation, nonuniform scale or separate submesh transforms.");
        return (scale, translation);
    }
}
