using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class CollisionBoxTemplateTests
{
    [Fact]
    public void CertifiedBoxAddsACompleteDeterministicClosureWithoutAMapDonor()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var baseline = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap");
            var manifest = Path.Combine(root, "fixtures", "collision-box-template", "ut4-4.15-windows-no-editor-v1", "collision-box-template.json");
            var first = Path.Combine(temporary, "first.umap"); var second = Path.Combine(temporary, "second.umap");
            using var location = JsonDocument.Parse("{\"x\":120,\"y\":-30,\"z\":45}"); using var rotation = JsonDocument.Parse("{\"pitch\":0,\"yaw\":16384,\"roll\":0}"); using var scale = JsonDocument.Parse("{\"x\":2,\"y\":0.5,\"z\":3}");
            var service = new CollisionBoxTemplateService(); var certified = service.ValidateBundle(manifest, "ut4-4.15-windows-no-editor-v1");
            Assert.Equal(new CollisionBoxDimensions(100, 100, 100), certified.Dimensions);
            var result = service.AddToMap(baseline, first, manifest, certified.Profile, "UT4Recon_Box_Test", location.RootElement, rotation.RootElement, scale.RootElement);
            service.AddToMap(baseline, second, manifest, certified.Profile, "UT4Recon_Box_Test", location.RootElement, rotation.RootElement, scale.RootElement);
            Assert.Equal(4, result.AddedExportIndexes.Count); Assert.Equal(Identity.Sha256File(first), Identity.Sha256File(second));

            var before = new PackageInspector().Inspect(baseline, "UnrealTournament/Content/RestrictedAssets/Maps/Example_Map.umap");
            var after = new PackageInspector().Inspect(first, "UnrealTournament/Content/RestrictedAssets/Maps/Example_Map.umap");
            Assert.Equal(before.ExportCount + 4, after.ExportCount); Assert.Empty(after.Diagnostics);
            var changedExisting = before.Objects.Where(x => after.Objects.Single(y => y.ExportIndex == x.ExportIndex).PayloadHash != x.PayloadHash).ToArray();
            Assert.Single(changedExisting); Assert.Equal("/Script/Engine.Level", changedExisting[0].ClassPath);
            var asset = new UAsset(first, EngineVersion.VER_UE4_15); var level = asset.Exports.OfType<LevelExport>().Single();
            Assert.Contains(level.Actors, x => x.Index == result.AddedExportIndexes.Min());
            var classes = result.AddedExportIndexes.Select(x => asset.Exports[x - 1].GetExportClassType().ToString()).Order().ToArray();
            Assert.Equal(new[] { "BlockingVolume", "BodySetup", "BrushComponent", "Model" }, classes);
        }
        finally { Directory.Delete(temporary, true); }
    }

    [Fact]
    public void CertifiedCustomCollisionAddsDonorOwnedGeometryDeterministically()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temporary);
        try
        {
            var baseline = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap");
            var donorManifest = Path.Combine(root, "fixtures", "custom-collision-donor", "rectangular-prism-v1", "custom-collision-donor.json");
            var boxManifest = Path.Combine(root, "fixtures", "collision-box-template", "ut4-4.15-windows-no-editor-v1", "collision-box-template.json");
            var first = Path.Combine(temporary, "first.umap"); var second = Path.Combine(temporary, "second.umap");
            using var location = JsonDocument.Parse("{\"x\":100,\"y\":200,\"z\":300}"); using var rotation = JsonDocument.Parse("{\"pitch\":0,\"yaw\":8192,\"roll\":0}"); using var scale = JsonDocument.Parse("{\"x\":1,\"y\":2,\"z\":0.5}");
            var service = new CollisionBoxTemplateService(); var donor = service.ValidateCustomBundle(donorManifest, "ut4-4.15-windows-no-editor-v1"); var box = service.ValidateBundle(boxManifest, donor.Profile);
            Assert.Equal(6, donor.PolygonCount); Assert.Equal(8, donor.VertexCount); Assert.Equal(new CollisionBoxDimensions(-120, -120, -50), donor.BoundsMinimum);
            Assert.Equal(new CollisionBoxDimensions(120, 120, 50), donor.BoundsMaximum); Assert.NotEqual(box.GeometryFingerprint, donor.GeometryFingerprint);
            var result = service.AddCustomToMap(baseline, first, donorManifest, donor.Profile, "UT4Recon_Custom_Test", location.RootElement, rotation.RootElement, scale.RootElement);
            service.AddCustomToMap(baseline, second, donorManifest, donor.Profile, "UT4Recon_Custom_Test", location.RootElement, rotation.RootElement, scale.RootElement);
            Assert.Equal(4, result.AddedExportIndexes.Count); Assert.Equal(Identity.Sha256File(first), Identity.Sha256File(second));
            var before = new PackageInspector().Inspect(baseline, "UnrealTournament/Content/RestrictedAssets/Maps/Example_Map.umap");
            var after = new PackageInspector().Inspect(first, "UnrealTournament/Content/RestrictedAssets/Maps/Example_Map.umap");
            Assert.Equal(before.ExportCount + 4, after.ExportCount); Assert.Empty(after.Diagnostics);
            Assert.Single(before.Objects, x => after.Objects.Single(y => y.ExportIndex == x.ExportIndex).PayloadHash != x.PayloadHash);
        }
        finally { Directory.Delete(temporary, true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
