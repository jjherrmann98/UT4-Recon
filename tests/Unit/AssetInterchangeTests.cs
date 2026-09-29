using Ut4Recon.Core;
using Ut4Recon.Package;
using System.Text.Json;

namespace Ut4Recon.UnitTests;

public class AssetInterchangeTests
{
    [Fact]
    public void ExtractsPortableDeterministicCollisionBundleAndValidatesChangedDonor()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var state = Path.Combine(temporary, ".ut4recon");
        try
        {
            const string internalPath = "UnrealTournament/Content/InvestigationAssets/SM_Chair.uasset";
            var source = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair.uasset"); var edited = Path.Combine(root, "research", "investigation", "cooked-fixtures", "SM_Chair_CollisionEdited.uasset");
            var baseline = Path.Combine(state, "baseline", internalPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(baseline)!); File.Copy(source, baseline);
            var package = new PackageInspector().Inspect(baseline, internalPath); var sourceIdentity = Identity.File(source);
            var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", sourceIdentity, [package]); ReconJson.Write(Path.Combine(state, "reconstruction-ir.json"), ir);
            var pak = new PakInventory(source, "/", false, sourceIdentity, sourceIdentity, null, []);
            ReconJson.Write(Path.Combine(state, "input-manifest.json"), new InputManifest(1, new ToolIdentity("test", "1"), DateTimeOffset.UnixEpoch, ir.Profile, pak, []));
            var body = package.Objects.Single(x => x.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) == true); var service = new AssetInterchangeService();
            var firstRoot = Path.Combine(temporary, "bundle-a"); var secondRoot = Path.Combine(temporary, "bundle-b"); var first = service.Extract(state, ir, body.Id, firstRoot); service.Extract(state, ir, body.Id, secondRoot);

            Assert.Equal(AssetReplacementKind.StaticMeshCollision, first.Kind); Assert.Equal(3, first.Closure.Count); Assert.Contains(first.Closure, x => x.Role == "preserved-render-root");
            Assert.DoesNotContain(Directory.EnumerateFiles(firstRoot, "*", SearchOption.AllDirectories).Where(x => Path.GetExtension(x) is ".json" or ".md" or ".txt").Select(File.ReadAllText), text => text.Contains(temporary, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(TreeHashes(firstRoot), TreeHashes(secondRoot));
            var valid = service.ValidateDonor(firstRoot, edited, DonorMode.CompatibleShell); Assert.True(valid.Passed); Assert.Contains(1, valid.ChangedExportIndices);
            var unchanged = service.ValidateDonor(firstRoot, source, DonorMode.CompatibleShell); Assert.False(unchanged.Passed); Assert.Contains(unchanged.Checks, x => x.Name == "selected-root-changed" && !x.Passed);
            var extractionPath = Path.Combine(firstRoot, "extraction-manifest.json");
            var extraction = JsonSerializer.Deserialize<AssetExtractionManifest>(File.ReadAllText(extractionPath), ReconJson.Options)!;
            ReconJson.Write(extractionPath, extraction with { PackageFlags = extraction.PackageFlags + ", PKG_Tampered" });
            Assert.Throws<InvalidDataException>(() => service.ValidateDonor(firstRoot, edited, DonorMode.CompatibleShell));
            File.AppendAllText(Path.Combine(secondRoot, "replacement-contract.json"), " ");
            Assert.Throws<InvalidDataException>(() => service.ValidateDonor(secondRoot, edited, DonorMode.CompatibleShell));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void ExtractsValidatesAndAppliesExternalPropertyDocument()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var state = Path.Combine(temporary, ".ut4recon");
        try
        {
            const string internalPath = "UnrealTournament/Content/Maps/Glass/Glass.umap";
            var source = Path.Combine(root, "research", "pairs", "Glass", "cooked", "Glass.umap");
            var baseline = Path.Combine(state, "baseline", internalPath.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(baseline)!); File.Copy(source, baseline);
            var package = new PackageInspector().Inspect(baseline, internalPath); var sourceIdentity = Identity.File(source);
            var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", sourceIdentity, [package]); ReconJson.Write(Path.Combine(state, "reconstruction-ir.json"), ir);
            var pak = new PakInventory(source, "/", false, sourceIdentity, sourceIdentity, null, []);
            ReconJson.Write(Path.Combine(state, "input-manifest.json"), new InputManifest(1, new ToolIdentity("test", "1"), DateTimeOffset.UnixEpoch, ir.Profile, pak, []));
            var target = package.Objects.Single(x => x.ObjectPath.EndsWith("Floor.StaticMeshComponent0", StringComparison.Ordinal));
            var service = new AssetInterchangeService(); var bundle = Path.Combine(temporary, "property-bundle");
            var manifest = service.Extract(state, ir, target.Id, bundle); Assert.Equal(AssetReplacementKind.PropertySet, manifest.Kind);
            var donor = Path.Combine(bundle, "interchange", "properties", "modified-properties.json");
            var document = JsonSerializer.Deserialize<ExternalPropertyDocument>(File.ReadAllText(donor), ReconJson.Options)!;
            var properties = document.Properties.Select(property => property.PropertyPath == "RelativeLocation"
                ? property with { Value = JsonSerializer.SerializeToElement(new { x = 25.0, y = 0.0, z = 0.0 }, ReconJson.Options) }
                : property).ToArray();
            Assert.Contains(properties, x => x.PropertyPath == "RelativeLocation"); ReconJson.Write(donor, document with { Properties = properties });
            var report = service.ValidateDonor(bundle, donor, DonorMode.ExternalDocument);
            Assert.True(report.Passed); Assert.Equal([target.ExportIndex], report.ChangedExportIndices); Assert.Equal(package.Objects.Count - 1, report.UnrelatedExportsVerified);
            var changes = service.PropertyChanges(bundle, donor); Assert.Single(changes); Assert.Equal("RelativeLocation", changes[0].Before.PropertyPath);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static IReadOnlyList<string> TreeHashes(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => Path.GetRelativePath(root, x), StringComparer.Ordinal)
        .Select(x => Path.GetRelativePath(root, x).Replace('\\', '/') + ":" + Identity.Sha256File(x)).ToArray();

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
