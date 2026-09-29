using System.Text.Json;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class CookedMapFixtureTests
{
    public static TheoryData<string, string, string, int, int, int> Maps => new()
    {
        { "research/pairs/Glass/cooked/Glass.umap", "UnrealTournament/Content/Maps/Glass/Glass.umap", "6645936b3f315b36b43c89fa885c879eaf625667e08371a70675da8013cacbe3", 168, 68, 49 },
        { "research/pairs/Example_Map/cooked/Example_Map.umap", "UnrealTournament/Content/Maps/Example_Map/Example_Map.umap", "9f011d93b17f3d78df179f64c9303d24346a8f6873df60015be9b8a9314e4022", 1252, 492, 2268 }
    };

    [Theory]
    [MemberData(nameof(Maps))]
    public void CookedMapInventoryMatchesGoldenEvidence(string fixture, string internalPath, string sha256, int names, int imports, int exports)
    {
        var path = Path.Combine(RepositoryRoot(), fixture.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(sha256, Identity.Sha256File(path));

        var inspector = new PackageInspector();
        var first = inspector.Inspect(path, internalPath);
        var second = inspector.Inspect(path, internalPath);

        Assert.Empty(first.Diagnostics);
        Assert.True(first.ContainsMap);
        Assert.True(first.IsFilterEditorOnly);
        Assert.Equal("VER_UE4_64BIT_EXPORTMAP_SERIALSIZES", first.ObjectVersion);
        Assert.Equal((names, imports, exports), (first.NameCount, first.ImportCount, first.ExportCount));
        Assert.NotEmpty(first.CustomVersions);
        Assert.Equal(exports, first.Objects.Count);
        Assert.Equal(exports, first.Objects.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(first.Objects, item =>
        {
            Assert.Equal(64, item.PayloadHash.Length);
            Assert.False(string.IsNullOrWhiteSpace(item.SupportReason));
        });
        Assert.True(first.Objects.Sum(x => (long)x.OpaqueNativeBytes) > 0);
        Assert.Equal(JsonSerializer.Serialize(first, ReconJson.Options), JsonSerializer.Serialize(second, ReconJson.Options));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
