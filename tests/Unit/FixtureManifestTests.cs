using System.Text.Json;
using Ut4Recon.Core;

namespace Ut4Recon.UnitTests;

public class FixtureManifestTests
{
    [Fact]
    public void EveryPinnedFixtureMatchesItsRecordedBytes()
    {
        var root = RepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "fixtures", "manifest.json")));
        foreach (var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var relativePath = fixture.GetProperty("path").GetString()!;
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(path);
            Assert.True(info.Exists, $"Missing fixture: {relativePath}");
            Assert.Equal(fixture.GetProperty("size").GetInt64(), info.Length);
            Assert.Equal(fixture.GetProperty("sha256").GetString(), Identity.Sha256File(path));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
