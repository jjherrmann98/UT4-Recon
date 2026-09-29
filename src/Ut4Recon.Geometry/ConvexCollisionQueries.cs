using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public static class ConvexCollisionQueries
{
    private sealed record Plane(MeshVector Normal, double Distance);

    public static (int Count, string Fingerprint) Fingerprint(IReadOnlyList<ConvexHull> hulls)
    {
        if (hulls.Count == 0) return (0, Identity.Sha256Bytes([]));
        var min = new MeshVector(hulls.Min(x => x.BoundsMinimum.X), hulls.Min(x => x.BoundsMinimum.Y), hulls.Min(x => x.BoundsMinimum.Z));
        var max = new MeshVector(hulls.Max(x => x.BoundsMaximum.X), hulls.Max(x => x.BoundsMaximum.Y), hulls.Max(x => x.BoundsMaximum.Z));
        var center = Scale(Add(min, max), .5); var extent = Subtract(max, min); var margin = Math.Max(extent.X, Math.Max(extent.Y, extent.Z)) + 1;
        var probes = new[]
        {
            (new MeshVector(min.X-margin, center.Y, center.Z), new MeshVector(1,0,0)), (new MeshVector(max.X+margin, center.Y, center.Z), new MeshVector(-1,0,0)),
            (new MeshVector(center.X, min.Y-margin, center.Z), new MeshVector(0,1,0)), (new MeshVector(center.X, max.Y+margin, center.Z), new MeshVector(0,-1,0)),
            (new MeshVector(center.X, center.Y, min.Z-margin), new MeshVector(0,0,1)), (new MeshVector(center.X, center.Y, max.Z+margin), new MeshVector(0,0,-1)),
            (new MeshVector(min.X-margin,min.Y-margin,min.Z-margin), Normalize(new MeshVector(1,1,1))),
            (new MeshVector(max.X+margin,max.Y+margin,max.Z+margin), Normalize(new MeshVector(-1,-1,-1)))
        };
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        foreach (var probe in probes)
        {
            var hit = hulls.Select(x => Raycast(x.Vertices, probe.Item1, probe.Item2)).Where(x => x is not null).Min(); writer.Write(hit is not null); writer.Write(hit is null ? -1L : (long)Math.Round(hit.Value * 1_000_000));
        }
        return (probes.Length, Identity.Sha256Bytes(memory.ToArray()));
    }

    public static double? Raycast(IReadOnlyList<MeshVector> vertices, MeshVector origin, MeshVector direction)
    {
        var entry = 0.0; var exit = double.PositiveInfinity;
        foreach (var plane in Planes(vertices))
        {
            var numerator = plane.Distance - Dot(plane.Normal, origin); var denominator = Dot(plane.Normal, direction);
            if (Math.Abs(denominator) < 1e-9) { if (numerator < -1e-5) return null; continue; }
            var time = numerator / denominator; if (denominator < 0) entry = Math.Max(entry, time); else exit = Math.Min(exit, time); if (entry > exit) return null;
        }
        return exit >= 0 ? Math.Max(0, entry) : null;
    }

    private static IReadOnlyList<Plane> Planes(IReadOnlyList<MeshVector> vertices)
    {
        const double epsilon = 1e-5; var result = new Dictionary<string, Plane>();
        for (var a = 0; a < vertices.Count; a++) for (var b = a + 1; b < vertices.Count; b++) for (var c = b + 1; c < vertices.Count; c++)
        {
            var normal = Cross(Subtract(vertices[b], vertices[a]), Subtract(vertices[c], vertices[a])); var length = Length(normal); if (length < epsilon) continue; normal = Scale(normal, 1 / length);
            var distances = vertices.Select(x => Dot(normal, Subtract(x, vertices[a]))).ToArray(); if (distances.Any(x => x > epsilon) && distances.Any(x => x < -epsilon)) continue;
            if (distances.Any(x => x > epsilon)) normal = Scale(normal, -1); var distance = Dot(normal, vertices[a]);
            var key = $"{Math.Round(normal.X, 5)},{Math.Round(normal.Y, 5)},{Math.Round(normal.Z, 5)},{Math.Round(distance, 4)}"; result.TryAdd(key, new Plane(normal, distance));
        }
        return result.Values.ToArray();
    }
    private static MeshVector Add(MeshVector a, MeshVector b) => new(a.X+b.X,a.Y+b.Y,a.Z+b.Z);
    private static MeshVector Subtract(MeshVector a, MeshVector b) => new(a.X-b.X,a.Y-b.Y,a.Z-b.Z);
    private static MeshVector Scale(MeshVector a,double s)=>new(a.X*s,a.Y*s,a.Z*s);
    private static double Dot(MeshVector a,MeshVector b)=>a.X*b.X+a.Y*b.Y+a.Z*b.Z;
    private static MeshVector Cross(MeshVector a,MeshVector b)=>new(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);
    private static double Length(MeshVector a)=>Math.Sqrt(Dot(a,a));
    private static MeshVector Normalize(MeshVector a)=>Scale(a,1/Length(a));
}
