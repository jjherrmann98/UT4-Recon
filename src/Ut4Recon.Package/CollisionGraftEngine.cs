using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.Package;

public sealed class CollisionGraftEngine
{
    public CollisionGraftReport Graft(string baselinePath, string donorPath, string outputPath, string internalPath)
    {
        baselinePath = Path.GetFullPath(baselinePath); donorPath = Path.GetFullPath(donorPath); outputPath = Path.GetFullPath(outputPath);
        var baselineHash = Identity.Sha256File(baselinePath); var donorHash = Identity.Sha256File(donorPath);
        var baseline = new UAsset(baselinePath, EngineVersion.VER_UE4_15); var donor = new UAsset(donorPath, EngineVersion.VER_UE4_15);
        VerifyCompatibleShell(baseline, donor);
        var baselineInventory = new PackageInspector().Inspect(baselinePath, internalPath); var donorInventory = new PackageInspector().Inspect(donorPath, internalPath);
        EnsureReadable(baselineInventory, "baseline"); EnsureReadable(donorInventory, "donor");
        var staticMesh = SingleClass(baseline, "StaticMesh"); var body = SingleClass(baseline, "BodySetup"); var donorBody = (NormalExport)donor.Exports[body.Index - 1];
        var beforeRender = baselineInventory.Objects.Single(x => x.ExportIndex == staticMesh.Index).PayloadHash;
        var beforeBody = baselineInventory.Objects.Single(x => x.ExportIndex == body.Index).PayloadHash;
        var donorBodyHash = donorInventory.Objects.Single(x => x.ExportIndex == body.Index).PayloadHash;
        CopyNormalExport(donorBody, (NormalExport)baseline.Exports[body.Index - 1]);

        var nav = ClassMatches(baseline, "NavCollision"); var donorNav = ClassMatches(donor, "NavCollision");
        if (nav.Count != donorNav.Count) throw new InvalidDataException("Baseline and donor NavCollision closure differs.");
        for (var i = 0; i < nav.Count; i++)
        {
            if (nav[i].Index != donorNav[i].Index) throw new InvalidDataException("NavCollision export indexes differ.");
            CopyNormalExport((NormalExport)donor.Exports[donorNav[i].Index - 1], (NormalExport)baseline.Exports[nav[i].Index - 1]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); baseline.Write(outputPath);
        var afterInventory = new PackageInspector().Inspect(outputPath, internalPath); EnsureReadable(afterInventory, "output");
        var afterRender = afterInventory.Objects.Single(x => x.ExportIndex == staticMesh.Index).PayloadHash;
        var afterBody = afterInventory.Objects.Single(x => x.ExportIndex == body.Index).PayloadHash;
        var diagnostics = new List<string>();
        if (beforeRender != afterRender) diagnostics.Add("StaticMesh render export changed during collision graft.");
        if (donorBodyHash != afterBody) diagnostics.Add("Written BodySetup export does not match the donor payload.");
        var navigationMatches = nav.All(x => donorInventory.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash == afterInventory.Objects.Single(y => y.ExportIndex == x.Index).PayloadHash);
        if (!navigationMatches) diagnostics.Add("Written NavCollision closure does not match the donor payload.");
        foreach (var item in baselineInventory.Objects.Where(x => x.ExportIndex != body.Index && nav.All(n => n.Index != x.ExportIndex) && x.ExportIndex != staticMesh.Index))
        {
            var after = afterInventory.Objects.Single(x => x.ExportIndex == item.ExportIndex);
            if (item.PayloadHash != after.PayloadHash) diagnostics.Add("Unrelated export changed: " + item.ObjectPath);
        }
        var renderPreserved = beforeRender == afterRender; var collisionMatches = donorBodyHash == afterBody;
        return new CollisionGraftReport(GeometryFormatVersions.CollisionGraftReport, GeometryFormatVersions.Profile, baselineHash, donorHash, Identity.Sha256File(outputPath),
            beforeRender, afterRender, beforeBody, donorBodyHash, afterBody, renderPreserved, collisionMatches, nav.Count, navigationMatches, diagnostics, renderPreserved && collisionMatches && navigationMatches && diagnostics.Count == 0);
    }

    private static void CopyNormalExport(NormalExport source, NormalExport target)
    {
        target.Data = source.Data.Select(x => (PropertyData)x.Clone()).ToList(); target.Extras = (byte[])source.Extras.Clone();
        target.ObjectGuid = source.ObjectGuid; target.PackageFlags = source.PackageFlags;
    }

    private static void VerifyCompatibleShell(UAsset baseline, UAsset donor)
    {
        using var a = JsonDocument.Parse(baseline.SerializeJson()); using var b = JsonDocument.Parse(donor.SerializeJson());
        foreach (var section in new[] { "NameMap", "Imports" }) if (a.RootElement.GetProperty(section).GetRawText() != b.RootElement.GetProperty(section).GetRawText())
            throw new InvalidDataException($"Baseline and donor {section} differ; reference remapping is not implemented for collision graft v1.");
        if (baseline.Exports.Count != donor.Exports.Count) throw new InvalidDataException("Baseline and donor export counts differ.");
        for (var i = 0; i < baseline.Exports.Count; i++)
        {
            var x = baseline.Exports[i]; var y = donor.Exports[i];
            if (x.ObjectName.ToString() != y.ObjectName.ToString() || x.ClassIndex.Index != y.ClassIndex.Index || x.OuterIndex.Index != y.OuterIndex.Index)
                throw new InvalidDataException($"Baseline and donor export identity differs at index {i + 1}.");
        }
    }

    private static (int Index, Export Export) SingleClass(UAsset asset, string className)
    {
        var matches = ClassMatches(asset, className); if (matches.Count != 1) throw new InvalidDataException($"Expected one {className} export, found {matches.Count}."); return matches[0];
    }
    private static List<(int Index, Export Export)> ClassMatches(UAsset asset, string className) => asset.Exports.Select((x, i) => (Index: i + 1, Export: x))
        .Where(x => x.Export.GetExportClassType().ToString().Equals(className, StringComparison.Ordinal)).ToList();
    private static void EnsureReadable(PackageInventory inventory, string role) { if (inventory.Diagnostics.Count != 0) throw new InvalidDataException($"The {role} package failed inspection: {string.Join("; ", inventory.Diagnostics)}"); }
}
