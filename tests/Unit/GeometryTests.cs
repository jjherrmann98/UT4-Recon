using Ut4Recon.Geometry;
using Ut4Recon.Package;
using Ut4Recon.Core;

namespace Ut4Recon.UnitTests;

public class GeometryTests
{
    [Fact]
    public void DecodesChairRenderAndCollisionDeterministically()
    {
        var root = RepositoryRoot(); var providerRoot = Path.Combine(root, "research", "investigation"); const string path = "cooked-fixtures/SM_Chair.uasset";
        var decoder = new NativeMeshDecoder(); var first = decoder.Decode(providerRoot, path); var second = decoder.Decode(providerRoot, path);
        Assert.Empty(first.Diagnostics); Assert.Equal(GeometryFormatVersions.Profile, first.Profile); Assert.Equal(3, first.StaticMeshExportIndex);
        var lod = Assert.Single(first.Lods); Assert.Equal(1467, lod.Positions.Count); Assert.Equal(5346, lod.Indices.Count); Assert.Equal(1782, Assert.Single(lod.Sections).TriangleCount);
        Assert.Equal(1467, Assert.Single(lod.UvChannels).Count); Assert.Equal(lod.PositionHash, second.Lods[0].PositionHash); Assert.Equal(lod.IndexHash, second.Lods[0].IndexHash);
        var collision = Assert.IsType<CollisionIr>(first.Collision); Assert.True(collision.HasConsistentSimpleCollision); Assert.Equal(16, Assert.Single(collision.ConvexHulls).Vertices.Count);
        Assert.Equal(8, collision.OfflineQueryCount); Assert.Equal(64, collision.OfflineQueryFingerprint.Length);
        var physx = Assert.Single(collision.CookedPayloads); Assert.Equal("PhysXPC", physx.Format); Assert.Equal(51929, physx.SizeOnDisk); Assert.True(physx.IsInline);
        var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"), "SM_Chair.obj");
        try
        {
            new MeshInterchangeWriter().WriteEditorObj(first, temporary); var obj = File.ReadAllText(temporary);
            Assert.Contains("o SM_Chair", obj); Assert.Contains("o UCX_SM_Chair_000", obj); Assert.Equal(1814, obj.Split('\n').Count(x => x.StartsWith("f ", StringComparison.Ordinal)));
            var workspace = new MeshEditorWorkspaceGenerator().Create(first, Path.Combine(Path.GetDirectoryName(temporary)!, "workspace"));
            Assert.All(new[] { workspace.MeshIr, workspace.EditorObj, workspace.RenderObj, workspace.CollisionObj, workspace.ImportSettings }, path => Assert.True(File.Exists(path)));
        }
        finally { var directory = Path.GetDirectoryName(temporary)!; if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }


    [Fact]
    public void GraftsCollisionWhilePreservingRenderExport()
    {
        var root = RepositoryRoot(); var input = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair.uasset");
        var donor = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair_CollisionEdited.uasset");
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var output = Path.Combine(directory, "SM_Chair.uasset");
        try
        {
            var report = new CollisionGraftEngine().Graft(input, donor, output, "UnrealTournament/Content/InvestigationAssets/SM_Chair.uasset");
            Assert.True(report.Passed); Assert.True(report.RenderPreserved); Assert.True(report.CollisionMatchesDonor); Assert.Equal(report.StaticMeshPayloadHashBefore, report.StaticMeshPayloadHashAfter);
            var fixtureRoot = Path.Combine(root, "research", "investigation"); var providerRoot = Path.GetDirectoryName(output)!; var decoder = new NativeMeshDecoder();
            var decodedOriginal = decoder.Decode(fixtureRoot, "cooked-fixtures/SM_Chair.uasset"); var decodedDonor = decoder.Decode(fixtureRoot, "cooked-fixtures/SM_Chair_CollisionEdited.uasset");
            var changed = new MeshValidator().Compare(decodedOriginal, decodedDonor); Assert.True(changed.RenderEquivalent); Assert.False(changed.CollisionEquivalent);
            var decodedOutput = decoder.Decode(providerRoot, "SM_Chair.uasset"); var comparison = new MeshValidator().Compare(decodedDonor, decodedOutput);
            Assert.True(comparison.RenderEquivalent); Assert.True(comparison.CollisionEquivalent); Assert.Empty(comparison.Diagnostics);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void GeneratesMeshWorkspaceFromRecoveryIr()
    {
        var root = RepositoryRoot(); var source = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair.uasset");
        var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); const string internalPath = "UnrealTournament/Content/InvestigationAssets/SM_Chair.uasset";
        try
        {
            var baseline = Path.Combine(temporary, "baseline"); var destination = Path.Combine(baseline, internalPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
            var package = new PackageInspector().Inspect(destination, internalPath); var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", Identity.File(source), [package]);
            var index = new RecoveryMeshWorkspaceGenerator().Create(baseline, ir, Path.Combine(temporary, "workspaces")); var entry = Assert.Single(index.Entries);
            Assert.Equal("awaiting-editor-import-and-cooked-donor", entry.Status); Assert.NotNull(entry.WorkspacePath); Assert.True(File.Exists(Path.Combine(entry.WorkspacePath!, "SM_Chair.editor.obj")));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void StagesRecoveredRenderMeshUnderOriginalAssetName()
    {
        var root = RepositoryRoot(); var source = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair.uasset");
        var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); const string internalPath = "UnrealTournament/Content/InvestigationAssets/SM_Chair.uasset";
        try
        {
            var baseline = Path.Combine(temporary, "baseline"); var destination = Path.Combine(baseline, internalPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
            var package = new PackageInspector().Inspect(destination, internalPath); var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", Identity.File(source), [package]);
            var index = new RecoveryMeshWorkspaceGenerator().Create(baseline, ir, Path.Combine(temporary, "workspaces"));
            var manifestPath = Path.Combine(temporary, "mesh-preview-import.json"); var manifest = new MeshPreviewImportWriter().Write(index, Path.Combine(temporary, "preview"), manifestPath);
            var entry = Assert.Single(manifest.Entries); Assert.Equal("/Game/InvestigationAssets", entry.DestinationPath); Assert.Equal("SM_Chair", entry.AssetName);
            Assert.Equal("SM_Chair.obj", Path.GetFileName(entry.SourceObj)); Assert.True(File.Exists(entry.SourceObj)); Assert.True(File.Exists(manifestPath));
            var mesh = System.Text.Json.JsonSerializer.Deserialize<StaticMeshIr>(File.ReadAllText(Path.Combine(index.Entries[0].WorkspacePath!, "SM_Chair.mesh-ir.json")), ReconJson.Options)!;
            var vertex = File.ReadLines(entry.SourceObj).First(line => line.StartsWith("v ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var expected = mesh.Lods[0].Positions[0];
            Assert.Equal(expected.X, double.Parse(vertex[1], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(-expected.Y, double.Parse(vertex[2], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(expected.Z, double.Parse(vertex[3], System.Globalization.CultureInfo.InvariantCulture));
            var face = File.ReadLines(entry.SourceObj).First(line => line.StartsWith("f ", StringComparison.Ordinal)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var expectedFace = mesh.Lods[0].Indices.Take(3).Select(index => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.Equal(expectedFace, face.Skip(1).Select(token => token.Split('/')[0]).ToArray());
            Assert.Contains("UT4-editor preview coordinates", File.ReadLines(entry.SourceObj).First());
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
