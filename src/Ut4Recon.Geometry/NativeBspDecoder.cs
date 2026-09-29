using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class NativeBspDecoder
{
    public BspMapIr Decode(string physicalPath, string virtualPath)
    {
        physicalPath = Path.GetFullPath(physicalPath);
        if (!File.Exists(physicalPath)) throw new FileNotFoundException("Cooked map package was not found.", physicalPath);
        var diagnostics = new List<string>(); var asset = new UAsset(physicalPath, EngineVersion.VER_UE4_15);
        using var document = JsonDocument.Parse(asset.SerializeJson()); var root = document.RootElement;
        var models = new List<BspModel>();
        for (var i = 0; i < asset.Exports.Count; i++)
        {
            var exportJson = root.GetProperty("Exports")[i];
            var classPath = ResolveIndex(root, exportJson.GetProperty("ClassIndex").GetInt32());
            if (!string.Equals(classPath, "/Script/Engine.Model", StringComparison.Ordinal) && !classPath.EndsWith(".Model", StringComparison.Ordinal)) continue;
            try { models.Add(DecodeModel(asset.Exports[i].Extras, root, ResolveIndex(root, i + 1), i + 1)); }
            catch (Exception error) { diagnostics.Add($"Model export {i + 1}: {error.Message}"); }
        }
        if (models.Count == 0) diagnostics.Add("No supported UModel geometry exports were decoded.");
        return new BspMapIr(GeometryFormatVersions.BspMapIr, GeometryFormatVersions.BspProfile, virtualPath.Replace('\\', '/'), Identity.Sha256File(physicalPath), models, diagnostics);
    }

    private static BspModel DecodeModel(byte[] raw, JsonElement root, string objectPath, int exportIndex)
    {
        const int geometryOffset = 30;
        if (raw.Length < geometryOffset) throw new InvalidDataException("Native UModel payload is shorter than its UT4 header.");
        if (raw[0] is not (0 or 1)) throw new InvalidDataException("Unsupported UModel strip-data flags.");
        using var stream = new MemoryStream(raw, false); using var reader = new BinaryReader(stream); stream.Position = geometryOffset;
        var vectors = ReadBulk(reader, 12, (r, _) => Vector(r));
        var points = ReadBulk(reader, 12, (r, _) => Vector(r));
        var nodes = ReadBulk(reader, 64, ReadNode);
        var surfaceCount = ReadCount(reader); var surfaces = new BspSurface[surfaceCount];
        for (var i = 0; i < surfaceCount; i++)
        {
            var material = reader.ReadInt32(); var flags = reader.ReadUInt32(); var basePoint = reader.ReadInt32(); var normal = reader.ReadInt32();
            var textureU = reader.ReadInt32(); var textureV = reader.ReadInt32(); var brushPolygon = reader.ReadInt32(); var actor = reader.ReadInt32();
            surfaces[i] = new BspSurface(i, ResolveIndex(root, material), flags, basePoint, normal, textureU, textureV, brushPolygon, ResolveIndex(root, actor),
                reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadInt32());
        }
        var vertexPool = ReadBulk(reader, 24, (r, i) => new BspVertexPoolEntry(i, r.ReadInt32(), r.ReadInt32(), Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle()), Finite(r.ReadSingle())));
        var parsed = checked((int)stream.Position); var polygons = new List<BspPolygon>();
        foreach (var node in nodes)
        {
            if (node.VertexCount < 3) continue;
            if (node.SurfaceIndex < 0 || node.SurfaceIndex >= surfaces.Length || node.VertexPoolStart < 0 || node.VertexPoolStart + node.VertexCount > vertexPool.Count)
                throw new InvalidDataException($"Node {node.Index} refers outside its surface or vertex-pool arrays.");
            var surface = surfaces[node.SurfaceIndex];
            if (!ValidIndex(surface.BasePointIndex, points.Count) || !ValidIndex(surface.NormalVectorIndex, vectors.Count) || !ValidIndex(surface.TextureUVectorIndex, vectors.Count) || !ValidIndex(surface.TextureVVectorIndex, vectors.Count))
                throw new InvalidDataException($"Surface {surface.Index} refers outside its point/vector arrays.");
            var positions = Enumerable.Range(node.VertexPoolStart, node.VertexCount).Select(x => vertexPool[x].PointIndex).Select(x => ValidIndex(x, points.Count) ? points[x] : throw new InvalidDataException($"Vertex pool refers to point {x} outside the point array.")).ToArray();
            polygons.Add(new BspPolygon(node.Index, surface.Index, positions, surface.Material, surface.Flags, points[surface.BasePointIndex], vectors[surface.NormalVectorIndex], vectors[surface.TextureUVectorIndex], vectors[surface.TextureVVectorIndex], surface.LightMapScale, PolygonArea(positions)));
        }
        var tail = raw.AsSpan(parsed); var metrics = Measure(polygons);
        return new BspModel(objectPath, exportIndex, vectors, points, nodes, surfaces, vertexPool, polygons, parsed, tail.Length,
            Identity.Sha256Bytes(raw.AsSpan(0, geometryOffset)), Identity.Sha256Bytes(tail), metrics);
    }

    private static List<T> ReadBulk<T>(BinaryReader reader, int stride, Func<BinaryReader, int, T> read)
    {
        var actualStride = reader.ReadInt32(); var count = ReadCount(reader);
        if (actualStride != stride) throw new InvalidDataException($"Expected UModel bulk stride {stride}, found {actualStride}.");
        var result = new List<T>(count);
        for (var i = 0; i < count; i++) { var start = reader.BaseStream.Position; result.Add(read(reader, i)); if (reader.BaseStream.Position - start != stride) throw new InvalidDataException("UModel element decoder consumed the wrong stride."); }
        return result;
    }

    private static BspNode ReadNode(BinaryReader reader, int index) => new(index,
        reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(),
        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadInt32(),
        reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
        reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32());

    private static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException($"Invalid UModel array count {count}."); return count;
    }

    private static MeshVector Vector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    private static double? Finite(float value) => float.IsFinite(value) ? value : null;
    private static bool ValidIndex(int index, int count) => index >= 0 && index < count;
    private static double PolygonArea(IReadOnlyList<MeshVector> vertices)
    {
        if (vertices.Count < 3) return 0; var origin = vertices[0]; double area = 0;
        for (var i = 1; i + 1 < vertices.Count; i++) area += Length(Cross(Subtract(vertices[i], origin), Subtract(vertices[i + 1], origin))) * 0.5;
        return area;
    }

    private static BspMetrics Measure(IReadOnlyList<BspPolygon> polygons)
    {
        var positions = polygons.SelectMany(x => x.Positions).ToArray();
        var min = positions.Length == 0 ? new MeshVector(0, 0, 0) : new MeshVector(positions.Min(x => x.X), positions.Min(x => x.Y), positions.Min(x => x.Z));
        var max = positions.Length == 0 ? new MeshVector(0, 0, 0) : new MeshVector(positions.Max(x => x.X), positions.Max(x => x.Y), positions.Max(x => x.Z));
        var groups = polygons.GroupBy(PlaneKey).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (x.Key, Area: x.Sum(y => y.Area))).ToArray();
        using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, Encoding.UTF8, true)) foreach (var group in groups) { writer.Write(group.Key); writer.Write(Math.Round(group.Area, 3)); }
        var query = RayFingerprint(polygons, min, max);
        return new BspMetrics(min, max, polygons.Sum(x => x.Area), groups.Length, Identity.Sha256Bytes(memory.ToArray()), query.Count, query.Hash);
    }

    internal static string PlaneKey(BspPolygon polygon)
    {
        var length = Length(polygon.Normal); var n = length == 0 ? polygon.Normal : Scale(polygon.Normal, 1 / length); var d = Dot(n, polygon.Positions[0]);
        return $"{polygon.Material}|{Math.Round(n.X, 3):F3}|{Math.Round(n.Y, 3):F3}|{Math.Round(n.Z, 3):F3}|{Math.Round(d):F0}";
    }

    private static (int Count, string Hash) RayFingerprint(IReadOnlyList<BspPolygon> polygons, MeshVector min, MeshVector max)
    {
        var center = Scale(Add(min, max), .5); var extent = Subtract(max, min); var margin = Math.Max(1, Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) * .1);
        var probes = new[] {
            (new MeshVector(min.X-margin,center.Y,center.Z),new MeshVector(1,0,0)), (new MeshVector(max.X+margin,center.Y,center.Z),new MeshVector(-1,0,0)),
            (new MeshVector(center.X,min.Y-margin,center.Z),new MeshVector(0,1,0)), (new MeshVector(center.X,max.Y+margin,center.Z),new MeshVector(0,-1,0)),
            (new MeshVector(center.X,center.Y,min.Z-margin),new MeshVector(0,0,1)), (new MeshVector(center.X,center.Y,max.Z+margin),new MeshVector(0,0,-1)) };
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Encoding.UTF8, true);
        foreach (var (origin, direction) in probes)
        {
            var hits = polygons.SelectMany(x => Triangles(x.Positions)).Select(x => RayTriangle(origin, direction, x.A, x.B, x.C)).Where(x => x >= 0).ToArray();
            writer.Write(hits.Length == 0 ? -1L : (long)Math.Round(hits.Min() * 1_000));
        }
        return (probes.Length, Identity.Sha256Bytes(memory.ToArray()));
    }

    private static IEnumerable<(MeshVector A, MeshVector B, MeshVector C)> Triangles(IReadOnlyList<MeshVector> p) { for (var i = 1; i + 1 < p.Count; i++) yield return (p[0], p[i], p[i + 1]); }
    private static double RayTriangle(MeshVector o, MeshVector d, MeshVector a, MeshVector b, MeshVector c)
    {
        const double epsilon = 1e-8; var e1 = Subtract(b, a); var e2 = Subtract(c, a); var h = Cross(d, e2); var det = Dot(e1, h); if (Math.Abs(det) < epsilon) return -1;
        var inv = 1 / det; var s = Subtract(o, a); var u = inv * Dot(s, h); if (u < 0 || u > 1) return -1; var q = Cross(s, e1); var v = inv * Dot(d, q); if (v < 0 || u + v > 1) return -1; var t = inv * Dot(e2, q); return t > epsilon ? t : -1;
    }

    private static string ResolveIndex(JsonElement root, int index, HashSet<int>? seen = null)
    {
        if (index == 0) return ""; seen ??= []; if (!seen.Add(index)) return "<cycle>";
        var value = index > 0 ? root.GetProperty("Exports")[index - 1] : root.GetProperty("Imports")[-index - 1]; var name = value.GetProperty("ObjectName").GetString() ?? "";
        var parent = ResolveIndex(root, value.GetProperty("OuterIndex").GetInt32(), seen); return string.IsNullOrEmpty(parent) ? name : parent + "." + name;
    }
    private static MeshVector Add(MeshVector a, MeshVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    private static MeshVector Subtract(MeshVector a, MeshVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static MeshVector Scale(MeshVector a, double value) => new(a.X * value, a.Y * value, a.Z * value);
    private static MeshVector Cross(MeshVector a, MeshVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Dot(MeshVector a, MeshVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static double Length(MeshVector a) => Math.Sqrt(Dot(a, a));
}
