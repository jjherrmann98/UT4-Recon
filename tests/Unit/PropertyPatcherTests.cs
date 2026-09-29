using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class PropertyPatcherTests
{
    [Fact]
    public void RenamesCookedPackageIdentityWithoutChangingExportPayloads()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(outputDirectory, "Glass_Fixed.umap");
        try
        {
            var inspector = new PackageInspector(); const string beforeInternal = "UnrealTournament/Content/Maps/Glass/Glass.umap";
            var before = inspector.Inspect(input, beforeInternal);
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/Game/Maps/Glass/Glass"] = "/Game/Maps/Glass/Glass_Fixed",
                ["Glass"] = "Glass_Fixed"
            };
            var result = new PackageIdentityRenamer().Rename(input, output, replacements);
            var after = inspector.Inspect(output, "UnrealTournament/Content/Maps/Glass/Glass_Fixed.umap");

            Assert.True(result.ReplacedNameEntries >= 1);
            Assert.Equal(before.ExportCount, after.ExportCount);
            Assert.Equal("/Game/Maps/Glass/Glass_Fixed", after.PackagePath);
            Assert.Equal(before.Objects.OrderBy(x => x.ExportIndex).Select(x => x.PayloadHash), after.Objects.OrderBy(x => x.ExportIndex).Select(x => x.PayloadHash));
        }
        finally { if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true); }
    }

    [Fact]
    public void PatchesOneTransformAndPreservesEveryOtherExportPayload()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(outputDirectory, "Glass.umap");
        try
        {
            var inspector = new PackageInspector();
            var beforePackage = inspector.Inspect(input, "UnrealTournament/Content/Maps/Glass/Glass.umap");
            var target = beforePackage.Objects.Single(x => x.ObjectPath.EndsWith("Floor.StaticMeshComponent0", StringComparison.Ordinal));
            var patcher = new PropertyPatcher();
            var before = patcher.ReadValue(input, target.ExportIndex, "RelativeLocation");
            var after = JsonSerializer.SerializeToElement(new { x = 12.5, y = -3.25, z = 40.0 });
            var staleBefore = JsonSerializer.SerializeToElement(new { x = 999.0, y = 0.0, z = 0.0 });
            Assert.Throws<InvalidDataException>(() => patcher.Apply(input, output, target.ExportIndex, "RelativeLocation", staleBefore, after));
            Assert.False(File.Exists(output));

            var result = patcher.Apply(input, output, target.ExportIndex, "RelativeLocation", before, after);
            Assert.True(PropertyPatcher.JsonEquivalent(after, result.After));
            Assert.Equal(result.ExtrasHashBefore, result.ExtrasHashAfter);

            var afterPackage = inspector.Inspect(output, "UnrealTournament/Content/Maps/Glass/Glass.umap");
            var changed = beforePackage.Objects.Zip(afterPackage.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray();
            Assert.Single(changed);
            Assert.Equal(target.Id, changed[0].First.Id);
            Assert.Equal(target.Id, changed[0].Second.Id);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public void PatchesOneCollisionShapeAndPreservesEveryOtherExportPayload()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Example_Map", "cooked", "Example_Map.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(outputDirectory, "Example_Map.umap");
        try
        {
            var inspector = new PackageInspector(); var internalPath = "UnrealTournament/Content/Maps/Example_Map/Example_Map.umap";
            var beforePackage = inspector.Inspect(input, internalPath); var target = beforePackage.Objects.Single(x => x.ExportIndex == 139);
            Assert.EndsWith("Box_Collision", target.ObjectPath, StringComparison.Ordinal);
            var patcher = new PropertyPatcher(); var before = patcher.ReadValue(input, target.ExportIndex, "BoxExtent");
            var after = JsonSerializer.SerializeToElement(new { x = 7.5, y = 195.0, z = 95.0 });

            patcher.Apply(input, output, target.ExportIndex, "BoxExtent", before, after);
            var afterPackage = inspector.Inspect(output, internalPath);
            var changed = beforePackage.Objects.Zip(afterPackage.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray();
            Assert.Single(changed); Assert.Equal(target.Id, changed[0].First.Id);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public void PatchesOneCollisionResponseAndOneScalarWithoutChangingOtherExports()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Example_Map", "cooked", "Example_Map.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var intermediate = Path.Combine(outputDirectory, "Example_Map-step1.umap");
        var output = Path.Combine(outputDirectory, "Example_Map.umap");
        try
        {
            var inspector = new PackageInspector(); var internalPath = "UnrealTournament/Content/Maps/Example_Map/Example_Map.umap";
            var beforePackage = inspector.Inspect(input, internalPath); var patcher = new PropertyPatcher();
            var responseTarget = beforePackage.Objects.Single(x => x.ExportIndex == 809);
            var responseBefore = patcher.ReadValue(input, responseTarget.ExportIndex, "BodyInstance.CollisionResponses.Pawn");
            Assert.Equal("ECR_Ignore", responseBefore.GetString());
            patcher.Apply(input, intermediate, responseTarget.ExportIndex, "BodyInstance.CollisionResponses.Pawn", responseBefore, JsonSerializer.SerializeToElement("ECR_Block"));

            var scalarTarget = beforePackage.Objects.Single(x => x.ExportIndex == 272);
            var scalarBefore = patcher.ReadValue(intermediate, scalarTarget.ExportIndex, "SphereRadius");
            patcher.Apply(intermediate, output, scalarTarget.ExportIndex, "SphereRadius", scalarBefore, JsonSerializer.SerializeToElement(450.0f));

            var afterPackage = inspector.Inspect(output, internalPath);
            var changed = beforePackage.Objects.Zip(afterPackage.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray();
            Assert.Equal(2, changed.Length);
            Assert.Equal(new[] { responseTarget.Id, scalarTarget.Id }.OrderBy(x => x), changed.Select(x => x.First.Id).OrderBy(x => x));
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public void ReplacesMaterialOverrideWithAnExistingPackageReferenceOnly()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var output = Path.Combine(outputDirectory, "Glass.umap");
        try
        {
            var inspector = new PackageInspector(); var internalPath = "UnrealTournament/Content/Maps/Glass/Glass.umap";
            var beforePackage = inspector.Inspect(input, internalPath); var patcher = new PropertyPatcher();
            var target = beforePackage.Objects.Single(x => x.ExportIndex == 37);
            var donor = beforePackage.Objects.Single(x => x.ExportIndex == 40);
            var before = patcher.ReadValue(input, target.ExportIndex, "OverrideMaterials");
            var after = patcher.ReadValue(input, donor.ExportIndex, "OverrideMaterials");
            Assert.False(PropertyPatcher.JsonEquivalent(before, after));

            patcher.Apply(input, output, target.ExportIndex, "OverrideMaterials", before, after);
            var afterPackage = inspector.Inspect(output, internalPath);
            var changed = beforePackage.Objects.Zip(afterPackage.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray();
            Assert.Single(changed);
            Assert.Equal(target.Id, changed[0].First.Id);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public void RoundTripsAndPatchesASplitUexpPackage()
    {
        var source = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N"));
        var input = Path.Combine(outputDirectory, "Glass.umap"); var output = Path.Combine(outputDirectory, "Glass-patched.umap");
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var split = new UAsset(source, EngineVersion.VER_UE4_15) { UseSeparateBulkDataFiles = true };
            split.Write(input);
            Assert.True(File.Exists(Path.ChangeExtension(input, ".uexp")));

            var inspector = new PackageInspector(); const string internalPath = "UnrealTournament/Content/Maps/Glass/Glass.umap";
            var beforePackage = inspector.Inspect(input, internalPath); Assert.Empty(beforePackage.Diagnostics);
            var target = beforePackage.Objects.Single(x => x.ObjectPath.EndsWith("Floor.StaticMeshComponent0", StringComparison.Ordinal));
            var patcher = new PropertyPatcher(); var before = patcher.ReadValue(input, target.ExportIndex, "RelativeLocation");
            var after = JsonSerializer.SerializeToElement(new { x = 1.0, y = 2.0, z = 3.0 });
            patcher.Apply(input, output, target.ExportIndex, "RelativeLocation", before, after);

            Assert.True(File.Exists(Path.ChangeExtension(output, ".uexp")));
            var afterPackage = inspector.Inspect(output, internalPath); Assert.Empty(afterPackage.Diagnostics);
            var changed = beforePackage.Objects.Zip(afterPackage.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray();
            Assert.Single(changed); Assert.Equal(target.Id, changed[0].First.Id);
        }
        finally
        {
            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true);
        }
    }

    [Fact]
    public void DeletesPersistentActorReferenceAndPreservesActorClosure()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var output = Path.Combine(outputDirectory, "Glass.umap");
        try
        {
            var inspector = new PackageInspector(); const string internalPath = "UnrealTournament/Content/Maps/Glass/Glass.umap"; var before = inspector.Inspect(input, internalPath);
            var actor = before.Objects.Single(x => x.ObjectPath == "Glass.PersistentLevel.Cube2"); var patcher = new PropertyPatcher(); Assert.True(patcher.IsPersistentLevelActor(input, actor.ExportIndex));
            var deletion = patcher.DeleteActor(input, output, actor.ExportIndex); var after = inspector.Inspect(output, internalPath);
            var changed = before.Objects.Zip(after.Objects).Where(x => x.First.PayloadHash != x.Second.PayloadHash).ToArray(); Assert.Single(changed);
            Assert.Equal(deletion.LevelExportIndex, changed[0].First.ExportIndex); Assert.Equal(actor.PayloadHash, after.Objects.Single(x => x.Id == actor.Id).PayloadHash);
        }
        finally { if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true); }
    }

    [Fact]
    public void ClonesSimpleStaticMeshActorClosureAndRemapsItsReferences()
    {
        var input = Path.Combine(RepositoryRoot(), "research", "pairs", "Glass", "cooked", "Glass.umap");
        var outputDirectory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var output = Path.Combine(outputDirectory, "Glass.umap");
        try
        {
            var inspector = new PackageInspector(); const string internalPath = "UnrealTournament/Content/Maps/Glass/Glass.umap"; var before = inspector.Inspect(input, internalPath);
            var actor = before.Objects.Single(x => x.ObjectPath == "Glass.PersistentLevel.Cube2"); var location = JsonSerializer.SerializeToElement(new { x = 1500.0, y = -350.0, z = 120.0 });
            var result = new PropertyPatcher().CloneStaticMeshActor(input, output, actor.ExportIndex, "UT4ReconAddedCube", location); var after = inspector.Inspect(output, internalPath);
            Assert.Equal(before.ExportCount + 2, after.ExportCount); Assert.Equal(2, result.AddedExportIndexes.Count);
            var addedActor = after.Objects.Single(x => x.ObjectPath == "Glass.PersistentLevel.UT4ReconAddedCube"); Assert.Equal("/Script/Engine.StaticMeshActor", addedActor.ClassPath);
            var addedComponent = after.Objects.Single(x => x.ObjectPath == "Glass.PersistentLevel.UT4ReconAddedCube.StaticMeshComponent0");
            Assert.True(PropertyPatcher.JsonEquivalent(location, new PropertyPatcher().ReadValue(output, addedComponent.ExportIndex, "RelativeLocation")));
            Assert.Equal(before.Objects.Single(x => x.ObjectPath == "Glass.PersistentLevel.Cube2.StaticMeshComponent0").SerialSize, addedComponent.SerialSize);
        }
        finally { if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
