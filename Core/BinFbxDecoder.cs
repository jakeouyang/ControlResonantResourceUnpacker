using System.Numerics;

namespace RmdblobUnpacker.Core;

public sealed record BinFbxMesh(int Lod, ulong MaterialId, Vector3[] Positions, Vector2[][] UVs, int[] Indices);
public sealed record BinFbxStream(int Lod, int Position, int PositionStride, int PositionFormat, int VertexCount, int[] UV, int[] UVStride);
public sealed class BinFbxLayout
{
    public float Mirror;
    public List<int> Spheres = new(), Boxes = new();
    public List<BinFbxStream> Streams = new();
}
public sealed record BinFbxModel(int LodCount, BinFbxMesh[] Meshes, BinFbxLayout Layout = null);

/// <summary>Version 92 static meshes. Every stream is bounded and meshlet topology is independently checked.</summary>
public static class BinFbxDecoder
{
    sealed class Reader
    {
        public readonly byte[] Data; public int Position;
        public Reader(byte[] data) { Data = data; }
        public void Need(int n) { if (n < 0 || Position < 0 || n > Data.Length - Position) throw new InvalidDataException("BINFBX: truncated section"); }
        public int Count(int limit = 1000000) { uint v = U(); if (v > limit) throw new NotSupportedException("BINFBX: unsupported section count"); return (int)v; }
        public uint U() { Need(4); uint v = BitConverter.ToUInt32(Data, Position); Position += 4; return v; }
        public int I() => unchecked((int)U());
        public ulong Q() { Need(8); ulong v = BitConverter.ToUInt64(Data, Position); Position += 8; return v; }
        public float F() { float v = BitConverter.Int32BitsToSingle(I()); if (!float.IsFinite(v)) throw new InvalidDataException("BINFBX: non-finite value"); return v; }
        public byte B() { Need(1); return Data[Position++]; }
        public void Skip(int n) { Need(n); Position += n; }
        public Vector3 V() => new(F(), F(), F());
    }
    sealed record Attribute(byte Buffer, byte Format, byte Usage, int Offset);
    sealed class Part
    {
        public int Lod, Vertices, Triangles, A, V, IndexSize, Index, Meshlets, MeshletOffset, BoundOffset;
        public Vector3 Min, Max; public Attribute[] Attributes; public int[] Strides = new int[2];
        public ulong Material;
    }
    static int Width(int format) => format switch { 2 => 12, 4 or 5 or 7 or 15 => 4, 8 or 13 => 8, _ => throw new NotSupportedException("BINFBX: unknown vertex format " + format) };
    static void Require(bool condition, string reason) { if (!condition) throw new InvalidDataException("BINFBX: " + reason); }
    public static BinFbxModel Decode(byte[] data, CancellationToken token = default)
    {
        try { return DecodeCore(data, token); }
        catch (OverflowException ex) { throw new InvalidDataException("BINFBX: section size overflow", ex); }
    }
    static BinFbxModel DecodeCore(byte[] data, CancellationToken token)
    {
        var r = new Reader(data);
        var layout = new BinFbxLayout();
        if (r.U() != 92) throw new NotSupportedException("BINFBX: only version 92 is supported");
        int lods = r.Count(16); Require(lods > 0, "no LODs");
        // Non-zero fields include serialized binding/pose data. Do not silently drop a rig.
        for (int i = 0; i < 9; i++) if (r.U() != 0) throw new NotSupportedException("BINFBX v92: skeletal/pose header is not yet supported; original retained");
        float scale = r.F(); Require(scale == 1, "unsupported global scale");
        int thresholds = r.Count(16); for (int i = 0; i < thresholds; i++) r.F();
        float mirror = r.F(); Require(mirror == -1 || mirror == 1, "invalid coordinate sign");
        layout.Mirror = mirror; layout.Spheres.Add(r.Position);
        r.V(); float radius = r.F(); Require(radius > 0, "invalid global sphere"); layout.Boxes.Add(r.Position); r.V(); r.V();
        var spheres = new (Vector3 Center, float Radius)[lods];
        for (int i = 0; i < lods; i++) { layout.Spheres.Add(r.Position); spheres[i] = (r.V(), r.F()); Require(spheres[i].Radius > 0, "invalid LOD sphere"); }
        int materials = r.Count(65536); var materialIds = new ulong[materials];
        for (int i = 0; i < materials; i++) materialIds[i] = r.Q();
        int maps = r.Count(65536); var map = new int[maps];
        for (int i = 0; i < maps; i++) { map[i] = r.Count(); Require(map[i] < materials, "material index outside table"); }
        int alternatives = r.Count(1024);
        for (int i = 0; i < alternatives; i++) { r.Skip(r.Count(65536)); for (int j = 0; j < maps; j++) Require(r.U() < materials, "alternate material index"); }
        r.Skip(checked(r.Count() * 4)); // Length-prefixed render graph words, not vertex data.
        if (r.B() != 0 || r.B() != 0) throw new NotSupportedException("BINFBX: unsupported render graph flags");
        int commands = r.Count(65536);
        for (int i = 0; i < commands; i++)
        {
            Require(r.I() == -2, "invalid LOD command"); Require(r.Count(16) < lods, "invalid command LOD");
            Require(r.I() == -1 && r.I() == 0 && r.I() == -3, "unsupported LOD command");
        }
        int partCount = r.Count(65536); Require(partCount > 0 && partCount == maps, "material map / mesh count mismatch");
        var parts = new List<Part>();
        for (int i = 0; i < partCount; i++)
        {
            token.ThrowIfCancellationRequested();
            var p = new Part { Lod = r.Count(16), Vertices = r.Count(5000000), Triangles = r.Count(10000000), A = r.Count(int.MaxValue), V = r.Count(int.MaxValue), IndexSize = r.Count(4), Index = r.Count(int.MaxValue) };
            Require(p.Lod < lods && p.Vertices > 0 && p.Triangles > 0 && p.IndexSize is 2 or 4, "invalid mesh counts");
            r.U(); layout.Spheres.Add(r.Position); r.V(); r.F(); layout.Boxes.Add(r.Position); p.Min = r.V(); p.Max = r.V(); r.U();
            int attrCount = r.B(); Require(attrCount is > 0 and <= 16, "invalid attribute count");
            var attrs = new List<Attribute>();
            for (int j = 0; j < attrCount; j++)
            {
                byte buffer = r.B(), format = r.B(), usage = r.B(), channel = r.B();
                Require(buffer < 2 && channel == 0, "unsupported attribute buffer/channel");
                if (usage is 5 or 6) throw new NotSupportedException("BINFBX: skin weights require skeleton support");
                attrs.Add(new(buffer, format, usage, p.Strides[buffer])); p.Strides[buffer] += Width(format);
            }
            Require(attrs.Count(a => a.Usage == 0) == 1, "missing or duplicate position"); p.Attributes = attrs.ToArray();
            r.I(); r.F(); byte rigid = r.B(); Require(rigid == 1, "non-rigid geometry");
            p.Meshlets = r.Count(1000000); p.MeshletOffset = r.Count(int.MaxValue);
            Require(r.I() == -1 && r.I() == -1, "unsupported meshlet layout");
            p.BoundOffset = r.Count(int.MaxValue); r.U(); Require(r.B() == 0, "unsupported mesh extension");
            p.Material = materialIds[map[i]]; parts.Add(p);
        }
        if (r.U() != 0) throw new NotSupportedException("BINFBX: extra mesh group is not yet supported");
        var groups = new List<List<Part>>();
        foreach (var p in parts)
        {
            if (groups.Count == 0 || (p.Lod != groups[^1][^1].Lod && p.A == 0 && p.V == 0)) groups.Add(new());
            groups[^1].Add(p);
        }
        var result = new List<BinFbxMesh>();
        foreach (var group in groups)
        {
            token.ThrowIfCancellationRequested();
            int aSize = group.Max(p => checked(p.A + p.Vertices * p.Strides[0]));
            int vSize = group.Max(p => checked(p.V + p.Vertices * p.Strides[1]));
            int iSize = group.Max(p => checked((p.Index + p.Triangles * 3) * p.IndexSize));
            int vStart = r.Position; r.Skip(vSize); int aStart = r.Position; r.Skip(aSize); int iStart = r.Position; r.Skip(iSize);
            int meshletStart = r.Position, meshletEnd = 0, boundsCount = 0;
            foreach (var p in group)
            {
                token.ThrowIfCancellationRequested();
                var position = p.Attributes.Single(a => a.Usage == 0);
                if (position.Format is not (8 or 2)) throw new NotSupportedException("BINFBX: unsupported position encoding");
                var uvAttrs = p.Attributes.Where(a => a.Usage == 2).ToArray();
                if (uvAttrs.Any(a => a.Format != 7)) throw new NotSupportedException("BINFBX: unsupported UV encoding");
                var vertices = new Vector3[p.Vertices]; var uv = uvAttrs.Select(_ => new Vector2[p.Vertices]).ToArray();
                int Address(Attribute attr, int v) => checked((attr.Buffer == 0 ? aStart + p.A : vStart + p.V) + v * p.Strides[attr.Buffer] + attr.Offset);
                layout.Streams.Add(new(p.Lod, Address(position, 0), p.Strides[position.Buffer], position.Format, p.Vertices,
                    uvAttrs.Select(a => Address(a, 0)).ToArray(), uvAttrs.Select(a => p.Strides[a.Buffer]).ToArray()));
                for (int v = 0; v < p.Vertices; v++)
                {
                    if ((v & 4095) == 0) token.ThrowIfCancellationRequested();
                    int offset = Address(position, v); Vector3 point;
                    if (position.Format == 8)
                    {
                        point = new Vector3(BitConverter.ToInt16(data, offset), BitConverter.ToInt16(data, offset + 2), BitConverter.ToInt16(data, offset + 4));
                        point = point / 32767f * spheres[p.Lod].Radius + spheres[p.Lod].Center;
                    }
                    else point = new Vector3(BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8));
                    Require(float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z), "non-finite position");
                    vertices[v] = point;
                    for (int j = 0; j < uv.Length; j++) { int q = Address(uvAttrs[j], v); uv[j][v] = new(BitConverter.ToUInt16(data, q) / 4096f, 1 - BitConverter.ToUInt16(data, q + 2) / 4096f); }
                }
                var indices = new int[checked(p.Triangles * 3)]; r.Position = checked(iStart + p.Index * p.IndexSize);
                for (int i = 0; i < indices.Length; i++) { if (p.IndexSize == 4) indices[i] = r.Count(int.MaxValue); else { r.Need(2); indices[i] = BitConverter.ToUInt16(data, r.Position); r.Skip(2); } Require(indices[i] < vertices.Length, "index outside vertex buffer"); }
                float tolerance = Math.Max(0.0001f, spheres[p.Lod].Radius * 0.00015f);
                foreach (int i in indices)
                {
                    var v = vertices[i];
                    Require(v.X >= p.Min.X - tolerance && v.X <= p.Max.X + tolerance && v.Y >= p.Min.Y - tolerance && v.Y <= p.Max.Y + tolerance && v.Z >= p.Min.Z - tolerance && v.Z <= p.Max.Z + tolerance, "decoded position outside mesh bounds");
                }
                // Validate against a second representation: meshlet local triangles and vertex remaps.
                var seen = new bool[p.Triangles];
                for (int j = 0; j < p.Meshlets; j++)
                {
                    if ((j & 4095) == 0) token.ThrowIfCancellationRequested();
                    r.Position = checked(meshletStart + p.MeshletOffset + j * 20);
                    int remapOffset = r.Count(int.MaxValue), triangleOffset = r.Count(int.MaxValue), minVertex = r.Count(int.MaxValue);
                    int nv = r.B(), nt = r.B(), width = r.B(); r.B(); int firstTriangle = r.Count(int.MaxValue);
                    Require(nv > 0 && nt > 0 && width is 1 or 2 or 4 && firstTriangle <= p.Triangles - nt, "invalid meshlet descriptor");
                    var remap = new int[nv]; r.Position = checked(meshletStart + remapOffset);
                    for (int k = 0; k < nv; k++)
                    {
                        int value; if (width == 1) value = r.B(); else if (width == 2) { value = r.B() | r.B() << 8; } else value = r.Count(int.MaxValue);
                        remap[k] = checked(minVertex + value); Require(remap[k] < p.Vertices, "meshlet remap out of range");
                    }
                    r.Position = checked(meshletStart + triangleOffset);
                    for (int k = 0; k < nt; k++)
                    {
                        Require(!seen[firstTriangle + k], "overlapping meshlet triangles"); seen[firstTriangle + k] = true;
                        for (int c = 0; c < 3; c++) { int local = r.B(); Require(local < nv && remap[local] == indices[(firstTriangle + k) * 3 + c], "meshlet/index topology mismatch"); }
                        r.B();
                    }
                    meshletEnd = Math.Max(meshletEnd, Math.Max(checked(triangleOffset + nt * 4), checked((remapOffset + nv * width + 3) & ~3)));
                }
                Require(seen.All(v => v), "meshlet topology coverage incomplete");
                boundsCount = Math.Max(boundsCount, checked(p.BoundOffset + p.Meshlets));
                // Keep source Y-up coordinates; reflect Z for the right-handed FBX coordinate system.
                if (mirror == -1) { for (int i = 0; i < vertices.Length; i++) vertices[i].Z = -vertices[i].Z; for (int i = 0; i < indices.Length; i += 3) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]); }
                result.Add(new(p.Lod, p.Material, vertices, uv, indices));
            }
            r.Position = meshletStart; r.Skip(checked(meshletEnd + boundsCount * 20));
            for (int i = 0; i < boundsCount; i++) layout.Spheres.Add(checked(meshletStart + meshletEnd + i * 20));
        }
        Require(r.Position == data.Length, "unconsumed data / unsupported stream layout");
        Require(result.Any(m => m.Lod == 0), "LOD0 is missing");
        return new(lods, result.ToArray(), layout);
    }
}
