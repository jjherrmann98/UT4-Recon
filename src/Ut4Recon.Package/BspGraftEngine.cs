using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.Package;

public sealed class BspGraftEngine
{
    public BspGraftReport Graft(string baselinePath, string donorPath, string outputPath, string internalPath)
    {
        baselinePath = Path.GetFullPath(baselinePath); donorPath = Path.GetFullPath(donorPath); outputPath = Path.GetFullPath(outputPath);
        var baseline = new UAsset(baselinePath, EngineVersion.VER_UE4_15); var donor = new UAsset(donorPath, EngineVersion.VER_UE4_15); VerifyCompatibleShell(baseline, donor);
        var inspector = new PackageInspector(); var before = inspector.Inspect(baselinePath, internalPath); var donorInventory = inspector.Inspect(donorPath, internalPath); EnsureReadable(before, "baseline"); EnsureReadable(donorInventory, "donor");
        var exports = baseline.Exports.Select((x, i) => (Export: x, Index: i + 1, Class: x.GetExportClassType().ToString())).ToArray();
        var modelComponents = exports.Where(x => x.Class == "ModelComponent").Select(x => x.Index).ToHashSet();
        var closure = exports.Where(x => x.Class == "Model" || x.Class == "ModelComponent" || (x.Class == "BodySetup" && x.Export.OuterIndex.Index > 0 && modelComponents.Contains(x.Export.OuterIndex.Index))).ToArray();
        if (!closure.Any(x => x.Class == "Model")) throw new InvalidDataException("Baseline contains no UModel export.");
        var changed = closure.Where(x => before.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash != donorInventory.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash).Select(x => x.Index).ToArray();
        if (changed.Length == 0) throw new InvalidDataException("BSP donor contains no changed model-closure exports.");
        foreach (var item in closure) CopyNormalExport((NormalExport)donor.Exports[item.Index - 1], (NormalExport)baseline.Exports[item.Index - 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); baseline.Write(outputPath);

        var after = inspector.Inspect(outputPath, internalPath); EnsureReadable(after, "output"); var diagnostics = new List<string>(); var closureSet = closure.Select(x => x.Index).ToHashSet();
        var levels = before.Objects.Where(x => x.ClassPath?.EndsWith(".Level", StringComparison.Ordinal) == true).ToArray();
        var levelsPreserved = levels.All(x => after.Objects.Single(y => y.ExportIndex == x.ExportIndex).PayloadHash == x.PayloadHash); if (!levelsPreserved) diagnostics.Add("A Level export changed during BSP graft; actor references or compiled level behavior may have changed.");
        var unrelatedVerified = 0;
        foreach (var item in before.Objects.Where(x => !closureSet.Contains(x.ExportIndex) && levels.All(y => y.ExportIndex != x.ExportIndex)))
        {
            if (after.Objects.Single(x => x.ExportIndex == item.ExportIndex).PayloadHash != item.PayloadHash) diagnostics.Add("Unrelated export changed: " + item.ObjectPath); else unrelatedVerified++;
        }
        var closureMatches = closure.All(x => after.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash == donorInventory.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash); if (!closureMatches) diagnostics.Add("Written BSP model/collision closure does not match the donor.");
        var donorGeometry = new NativeBspDecoder().Decode(donorPath, internalPath); var outputGeometry = new NativeBspDecoder().Decode(outputPath, internalPath); var geometryMatches = ExactGeometry(donorGeometry, outputGeometry); if (!geometryMatches) diagnostics.Add("Decoded output BSP geometry does not match the donor.");
        var passed = levelsPreserved && closureMatches && geometryMatches && diagnostics.Count == 0;
        return new BspGraftReport(GeometryFormatVersions.BspGraftReport, GeometryFormatVersions.BspProfile, Identity.Sha256File(baselinePath), Identity.Sha256File(donorPath), Identity.Sha256File(outputPath), closure.Select(x => x.Index).ToArray(), changed, [], levels.Length, levelsPreserved, unrelatedVerified, geometryMatches, closureMatches, diagnostics, passed);
    }

    private static bool ExactGeometry(BspMapIr a, BspMapIr b)
    {
        if (a.Models.Count != b.Models.Count) return false;
        return a.Models.Zip(b.Models).All(x => x.First.ObjectPath == x.Second.ObjectPath && x.First.Polygons.Count == x.Second.Polygons.Count && x.First.Metrics == x.Second.Metrics && x.First.RemainingNativeHash == x.Second.RemainingNativeHash);
    }
    private static void CopyNormalExport(NormalExport source, NormalExport target) { target.Data = source.Data.Select(x => (PropertyData)x.Clone()).ToList(); target.Extras = (byte[])source.Extras.Clone(); target.ObjectGuid = source.ObjectGuid; target.PackageFlags = source.PackageFlags; }
    private static void VerifyCompatibleShell(UAsset baseline, UAsset donor)
    {
        using var a = JsonDocument.Parse(baseline.SerializeJson()); using var b = JsonDocument.Parse(donor.SerializeJson());
        foreach (var section in new[] { "NameMap", "Imports" }) if (a.RootElement.GetProperty(section).GetRawText() != b.RootElement.GetProperty(section).GetRawText()) throw new InvalidDataException($"Baseline and donor {section} differ; BSP graft v1 requires a donor cooked from the compatible original package shell.");
        if (baseline.Exports.Count != donor.Exports.Count) throw new InvalidDataException("Baseline and donor export counts differ.");
        for (var i = 0; i < baseline.Exports.Count; i++) { var x = baseline.Exports[i]; var y = donor.Exports[i]; if (x.ObjectName.ToString() != y.ObjectName.ToString() || x.ClassIndex.Index != y.ClassIndex.Index || x.OuterIndex.Index != y.OuterIndex.Index) throw new InvalidDataException($"Baseline and donor export identity differs at index {i + 1}."); }
    }
    private static void EnsureReadable(PackageInventory inventory, string role) { if (inventory.Diagnostics.Count != 0) throw new InvalidDataException($"The {role} package failed inspection: {string.Join("; ", inventory.Diagnostics)}"); }
}
