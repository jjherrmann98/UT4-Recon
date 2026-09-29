using System.Reflection;
using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.Package;

public sealed class FreshBspGraftEngine
{
    public BspGraftReport Graft(string baselinePath, string donorPath, string outputPath, string internalPath)
    {
        baselinePath = Path.GetFullPath(baselinePath); donorPath = Path.GetFullPath(donorPath); outputPath = Path.GetFullPath(outputPath);
        var baseline = new UAsset(baselinePath, EngineVersion.VER_UE4_15); var donor = new UAsset(donorPath, EngineVersion.VER_UE4_15); var inspector = new PackageInspector();
        var before = inspector.Inspect(baselinePath, internalPath); EnsureReadable(before, "baseline"); var beforeCount = baseline.Exports.Count;
        var targetLevel = SingleClass(baseline, "Level"); var donorLevel = SingleClass(donor, "Level"); var targetLevelExport = (NormalExport)targetLevel.Export; var donorLevelExport = (NormalExport)donorLevel.Export;
        var targetModel = ObjectValue(targetLevelExport, "Model").Value.Index; var donorModel = ObjectValue(donorLevelExport, "Model").Value.Index;
        var targetComponents = ObjectArray(targetLevelExport, "ModelComponents").Select(x => x.Value.Index).ToArray(); var donorComponents = ObjectArray(donorLevelExport, "ModelComponents").Select(x => x.Value.Index).ToArray();
        if (targetModel <= 0 || donorModel <= 0 || targetComponents.Length == 0 || donorComponents.Length == 0) throw new InvalidDataException("Level BSP closure references are incomplete.");
        var targetBodies = targetComponents.Select(x => ObjectValue((NormalExport)baseline.Exports[x - 1], "ModelBodySetup").Value.Index).ToArray(); var donorBodies = donorComponents.Select(x => ObjectValue((NormalExport)donor.Exports[x - 1], "ModelBodySetup").Value.Index).ToArray();
        if (targetBodies.Any(x => x <= 0) || donorBodies.Any(x => x <= 0)) throw new InvalidDataException("A model component has no exported ModelBodySetup.");

        var extraCount = Math.Max(0, donorComponents.Length - targetComponents.Length); var addedBodies = Enumerable.Range(beforeCount + 1, extraCount).ToArray(); var addedComponents = Enumerable.Range(beforeCount + extraCount + 1, extraCount).ToArray();
        var mappedComponents = donorComponents.Select((_, i) => i < targetComponents.Length ? targetComponents[i] : addedComponents[i - targetComponents.Length]).ToArray();
        var mappedBodies = donorBodies.Select((_, i) => i < targetBodies.Length ? targetBodies[i] : addedBodies[i - targetBodies.Length]).ToArray();
        var remap = new Dictionary<int, int> { [donorLevel.Index] = targetLevel.Index, [donorModel] = targetModel };
        for (var i = 0; i < donorComponents.Length; i++) { remap[donorComponents[i]] = mappedComponents[i]; remap[donorBodies[i]] = mappedBodies[i]; }
        int MapIndex(int index)
        {
            if (index == 0) return 0; if (remap.TryGetValue(index, out var mapped)) return mapped;
            var path = ResolveIndex(donor, index); var candidates = Enumerable.Range(1, beforeCount).Concat(Enumerable.Range(1, baseline.Imports.Count).Select(x => -x)).Where(x => ResolveIndex(baseline, x) == path).ToArray();
            if (candidates.Length != 1) throw new InvalidDataException($"Donor reference {index} ({path}) resolves to {candidates.Length} baseline references."); return candidates[0];
        }

        var targetModelExport = (NormalExport)baseline.Exports[targetModel - 1]; var donorModelExport = (NormalExport)donor.Exports[donorModel - 1]; CopyContent(donorModelExport, targetModelExport, baseline, MapIndex); targetModelExport.Extras = RemapModelExtras(donorModelExport.Extras, MapIndex);
        var normalizedDonorBodies = new List<byte[]>();
        for (var i = 0; i < donorComponents.Length; i++)
        {
            var donorBody = (NormalExport)donor.Exports[donorBodies[i] - 1]; var bodyExtras = RemapBodySetupExtras(donorBody.Extras, donor, baseline); normalizedDonorBodies.Add(bodyExtras);
            NormalExport targetBody;
            if (i < targetBodies.Length) { targetBody = (NormalExport)baseline.Exports[targetBodies[i] - 1]; CopyContent(donorBody, targetBody, baseline, MapIndex); targetBody.Extras = bodyExtras; }
            else { targetBody = CloneExport(donorBody, baseline, MapIndex); targetBody.ObjectName = new FName(baseline, $"BodySetup_UT4Recon_{i}"); targetBody.OuterIndex = new FPackageIndex(mappedComponents[i]); targetBody.Extras = bodyExtras; baseline.Exports.Add(targetBody); }
        }
        for (var i = 0; i < donorComponents.Length; i++)
        {
            var donorComponent = (NormalExport)donor.Exports[donorComponents[i] - 1]; var extras = RemapModelComponentExtras(donorComponent.Extras, MapIndex); NormalExport target;
            if (i < targetComponents.Length) { target = (NormalExport)baseline.Exports[targetComponents[i] - 1]; CopyContent(donorComponent, target, baseline, MapIndex); target.Extras = extras; }
            else { target = CloneExport(donorComponent, baseline, MapIndex); target.ObjectName = new FName(baseline, $"ModelComponent_UT4Recon_{i}"); target.OuterIndex = new FPackageIndex(targetLevel.Index); target.Extras = extras; baseline.Exports.Add(target); }
        }
        var levelComponents = FindProperty<ArrayPropertyData>(targetLevelExport, "ModelComponents"); var componentTemplate = (ObjectPropertyData)levelComponents.Value[0]; levelComponents.Value = mappedComponents.Select((x, i) => { var item = (ObjectPropertyData)componentTemplate.Clone(); item.Name = new FName(baseline, i.ToString()); item.Value = new FPackageIndex(x); return (PropertyData)item; }).ToArray();
        ObjectValue(targetLevelExport, "Model").Value = new FPackageIndex(targetModel);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); baseline.Write(outputPath);

