using System.Globalization;
using System.Text;

namespace Ut4Recon.Geometry;

public sealed class MeshInterchangeWriter
{
    public void WriteObj(StaticMeshIr mesh, string path, int lodIndex = 0) => WriteObj(mesh, path, lodIndex, false);
    public void WriteEditorObj(StaticMeshIr mesh, string path, int lodIndex = 0) => WriteObj(mesh, path, lodIndex, true);
    public void WriteUnrealPreviewObj(StaticMeshIr mesh, string path, int lodIndex = 0) => WriteObj(mesh, path, lodIndex, false, true);

    private static void WriteObj(StaticMeshIr mesh, string path, int lodIndex, bool includeCollision, bool unrealNativeCoordinates = false)
    {
        var lod = mesh.Lods.Single(x => x.Index == lodIndex); var culture = CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine((unrealNativeCoordinates ? "# UT4Recon UT4-editor preview coordinates; source hash " : "# UT4Recon decoded render mesh; source hash ") + mesh.SourceFileSha256);
        writer.WriteLine("o " + Sanitize(mesh.ObjectName));
        foreach (var vertex in lod.Positions) WriteVector(writer, "v", unrealNativeCoordinates ? ToUt4ObjImport(vertex) : ToInterchange(vertex), culture);
        var uv = lod.UvChannels.FirstOrDefault(); if (uv is not null) foreach (var value in uv) writer.WriteLine($"vt {value.U.ToString("R", culture)} {(1 - value.V).ToString("R", culture)}");
        foreach (var normal in lod.TangentZ) WriteVector(writer, "vn", unrealNativeCoordinates ? ToUt4ObjImport(normal) : ToInterchange(normal), culture);
        for (var at = 0; at + 2 < lod.Indices.Count; at += 3)
        {
            var a = lod.Indices[at] + 1;
            // Both coordinate conversions are reflections. Generic interchange files reverse the face here;
            // the pinned UT4 importer performs the preview reflection itself, so preview faces must retain
            // the cooked index order or every imported triangle becomes back-facing.
            var b = lod.Indices[unrealNativeCoordinates ? at + 1 : at + 2] + 1;
            var c = lod.Indices[unrealNativeCoordinates ? at + 2 : at + 1] + 1;
            var complete = uv?.Count == lod.Positions.Count && lod.TangentZ.Count == lod.Positions.Count;
            writer.WriteLine(complete ? $"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}" : $"f {a} {b} {c}");
        }
        if (!includeCollision || mesh.Collision is null) return;
        var offset = lod.Positions.Count + 1;
        foreach (var hull in mesh.Collision.ConvexHulls)
        {
            writer.WriteLine("o UCX_" + Sanitize(mesh.ObjectName) + "_" + hull.Index.ToString("000", culture));
            foreach (var vertex in hull.Vertices) WriteVector(writer, "v", ToInterchange(vertex), culture);
            foreach (var face in ConvexFaces(hull.Vertices)) writer.WriteLine($"f {offset + face.A} {offset + face.C} {offset + face.B}");
            offset += hull.Vertices.Count;
        }
    }

    public void WriteCollisionObj(StaticMeshIr mesh, string path)
    {
        if (mesh.Collision is null) throw new InvalidOperationException("Mesh has no decoded BodySetup.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)); var culture = CultureInfo.InvariantCulture; var offset = 1;
        writer.WriteLine("# UT4Recon convex collision point clouds. Faces are intentionally omitted; Unreal must rebuild each convex hull.");
        foreach (var hull in mesh.Collision.ConvexHulls)
        {
            writer.WriteLine("o UCX_" + Sanitize(mesh.ObjectName) + "_" + hull.Index.ToString("000", culture));
            foreach (var vertex in hull.Vertices) WriteVector(writer, "v", ToInterchange(vertex), culture);
            foreach (var face in ConvexFaces(hull.Vertices)) writer.WriteLine($"f {offset + face.A} {offset + face.C} {offset + face.B}");
            offset += hull.Vertices.Count;
        }
    }

    private sealed record Face(int A, int B, int C);
    private static IReadOnlyList<Face> ConvexFaces(IReadOnlyList<MeshVector> vertices)
    {
        const double epsilon = 1e-5; var planes = new Dictionary<string, (MeshVector Normal, List<int> Vertices)>();
        for (var a = 0; a < vertices.Count; a++) for (var b = a + 1; b < vertices.Count; b++) for (var c = b + 1; c < vertices.Count; c++)
        {
            var normal = Cross(Subtract(vertices[b], vertices[a]), Subtract(vertices[c], vertices[a])); var length = Length(normal); if (length < epsilon) continue; normal = Scale(normal, 1 / length);
            var distances = vertices.Select(x => Dot(normal, Subtract(x, vertices[a]))).ToArray();
            if (distances.Any(x => x > epsilon) && distances.Any(x => x < -epsilon)) continue;
            if (distances.Any(x => x > epsilon)) normal = Scale(normal, -1);
            var offset = Dot(normal, vertices[a]); var key = $"{Math.Round(normal.X, 5)},{Math.Round(normal.Y, 5)},{Math.Round(normal.Z, 5)},{Math.Round(offset, 4)}";
            if (!planes.ContainsKey(key)) planes[key] = (normal, distances.Select((x, i) => (x, i)).Where(x => Math.Abs(x.x) <= epsilon).Select(x => x.i).ToList());
        }
        var faces = new List<Face>();
        foreach (var plane in planes.Values)
        {
            var ids = plane.Vertices.Distinct().ToArray(); if (ids.Length < 3) continue;
            var center = new MeshVector(ids.Average(i => vertices[i].X), ids.Average(i => vertices[i].Y), ids.Average(i => vertices[i].Z));
            var axis = Math.Abs(plane.Normal.Z) < .9 ? new MeshVector(0, 0, 1) : new MeshVector(0, 1, 0); var u = Normalize(Cross(axis, plane.Normal)); var v = Cross(plane.Normal, u);
            var ordered = ids.OrderBy(i => Math.Atan2(Dot(Subtract(vertices[i], center), v), Dot(Subtract(vertices[i], center), u))).ToArray();
            for (var i = 1; i + 1 < ordered.Length; i++) faces.Add(new Face(ordered[0], ordered[i], ordered[i + 1]));
        }
        return faces;
    }

    private static MeshVector ToInterchange(MeshVector value) => new(value.X, value.Z, -value.Y);
    // The pinned UT4 4.15 OBJ importer copies X/Z and negates Y. Pre-negate Y so the imported preview preserves cooked Unreal coordinates.
    private static MeshVector ToUt4ObjImport(MeshVector value) => new(value.X, -value.Y, value.Z);
    private static void WriteVector(TextWriter writer, string prefix, MeshVector value, CultureInfo culture) => writer.WriteLine($"{prefix} {value.X.ToString("R", culture)} {value.Y.ToString("R", culture)} {value.Z.ToString("R", culture)}");
    private static MeshVector Subtract(MeshVector a, MeshVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    private static MeshVector Scale(MeshVector a, double scale) => new(a.X * scale, a.Y * scale, a.Z * scale);
    private static double Dot(MeshVector a, MeshVector b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    private static MeshVector Cross(MeshVector a, MeshVector b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    private static double Length(MeshVector a) => Math.Sqrt(Dot(a, a));
    private static MeshVector Normalize(MeshVector a) => Scale(a, 1 / Length(a));
    private static string Sanitize(string value) => new(value.Select(x => char.IsLetterOrDigit(x) || x == '_' ? x : '_').ToArray());
}
