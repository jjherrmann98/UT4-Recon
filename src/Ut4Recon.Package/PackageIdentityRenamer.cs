using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace Ut4Recon.Package;

public sealed record PackageRenameResult(int ReplacedNameEntries, int ExportCount);

public sealed class PackageIdentityRenamer
{
    public PackageRenameResult Rename(string inputPath, string outputPath, IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0 || replacements.Any(x => string.IsNullOrEmpty(x.Key) || string.IsNullOrEmpty(x.Value) || x.Key == x.Value))
            throw new ArgumentException("Package identity replacements must be non-empty changes.", nameof(replacements));

        var asset = new UAsset(inputPath, EngineVersion.VER_UE4_15);
        var changed = 0;
        for (var index = 0; index < asset.GetNameMapIndexList().Count; index++)
        {
            var before = asset.GetNameReference(index).ToString();
            if (!replacements.TryGetValue(before, out var after)) continue;
            asset.SetNameReference(index, new FString(after));
            changed++;
        }
        if (changed == 0) throw new InvalidDataException("No exact package identity name entries matched the requested rename.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        asset.Write(outputPath);

        var written = new UAsset(outputPath, EngineVersion.VER_UE4_15);
        var names = Enumerable.Range(0, written.GetNameMapIndexList().Count).Select(i => written.GetNameReference(i).ToString()).ToArray();
        foreach (var replacement in replacements)
        {
            if (names.Contains(replacement.Key, StringComparer.Ordinal))
                throw new InvalidDataException($"Old package identity remained after write: {replacement.Key}");
        }
        if (written.Exports.Count != asset.Exports.Count) throw new InvalidDataException("Package identity rename changed the export count.");
        return new PackageRenameResult(changed, written.Exports.Count);
    }
}
