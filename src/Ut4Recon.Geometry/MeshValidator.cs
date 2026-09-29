namespace Ut4Recon.Geometry;

public sealed class MeshValidator
{
    public MeshComparison Compare(StaticMeshIr expected, StaticMeshIr actual)
    {
        var diagnostics = new List<string>(); var render = true;
        if (expected.SchemaVersion != GeometryFormatVersions.StaticMeshIr || actual.SchemaVersion != GeometryFormatVersions.StaticMeshIr) { diagnostics.Add("Mesh IR schema is unsupported."); render = false; }
        if (expected.Lods.Count != actual.Lods.Count) { diagnostics.Add($"LOD count differs: {expected.Lods.Count} versus {actual.Lods.Count}."); render = false; }
        if (expected.Bounds != actual.Bounds) { diagnostics.Add("Render bounds differ."); render = false; }
        foreach (var pair in expected.Lods.Zip(actual.Lods))
        {
            if (pair.First.Positions.Count != pair.Second.Positions.Count) { diagnostics.Add($"LOD {pair.First.Index} vertex count differs."); render = false; }
            if (pair.First.Indices.Count != pair.Second.Indices.Count) { diagnostics.Add($"LOD {pair.First.Index} index count differs."); render = false; }
            if (pair.First.PositionHash != pair.Second.PositionHash) { diagnostics.Add($"LOD {pair.First.Index} position stream differs."); render = false; }
            if (pair.First.IndexHash != pair.Second.IndexHash) { diagnostics.Add($"LOD {pair.First.Index} index stream differs."); render = false; }
            if (pair.First.AttributeHash != pair.Second.AttributeHash) { diagnostics.Add($"LOD {pair.First.Index} tangent/UV streams differ."); render = false; }
            if (!pair.First.Sections.SequenceEqual(pair.Second.Sections)) { diagnostics.Add($"LOD {pair.First.Index} sections differ."); render = false; }
        }
        var collision = CollisionEquivalent(expected.Collision, actual.Collision, diagnostics);
        return new MeshComparison(GeometryFormatVersions.MeshComparison, GeometryFormatVersions.Profile, render, collision, diagnostics);
    }

    private static bool CollisionEquivalent(CollisionIr? expected, CollisionIr? actual, List<string> diagnostics)
    {
        if (expected is null || actual is null)
        {
            if (expected is null && actual is null) return true;
            diagnostics.Add("BodySetup presence differs."); return false;
        }
        var same = expected.TaggedGeometryHash == actual.TaggedGeometryHash && expected.OpaqueNativeHash == actual.OpaqueNativeHash && expected.OfflineQueryFingerprint == actual.OfflineQueryFingerprint &&
                   expected.CookedPayloads.Select(x => (x.Format, x.ElementCount, x.SizeOnDisk, x.PayloadHash)).SequenceEqual(actual.CookedPayloads.Select(x => (x.Format, x.ElementCount, x.SizeOnDisk, x.PayloadHash)));
        if (!same) diagnostics.Add("BodySetup tagged geometry or cooked collision payload differs.");
        return same;
    }
}
