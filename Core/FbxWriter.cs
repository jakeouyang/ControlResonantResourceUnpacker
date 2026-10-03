using System.IO.Compression;
using System.Numerics;
using System.Text;

namespace RmdblobUnpacker.Core;

/// <summary>Minimal binary FBX 7.4 mesh scene, suitable for Blender/Maya and independent FBX readers.</summary>
public static class FbxWriter
{
    sealed record Node(string Name, object[] Properties, Node[] Children);
    static Node N(string name, params object[] values) => new(name, values, System.Array.Empty<Node>());
    static Node Tree(string name, Node[] children, params object[] values) => new(name, values, children);
    static Node P(string name, string type, params object[] values) => N("P", new object[] { name, type, type == "double" ? "Number" : type == "int" ? "Integer" : "", "" }.Concat(values).ToArray());
    static Node Array(string name, object values) => N(name, values);

    public static void Write(string destination, BinFbxModel model, int lod = 0, CancellationToken token = default)
    {
        var meshes = model.Meshes.Where(m => m.Lod == lod).ToArray();
        if (meshes.Length == 0) throw new InvalidDataException("FBX: requested LOD is missing");
        var objects = new List<Node>(); var connections = new List<Node>();
        var materialIds = meshes.Select(m => m.MaterialId).Distinct().ToArray();
        for (int i = 0; i < materialIds.Length; i++)
            objects.Add(Tree("Material", new[] { N("Version", 102), N("ShadingModel", "lambert"), N("MultiLayer", 0),
                Tree("Properties70", new[] { P("DiffuseColor", "Color", 0.65, 0.65, 0.65) }) }, 100000L + i, $"material_{materialIds[i]:X16}\0\u0001Material", ""));
        for (int i = 0; i < meshes.Length; i++)
        {
            token.ThrowIfCancellationRequested(); var mesh = meshes[i]; long geoId = 1000 + i * 2, modelId = geoId + 1;
            var normals = new Vector3[mesh.Positions.Length];
            for (int f = 0; f < mesh.Indices.Length; f += 3)
            {
                int a = mesh.Indices[f], b = mesh.Indices[f + 1], c = mesh.Indices[f + 2];
                var cross = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                normals[a] += cross; normals[b] += cross; normals[c] += cross;
            }
            for (int n = 0; n < normals.Length; n++) normals[n] = normals[n].LengthSquared() > 1e-30f ? Vector3.Normalize(normals[n]) : Vector3.UnitY;
            int[] polygon = mesh.Indices.ToArray(); for (int f = 2; f < polygon.Length; f += 3) polygon[f] = -polygon[f] - 1;
            var children = new List<Node> { N("GeometryVersion", 124), Array("Vertices", Flatten(mesh.Positions)), Array("PolygonVertexIndex", polygon),
                Tree("LayerElementNormal", new[] { N("Version", 101), N("Name", "RecalculatedNormals"), N("MappingInformationType", "ByVertice"), N("ReferenceInformationType", "Direct"), Array("Normals", Flatten(normals)) }, 0),
                Tree("LayerElementMaterial", new[] { N("Version", 101), N("Name", ""), N("MappingInformationType", "AllSame"), N("ReferenceInformationType", "IndexToDirect"), Array("Materials", new[] { 0 }) }, 0) };
            for (int u = 0; u < mesh.UVs.Length; u++)
                children.Add(Tree("LayerElementUV", new[] { N("Version", 101), N("Name", "UVMap" + u), N("MappingInformationType", "ByVertice"), N("ReferenceInformationType", "Direct"), Array("UV", mesh.UVs[u].SelectMany(v => new[] { (double)v.X, v.Y }).ToArray()) }, u));
            for (int layer = 0; layer < Math.Max(1, mesh.UVs.Length); layer++)
            {
                var elements = new List<Node> { N("Version", 100) };
                if (layer == 0)
                {
                    elements.Add(Tree("LayerElement", new[] { N("Type", "LayerElementNormal"), N("TypedIndex", 0) }));
                    elements.Add(Tree("LayerElement", new[] { N("Type", "LayerElementMaterial"), N("TypedIndex", 0) }));
                }
                if (layer < mesh.UVs.Length) elements.Add(Tree("LayerElement", new[] { N("Type", "LayerElementUV"), N("TypedIndex", layer) }));
                children.Add(Tree("Layer", elements.ToArray(), layer));
            }
            string name = $"LOD{lod}_mesh{i}";
            objects.Add(Tree("Geometry", children.ToArray(), geoId, name + "\0\u0001Geometry", "Mesh"));
            objects.Add(Tree("Model", new[] { N("Version", 232), Tree("Properties70", new[] {
                P("Lcl Translation", "Lcl Translation", 0.0, 0.0, 0.0), P("Lcl Rotation", "Lcl Rotation", 0.0, 0.0, 0.0), P("Lcl Scaling", "Lcl Scaling", 1.0, 1.0, 1.0) }), N("Shading", true), N("Culling", "CullingOff") }, modelId, name + "\0\u0001Model", "Mesh"));
            connections.Add(N("C", "OO", geoId, modelId)); connections.Add(N("C", "OO", modelId, 0L));
            connections.Add(N("C", "OO", 100000L + System.Array.IndexOf(materialIds, mesh.MaterialId), modelId));
        }
        var roots = new[] {
            Tree("FBXHeaderExtension", new[] { N("FBXHeaderVersion", 1003), N("FBXVersion", 7400), N("EncryptionType", 0), N("Creator", "Control Resonant Resource Unpacker") }),
            Tree("GlobalSettings", new[] { N("Version", 1000), Tree("Properties70", new[] {
                P("UpAxis", "int", 1), P("UpAxisSign", "int", 1), P("FrontAxis", "int", 2), P("FrontAxisSign", "int", 1),
                P("CoordAxis", "int", 0), P("CoordAxisSign", "int", 1), P("UnitScaleFactor", "double", 100.0), P("OriginalUnitScaleFactor", "double", 100.0) }) }),
            Tree("Documents", new[] { N("Count", 1), Tree("Document", new[] { Tree("Properties70", System.Array.Empty<Node>()), N("RootNode", 0L) }, 1L, "Scene", "Scene") }),
            Tree("References", System.Array.Empty<Node>()),
            Tree("Definitions", new[] { N("Version", 100), N("Count", objects.Count), Tree("ObjectType", new[] { N("Count", meshes.Length) }, "Geometry"), Tree("ObjectType", new[] { N("Count", meshes.Length) }, "Model"), Tree("ObjectType", new[] { N("Count", materialIds.Length) }, "Material") }),
            Tree("Objects", objects.ToArray()), Tree("Connections", connections.ToArray()), Tree("Takes", new[] { N("Current", "") }) };
        string temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(file, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0")); writer.Write(7400);
                foreach (var node in roots) { token.ThrowIfCancellationRequested(); WriteNode(writer, node, token); }
                writer.Write(new byte[13]);
                writer.Write(Convert.FromHexString("FABCAB09D0C8D466B176FB831CF7267E"));
                writer.Write(new byte[(16 - file.Position % 16) % 16]); writer.Write(7400); writer.Write(new byte[120]);
                writer.Write(Convert.FromHexString("F85A8C6ADEF5D97EECE90CE3758F290B")); writer.Flush(); file.Flush(true);
            }
            token.ThrowIfCancellationRequested(); File.Move(temp, destination, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static double[] Flatten(Vector3[] values) => values.SelectMany(v => new[] { (double)v.X, v.Y, v.Z }).ToArray();
    static void WriteNode(BinaryWriter w, Node node, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); long start = w.BaseStream.Position;
        byte[] name = Encoding.UTF8.GetBytes(node.Name); w.Write(0u); w.Write(node.Properties.Length); w.Write(0u); w.Write((byte)name.Length); w.Write(name);
        long propertiesStart = w.BaseStream.Position;
        foreach (var p in node.Properties) WriteProperty(w, p);
        long propertiesLength = w.BaseStream.Position - propertiesStart;
        foreach (var child in node.Children) WriteNode(w, child, token);
        if (node.Children.Length > 0) w.Write(new byte[13]);
        long end = w.BaseStream.Position; w.BaseStream.Position = start; w.Write(checked((uint)end)); w.BaseStream.Position = start + 8; w.Write(checked((uint)propertiesLength)); w.BaseStream.Position = end;
    }
    static void WriteProperty(BinaryWriter w, object value)
    {
        switch (value)
        {
            case int i: w.Write((byte)'I'); w.Write(i); break;
            case long l: w.Write((byte)'L'); w.Write(l); break;
            case double d: w.Write((byte)'D'); w.Write(d); break;
            case bool b: w.Write((byte)'C'); w.Write(b); break;
            case string s: byte[] text = Encoding.UTF8.GetBytes(s); w.Write((byte)'S'); w.Write(text.Length); w.Write(text); break;
            case double[] a: WriteArray(w, 'd', a, 8); break;
            case int[] a: WriteArray(w, 'i', a, 4); break;
            default: throw new InvalidDataException("FBX: unsupported property");
        }
    }
    static void WriteArray(BinaryWriter writer, char code, System.Array values, int size)
    {
        byte[] data = new byte[checked(values.Length * size)]; Buffer.BlockCopy(values, 0, data, 0, data.Length);
        using var compressed = new MemoryStream(); using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true)) z.Write(data);
        writer.Write((byte)code); writer.Write(values.Length); writer.Write(1); writer.Write(checked((int)compressed.Length)); writer.Write(compressed.ToArray());
    }
}