        var afterAsset = new UAsset(outputPath, EngineVersion.VER_UE4_15); var after = inspector.Inspect(outputPath, internalPath); EnsureReadable(after, "output"); var diagnostics = new List<string>();
        var levelAfter = (NormalExport)afterAsset.Exports[targetLevel.Index - 1]; var compiledLevelPreserved = Identity.Sha256Bytes(targetLevelExport.Extras) == Identity.Sha256Bytes(levelAfter.Extras); if (!compiledLevelPreserved) diagnostics.Add("Opaque Level data changed while replacing ModelComponents.");
        var protectedIndices = new HashSet<int>([targetLevel.Index, targetModel, .. targetComponents, .. targetBodies]); var unrelated = 0;
        foreach (var item in before.Objects.Where(x => !protectedIndices.Contains(x.ExportIndex))) { if (after.Objects.Single(x => x.ExportIndex == item.ExportIndex).PayloadHash != item.PayloadHash) diagnostics.Add("Unrelated export changed: " + item.ObjectPath); else unrelated++; }
        var outputGeometry = new NativeBspDecoder().Decode(outputPath, internalPath); var donorGeometry = new NativeBspDecoder().Decode(donorPath, internalPath); var geometryMatches = PrincipalMetrics(outputGeometry) == PrincipalMetrics(donorGeometry); if (!geometryMatches) diagnostics.Add("Transplanted principal BSP geometry does not match the fresh donor.");
        var collisionMatches = mappedBodies.Select((x, i) => Identity.Sha256Bytes(((NormalExport)afterAsset.Exports[x - 1]).Extras) == Identity.Sha256Bytes(normalizedDonorBodies[i])).All(x => x); if (!collisionMatches) diagnostics.Add("Transplanted BodySetup collision closure does not match the normalized fresh donor.");
        var changed = before.Objects.Where(x => protectedIndices.Contains(x.ExportIndex)).Where(x => after.Objects.Single(y => y.ExportIndex == x.ExportIndex).PayloadHash != x.PayloadHash).Select(x => x.ExportIndex).ToArray(); var added = Enumerable.Range(beforeCount + 1, afterAsset.Exports.Count - beforeCount).ToArray(); var closure = new[] { targetModel, targetLevel.Index }.Concat(mappedBodies).Concat(mappedComponents).Distinct().Order().ToArray();
        var passed = compiledLevelPreserved && geometryMatches && collisionMatches && diagnostics.Count == 0;
        return new BspGraftReport(GeometryFormatVersions.BspGraftReport, GeometryFormatVersions.BspProfile, Identity.Sha256File(baselinePath), Identity.Sha256File(donorPath), Identity.Sha256File(outputPath), closure, changed, added, 1, compiledLevelPreserved, unrelated, geometryMatches, collisionMatches, diagnostics, passed);
    }

    private static NormalExport CloneExport(NormalExport source, UAsset target, Func<int, int> map) { var clone = (NormalExport)source.Clone(); RemapHeader(clone, target, map); clone.Data = source.Data.Select(x => CloneProperty(x, target, map)).ToList(); return clone; }
    private static void CopyContent(NormalExport source, NormalExport target, UAsset asset, Func<int, int> map) { target.Data = source.Data.Select(x => CloneProperty(x, asset, map)).ToList(); target.ObjectGuid = source.ObjectGuid; target.PackageFlags = source.PackageFlags; }
    private static void RemapHeader(Export export, UAsset target, Func<int, int> map) { export.ObjectName = new FName(target, export.ObjectName.ToString()); export.OuterIndex = new FPackageIndex(map(export.OuterIndex.Index)); export.ClassIndex = new FPackageIndex(map(export.ClassIndex.Index)); export.SuperIndex = new FPackageIndex(map(export.SuperIndex.Index)); export.TemplateIndex = new FPackageIndex(map(export.TemplateIndex.Index)); RemapList(export.SerializationBeforeSerializationDependencies, map); RemapList(export.CreateBeforeSerializationDependencies, map); RemapList(export.SerializationBeforeCreateDependencies, map); RemapList(export.CreateBeforeCreateDependencies, map); }
    private static void RemapList(IList<FPackageIndex> values, Func<int, int> map) { for (var i = 0; i < values.Count; i++) values[i] = new FPackageIndex(map(values[i].Index)); }
    private static PropertyData CloneProperty(PropertyData source, UAsset target, Func<int, int> map)
    {
        var clone = (PropertyData)source.Clone(); RebindNames(clone, target);
        void Visit(PropertyData item) { if (item is ObjectPropertyData reference) reference.Value = new FPackageIndex(map(reference.Value.Index)); else if (item is StructPropertyData structure) foreach (var child in structure.Value) Visit(child); else if (item is ArrayPropertyData array) foreach (var child in array.Value) Visit(child); }
        Visit(clone); return clone;
    }
    private static void RebindNames(PropertyData property, UAsset target)
    {
        foreach (var field in property.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public).Where(x => x.FieldType == typeof(FName))) if (field.GetValue(property) is FName value) field.SetValue(property, new FName(target, value.ToString()));
        foreach (var info in property.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.PropertyType == typeof(FName) && x.CanRead && x.CanWrite)) if (info.GetValue(property) is FName value) info.SetValue(property, new FName(target, value.ToString()));
        if (property is StructPropertyData structure) foreach (var child in structure.Value) RebindNames(child, target); else if (property is ArrayPropertyData array) foreach (var child in array.Value) RebindNames(child, target);
    }
    private static byte[] RemapModelExtras(byte[] input, Func<int, int> map)
    {
        var data = (byte[])input.Clone(); using var stream = new MemoryStream(data, true); using var reader = new BinaryReader(stream); stream.Position = 30; SkipBulk(reader, 12); SkipBulk(reader, 12); SkipBulk(reader, 64); var count = ReadCount(reader);
        for (var i = 0; i < count; i++) { var start = stream.Position; WriteInt(data, start, map(reader.ReadInt32())); stream.Position = start + 28; WriteInt(data, stream.Position, map(reader.ReadInt32())); stream.Position = start + 56; } return data;
    }
    private static byte[] RemapModelComponentExtras(byte[] input, Func<int, int> map)
    {
        var data = (byte[])input.Clone(); using var stream = new MemoryStream(data, true); using var reader = new BinaryReader(stream); WriteInt(data, 0, map(reader.ReadInt32())); var elements = ReadCount(reader);
        for (var i = 0; i < elements; i++) { stream.Position += 16; var at = stream.Position; WriteInt(data, at, map(reader.ReadInt32())); at = stream.Position; WriteInt(data, at, map(reader.ReadInt32())); var nodes = ReadCount(reader); stream.Position += nodes * 2L; }
        stream.Position += 4; var componentNodes = ReadCount(reader); stream.Position += componentNodes * 2L; if (stream.Position != stream.Length) throw new InvalidDataException("Unsupported ModelComponent native layout."); return data;
    }
    private static byte[] RemapBodySetupExtras(byte[] input, UAsset donor, UAsset baseline)
    {
        if (input.Length < 36 || BitConverter.ToInt32(input, 16) != 1 || BitConverter.ToInt32(input, 20) != 1) throw new InvalidDataException("Unsupported cooked BodySetup native layout."); var data = (byte[])input.Clone(); var donorName = donor.GetNameReference(BitConverter.ToInt32(data, 28)).ToString(); var names = baseline.GetNameMapIndexList(); var index = Enumerable.Range(0, names.Count).SingleOrDefault(i => names[i].ToString() == donorName, -1); if (index < 0) { baseline.AddNameReference(new FString(donorName)); names = baseline.GetNameMapIndexList(); index = Enumerable.Range(0, names.Count).Single(i => names[i].ToString() == donorName); } WriteInt(data, 28, index); return data;
    }
    private static void SkipBulk(BinaryReader reader, int stride) { if (reader.ReadInt32() != stride) throw new InvalidDataException("Unsupported UModel bulk stride."); var count = ReadCount(reader); reader.BaseStream.Position += (long)stride * count; }
    private static int ReadCount(BinaryReader reader) { var count = reader.ReadInt32(); if (count < 0 || count > 1_000_000) throw new InvalidDataException("Invalid native array count."); return count; }
    private static void WriteInt(byte[] data, long offset, int value) => BitConverter.GetBytes(value).CopyTo(data, checked((int)offset));
    private static ObjectPropertyData ObjectValue(NormalExport export, string name) => FindProperty<ObjectPropertyData>(export, name);
    private static ObjectPropertyData[] ObjectArray(NormalExport export, string name) => FindProperty<ArrayPropertyData>(export, name).Value.Cast<ObjectPropertyData>().ToArray();
    private static T FindProperty<T>(NormalExport export, string name) where T : PropertyData => export.Data.OfType<T>().Single(x => x.Name.ToString() == name);
    private static (int Index, Export Export) SingleClass(UAsset asset, string name) { var items = asset.Exports.Select((x, i) => (Index: i + 1, Export: x)).Where(x => x.Export.GetExportClassType().ToString() == name).ToArray(); if (items.Length != 1) throw new InvalidDataException($"Expected one {name} export, found {items.Length}."); return items[0]; }
    private static string ResolveIndex(UAsset asset, int index, HashSet<int>? seen = null) { if (index == 0) return ""; seen ??= []; if (!seen.Add(index)) return "<cycle>"; var name = index > 0 ? asset.Exports[index - 1].ObjectName.ToString() : asset.Imports[-index - 1].ObjectName.ToString(); var outer = index > 0 ? asset.Exports[index - 1].OuterIndex.Index : asset.Imports[-index - 1].OuterIndex.Index; var parent = ResolveIndex(asset, outer, seen); return string.IsNullOrEmpty(parent) ? name : parent + "." + name; }
    private static BspMetrics PrincipalMetrics(BspMapIr map) => map.Models.OrderByDescending(x => x.Polygons.Count).First().Metrics;
    private static void EnsureReadable(PackageInventory inventory, string role) { if (inventory.Diagnostics.Count != 0) throw new InvalidDataException($"The {role} package failed inspection: {string.Join("; ", inventory.Diagnostics)}"); }
}
