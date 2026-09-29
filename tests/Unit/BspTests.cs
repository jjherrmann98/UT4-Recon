using Ut4Recon.Geometry;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class BspTests
{
    private const string InternalPath = "UnrealTournament/Content/RestrictedAssets/Maps/Example_Map.umap";

    [Fact]
    public void DecodesExampleMapAndCreatesRecoveredBrushWorkspace()
    {
        var root = RepositoryRoot(); var input = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap"); var map = new NativeBspDecoder().Decode(input, InternalPath);
        Assert.Empty(map.Diagnostics); Assert.Equal(7, map.Models.Count); var model = map.Models.OrderByDescending(x => x.Polygons.Count).First();
        Assert.Equal(352, model.ExportIndex); Assert.Equal(15, model.Vectors.Count); Assert.Equal(1627, model.Points.Count); Assert.Equal(987, model.Nodes.Count); Assert.Equal(429, model.Surfaces.Count); Assert.Equal(8662, model.VertexPool.Count); Assert.Equal(987, model.Polygons.Count);
        Assert.Equal(314850, model.ParsedGeometryBytes); Assert.Equal(166472, model.RemainingNativeBytes); Assert.Equal(6, model.Metrics.OfflineQueryCount);
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        try { var workspace = new BspInterchangeWriter().CreateWorkspace(map, directory); Assert.True(File.Exists(workspace.T3d)); Assert.True(File.Exists(workspace.Obj)); var t3d = File.ReadAllText(workspace.T3d); Assert.Contains("Begin Brush Name=RecoveredModel", t3d); Assert.Equal(987, t3d.Split('\n').Count(x => x.StartsWith("Begin Polygon", StringComparison.Ordinal))); Assert.Contains("original additive/subtractive brush history", workspace.InventedEditorMetadata); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void RebuiltRecoveredBrushMeetsDeclaredGeometryTolerance()
    {
        var root = RepositoryRoot(); var decoder = new NativeBspDecoder(); var original = decoder.Decode(Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap"), InternalPath);
        var rebuilt = decoder.Decode(Path.Combine(root, "research", "investigation", "cooked-fixtures", "RecoveredBSP.umap"), "UnrealTournament/Content/Reconstruction/RecoveredBSP.umap"); var report = new BspValidator().Compare(original, rebuilt, requireOfflineQueries: true);
        Assert.True(report.WithinTolerance); Assert.True(report.OfflineQueriesEquivalent); Assert.Equal(0, report.MaximumBoundsDisplacement); Assert.InRange(report.SurfaceAreaDifferenceRatio, 0, 0.00000001);
    }

    [Fact]
    public void GraftsChangedBspWhilePreservingLevelAndUnrelatedExports()
    {
        var root = RepositoryRoot(); var baseline = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap"); var donor = Path.Combine(root, "research", "investigation", "cooked-fixtures", "Example_Map_BspEdited.umap");
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "Example_Map.umap");
        try { var report = new BspGraftEngine().Graft(baseline, donor, output, InternalPath); Assert.True(report.Passed); Assert.True(report.GeometryMatchesDonor); Assert.True(report.LevelExportsPreserved); Assert.True(report.CollisionClosureMatchesDonor); Assert.Equal([352], report.ChangedClosureExportIndices); Assert.Equal(2128, report.UnrelatedExportsVerified); }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void GraftsFreshRecoveredBrushShellAndRemapsItsCollisionClosure()
    {
        var root = RepositoryRoot(); var baseline = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap"); var donor = Path.Combine(root, "research", "investigation", "cooked-fixtures", "RecoveredBSP.umap");
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "Example_Map.umap");
        try
        {
            var report = new FreshBspGraftEngine().Graft(baseline, donor, output, InternalPath);
            Assert.True(report.Passed, string.Join("; ", report.Diagnostics)); Assert.True(report.GeometryMatchesDonor); Assert.True(report.CollisionClosureMatchesDonor); Assert.True(report.LevelExportsPreserved);
            Assert.Equal(134, report.ChangedClosureExportIndices.Count); Assert.Equal([2269, 2270], report.AddedClosureExportIndices); Assert.Equal(2134, report.UnrelatedExportsVerified);
            var outputMap = new NativeBspDecoder().Decode(output, InternalPath); var donorMap = new NativeBspDecoder().Decode(donor, "UnrealTournament/Content/Reconstruction/RecoveredBSP.umap");
            var comparison = new BspValidator().Compare(outputMap, donorMap, requireOfflineQueries: true); Assert.True(comparison.WithinTolerance); Assert.True(comparison.OfflineQueriesEquivalent); Assert.Equal(0, comparison.MaximumBoundsDisplacement); Assert.Equal(0, comparison.SurfaceAreaDifferenceRatio);
            Assert.Equal(1005, outputMap.Models.OrderByDescending(x => x.Polygons.Count).First().Polygons.Count);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
