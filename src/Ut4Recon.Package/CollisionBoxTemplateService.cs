using System.Reflection;
using System.Text;
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

public sealed class CollisionBoxTemplateService
{
    public CollisionBoxTemplateManifest Certify(string packagePath, string internalPath, string profile, int editorChangelist = 3525360, int runtimeApiVersion = 3525109)
    {
        packagePath = Path.GetFullPath(packagePath);
        var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15);
        var inventory = new PackageInspector().Inspect(packagePath, internalPath);
        if (inventory.Diagnostics.Count != 0) throw new InvalidDataException("Template package inspection failed: " + string.Join("; ", inventory.Diagnostics));
        var level = SingleClass(asset, "Level");
        var actors = level.Export is LevelExport levelExport
            ? levelExport.Actors.Where(x => x.IsExport()).Select(x => x.Index).Where(x => ClassPath(asset, x) == "/Script/Engine.BlockingVolume").ToArray()
            : [];
        if (actors.Length != 1) throw new InvalidDataException($"Template must contain exactly one BlockingVolume; found {actors.Length}.");
        var closure = Closure(asset, actors[0]);
        ValidateClosure(asset, closure);
        var modelIndex = closure.Single(x => ClassPath(asset, x) == "/Script/Engine.Model");
        var bodyIndex = closure.Single(x => ClassPath(asset, x) == "/Script/Engine.BodySetup");
        var geometry = new NativeBspDecoder().Decode(packagePath, internalPath);
        if (geometry.Diagnostics.Count != 0) throw new InvalidDataException("Template geometry decoding failed: " + string.Join("; ", geometry.Diagnostics));
        var model = geometry.Models.Single(x => x.ExportIndex == modelIndex);
        ValidateAxisAlignedBox(model);
        var dimensions = new CollisionBoxDimensions(
            model.Metrics.BoundsMaximum.X - model.Metrics.BoundsMinimum.X,
            model.Metrics.BoundsMaximum.Y - model.Metrics.BoundsMinimum.Y,
            model.Metrics.BoundsMaximum.Z - model.Metrics.BoundsMinimum.Z);
        var objects = closure.Select(index => inventory.Objects.Single(x => x.ExportIndex == index)).Select(x =>
            new AssetClosureObject(Role(asset, x.ExportIndex), x.Id, x.ObjectPath, x.ClassPath, x.ExportIndex, x.PayloadHash, x.OpaqueNativeBytes)).ToArray();
        var geometryFingerprint = GeometryFingerprint(model);
        var collisionFingerprint = CollisionFingerprint(asset.Exports[bodyIndex - 1].Extras);
        return new CollisionBoxTemplateManifest(FormatVersions.CollisionBoxTemplate, profile, editorChangelist, runtimeApiVersion,
            Identity.File(packagePath, "CollisionBoxTemplate.umap"), internalPath.Replace('\\', '/'), actors[0], inventory.Objects.Single(x => x.ExportIndex == actors[0]).ObjectPath,
            objects, dimensions, geometryFingerprint, collisionFingerprint, inventory.ImportPackages.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["translation", "rotation", "relative-scale-3d"]);
    }

    public CollisionBoxTemplateManifest ValidateBundle(string manifestPath, string expectedProfile)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var manifest = JsonSerializer.Deserialize<CollisionBoxTemplateManifest>(File.ReadAllText(manifestPath), ReconJson.Options) ?? throw new InvalidDataException(manifestPath);
        if (manifest.SchemaVersion != FormatVersions.CollisionBoxTemplate || manifest.Profile != expectedProfile) throw new InvalidDataException("Collision box template schema or support profile does not match the project.");
        if (manifest.EditorChangelist != 3525360 || manifest.RuntimeApiVersion != 3525109) throw new InvalidDataException("Collision box template is not certified for editor CL 3525360 / runtime API 3525109.");
        var packagePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Package.Path));
        var expected = manifest.Package with { Path = packagePath }; VerifyFile(expected);
        var actual = Certify(packagePath, manifest.InternalPath, manifest.Profile, manifest.EditorChangelist, manifest.RuntimeApiVersion);
        if (actual.ActorExportIndex != manifest.ActorExportIndex || actual.ActorObjectPath != manifest.ActorObjectPath || actual.GeometryFingerprint != manifest.GeometryFingerprint ||
            actual.CollisionFingerprint != manifest.CollisionFingerprint || actual.Closure.Select(x => x.PayloadHash).SequenceEqual(manifest.Closure.Select(x => x.PayloadHash), StringComparer.Ordinal) == false)
            throw new InvalidDataException("Collision box template content does not match its certification manifest.");
        if (!actual.RequiredImportPackages.SequenceEqual(manifest.RequiredImportPackages, StringComparer.Ordinal))
            throw new InvalidDataException("Collision box template import dependencies do not match its certification manifest.");
        return manifest;
    }

    public CustomCollisionDonorManifest CertifyCustom(string packagePath, string internalPath, string profile, int editorChangelist = 3525360, int runtimeApiVersion = 3525109)
    {
        packagePath = Path.GetFullPath(packagePath); var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15);
        var inventory = new PackageInspector().Inspect(packagePath, internalPath);
        if (inventory.Diagnostics.Count != 0) throw new InvalidDataException("Custom collision donor inspection failed: " + string.Join("; ", inventory.Diagnostics));
        var level = SingleClass(asset, "Level");
        var actors = level.Export is LevelExport levelExport
            ? levelExport.Actors.Where(x => x.IsExport()).Select(x => x.Index).Where(x => ClassPath(asset, x) == "/Script/Engine.BlockingVolume").ToArray() : [];
        if (actors.Length != 1) throw new InvalidDataException($"Custom collision donor must contain exactly one BlockingVolume; found {actors.Length}.");
        var otherRuntimeActors = ((LevelExport)level.Export).Actors.Where(x => x.IsExport()).Select(x => x.Index)
            .Where(x => x != actors[0] && !IsStructuralMapActor(ClassPath(asset, x))).ToArray();
        if (otherRuntimeActors.Length != 0) throw new InvalidDataException("Custom collision donor contains unsupported runtime actor(s): " + string.Join(", ", otherRuntimeActors.Select(x => ClassPath(asset, x))));
        var closure = Closure(asset, actors[0]); ValidateClosure(asset, closure);
        var modelIndex = closure.Single(x => ClassPath(asset, x) == "/Script/Engine.Model"); var bodyIndex = closure.Single(x => ClassPath(asset, x) == "/Script/Engine.BodySetup");
        var geometry = new NativeBspDecoder().Decode(packagePath, internalPath);
        if (geometry.Diagnostics.Count != 0) throw new InvalidDataException("Custom collision geometry decoding failed: " + string.Join("; ", geometry.Diagnostics));
        var model = geometry.Models.Single(x => x.ExportIndex == modelIndex); var vertexCount = model.Polygons.SelectMany(x => x.Positions).Select(x => $"{x.X:R},{x.Y:R},{x.Z:R}").Distinct(StringComparer.Ordinal).Count();
        if (model.Polygons.Count == 0 || vertexCount < 4 || model.Polygons.Any(x => x.Positions.Count < 3)) throw new InvalidDataException("Custom collision donor has empty or degenerate brush geometry.");
        var extent = new[] { model.Metrics.BoundsMaximum.X - model.Metrics.BoundsMinimum.X, model.Metrics.BoundsMaximum.Y - model.Metrics.BoundsMinimum.Y, model.Metrics.BoundsMaximum.Z - model.Metrics.BoundsMinimum.Z };
        if (extent.Any(x => x <= 0)) throw new InvalidDataException("Custom collision donor bounds must have non-zero extent on every axis.");
        var objects = closure.Select(index => inventory.Objects.Single(x => x.ExportIndex == index)).Select(x =>
            new AssetClosureObject(Role(asset, x.ExportIndex), x.Id, x.ObjectPath, x.ClassPath, x.ExportIndex, x.PayloadHash, x.OpaqueNativeBytes)).ToArray();
        return new CustomCollisionDonorManifest(FormatVersions.CustomCollisionDonor, profile, editorChangelist, runtimeApiVersion,
            Identity.File(packagePath, "CustomCollisionDonor.umap"), internalPath.Replace('\\', '/'), actors[0], inventory.Objects.Single(x => x.ExportIndex == actors[0]).ObjectPath,
            objects, model.Polygons.Count, vertexCount,
            new(model.Metrics.BoundsMinimum.X, model.Metrics.BoundsMinimum.Y, model.Metrics.BoundsMinimum.Z), new(model.Metrics.BoundsMaximum.X, model.Metrics.BoundsMaximum.Y, model.Metrics.BoundsMaximum.Z),
            GeometryFingerprint(model), CollisionFingerprint(asset.Exports[bodyIndex - 1].Extras), inventory.ImportPackages.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            ["translation", "rotation", "relative-scale-3d"], ["The donor owns one four-export BlockingVolume closure only.", "Brush construction history and editor metadata are not preserved."]);
    }

    public CustomCollisionDonorManifest ValidateCustomBundle(string manifestPath, string expectedProfile)
    {
        manifestPath = Path.GetFullPath(manifestPath); var manifest = JsonSerializer.Deserialize<CustomCollisionDonorManifest>(File.ReadAllText(manifestPath), ReconJson.Options) ?? throw new InvalidDataException(manifestPath);
        if (manifest.SchemaVersion != FormatVersions.CustomCollisionDonor || manifest.Profile != expectedProfile || manifest.EditorChangelist != 3525360 || manifest.RuntimeApiVersion != 3525109)
            throw new InvalidDataException("Custom collision donor profile or editor/runtime certification is unsupported.");
        var package = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifestPath)!, manifest.Package.Path)); VerifyFile(manifest.Package with { Path = package });
        var actual = CertifyCustom(package, manifest.InternalPath, manifest.Profile, manifest.EditorChangelist, manifest.RuntimeApiVersion);
        if (actual.ActorExportIndex != manifest.ActorExportIndex || actual.ActorObjectPath != manifest.ActorObjectPath || actual.PolygonCount != manifest.PolygonCount || actual.VertexCount != manifest.VertexCount ||
            actual.GeometryFingerprint != manifest.GeometryFingerprint || actual.CollisionFingerprint != manifest.CollisionFingerprint ||
            !actual.RequiredImportPackages.SequenceEqual(manifest.RequiredImportPackages, StringComparer.Ordinal) ||
            !actual.Closure.Select(x => x.PayloadHash).SequenceEqual(manifest.Closure.Select(x => x.PayloadHash), StringComparer.Ordinal))
            throw new InvalidDataException("Custom collision donor content does not match its certification manifest.");
        return manifest;
    }

    public ActorCloneResult AddToMap(string inputPath, string outputPath, string templateManifestPath, string expectedProfile, string newActorName,
        JsonElement location, JsonElement rotation, JsonElement? scale = null)
    {
        var manifest = ValidateBundle(templateManifestPath, expectedProfile);
        var templatePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(templateManifestPath))!, manifest.Package.Path));
        return CloneFromTemplate(inputPath, outputPath, templatePath, manifest.ActorExportIndex, newActorName, location, rotation, scale);
    }

    public ActorCloneResult AddCustomToMap(string inputPath, string outputPath, string donorManifestPath, string expectedProfile, string newActorName,
        JsonElement location, JsonElement rotation, JsonElement? scale = null)
    {
        var manifest = ValidateCustomBundle(donorManifestPath, expectedProfile);
        var donorPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(donorManifestPath))!, manifest.Package.Path));
        return CloneFromTemplate(inputPath, outputPath, donorPath, manifest.ActorExportIndex, newActorName, location, rotation, scale);
    }

    private static ActorCloneResult CloneFromTemplate(string inputPath, string outputPath, string templatePath, int actorIndex, string newActorName,
        JsonElement location, JsonElement rotation, JsonElement? scale)
    {
        if (string.IsNullOrWhiteSpace(newActorName) || newActorName.Any(x => !char.IsLetterOrDigit(x) && x != '_')) throw new InvalidDataException("New actor name must contain only letters, digits, and underscores.");
        var target = new UAsset(inputPath, EngineVersion.VER_UE4_15); var donor = new UAsset(templatePath, EngineVersion.VER_UE4_15);
        var targetLevel = SingleClass(target, "Level"); var donorLevel = SingleClass(donor, "Level"); var targetWorld = SingleClass(target, "World"); var donorWorld = SingleClass(donor, "World");
        var targetLevelExport = (LevelExport)targetLevel.Export;
        if (((LevelExport)donorLevel.Export).Actors.Count(x => x.Index == actorIndex) != 1) throw new InvalidDataException("Certified template actor is not a unique level actor.");
        if (target.Exports.Any(x => x.ObjectName.ToString() == newActorName && x.OuterIndex.Index == targetLevel.Index)) throw new InvalidDataException("Target map already contains the requested actor name.");
        var closure = Closure(donor, actorIndex); ValidateClosure(donor, closure);
        var firstNew = target.Exports.Count + 1; var remap = closure.Select((old, offset) => (old, @new: firstNew + offset)).ToDictionary(x => x.old, x => x.@new);
        remap[donorLevel.Index] = targetLevel.Index; remap[donorWorld.Index] = targetWorld.Index;

        int MapIndex(int index)
        {
            if (index == 0) return 0;
            if (remap.TryGetValue(index, out var mapped)) return mapped;
            var path = ResolveIndex(donor, index);
            var candidates = Enumerable.Range(1, target.Exports.Count).Concat(Enumerable.Range(1, target.Imports.Count).Select(x => -x)).Where(x => ResolveIndex(target, x) == path).ToArray();
            if (candidates.Length == 1) return candidates[0];
            if (index > 0) throw new InvalidDataException($"Template export reference {path} is outside the certified closure and has no unique target equivalent.");
            var source = donor.Imports[-index - 1];
            var outer = MapIndex(source.OuterIndex.Index);
            var added = new Import(source.ClassPackage.ToString(), source.ClassName.ToString(), new FPackageIndex(outer), source.ObjectName.ToString(), source.bImportOptional, target);
            if (source.PackageName is not null) added.PackageName = new FName(target, source.PackageName.ToString());
            target.Imports.Add(added); var result = -target.Imports.Count; remap[index] = result; return result;
        }

        var clones = new List<Export>();
        foreach (var index in closure)
        {
            var source = donor.Exports[index - 1]; var clone = (Export)source.Clone(); clone.Asset = target;
            clone.ObjectName = new FName(target, source.ObjectName.ToString()); clone.OuterIndex = new FPackageIndex(MapIndex(source.OuterIndex.Index)); clone.ClassIndex = new FPackageIndex(MapIndex(source.ClassIndex.Index));
            clone.SuperIndex = new FPackageIndex(MapIndex(source.SuperIndex.Index)); clone.TemplateIndex = new FPackageIndex(MapIndex(source.TemplateIndex.Index));
            RemapList(clone.SerializationBeforeSerializationDependencies, MapIndex); RemapList(clone.CreateBeforeSerializationDependencies, MapIndex);
            RemapList(clone.SerializationBeforeCreateDependencies, MapIndex); RemapList(clone.CreateBeforeCreateDependencies, MapIndex);
            if (clone is NormalExport normal && source is NormalExport sourceNormal)
            {
                normal.Data = sourceNormal.Data.Select(x => CloneProperty(x, target, MapIndex)).ToList();
                if (normal.ObjectGuid is not null) normal.ObjectGuid = DeterministicGuid(newActorName + "\n" + index);
            }
            var classPath = ClassPath(donor, index);
            clone.Extras = classPath switch
            {
                "/Script/Engine.Model" => RemapModelExtras(source.Extras, MapIndex),
                "/Script/Engine.BodySetup" => RemapBodySetupExtras(source.Extras, donor, target),
                _ => (byte[])source.Extras.Clone()
            };
            clones.Add(clone);
        }
        clones[0].ObjectName = new FName(target, newActorName);
        var componentIndex = closure.Single(x => ClassPath(donor, x) == "/Script/Engine.BrushComponent");
        var component = (NormalExport)clones[closure.IndexOf(componentIndex)];
        SetTransform(component, target, "RelativeLocation", location, new { x = 0.0, y = 0.0, z = 0.0 });
        SetTransform(component, target, "RelativeRotation", rotation, new { pitch = 0.0, yaw = 0.0, roll = 0.0 });
        if (scale is JsonElement scaleValue) SetTransform(component, target, "RelativeScale3D", scaleValue, new { x = 1.0, y = 1.0, z = 1.0 });
        target.Exports.AddRange(clones); targetLevelExport.Actors.Add(new FPackageIndex(remap[actorIndex])); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!); target.Write(outputPath);
        var written = new UAsset(outputPath, EngineVersion.VER_UE4_15); var writtenLevel = (LevelExport)written.Exports[targetLevel.Index - 1];
        if (!writtenLevel.Actors.Any(x => x.Index == remap[actorIndex])) throw new InvalidDataException("Template collision actor failed reload verification.");
        var writtenModel = remap[closure.Single(x => ClassPath(donor, x) == "/Script/Engine.Model")];
        var decoded = new NativeBspDecoder().Decode(outputPath, PackagePaths.FromInternalPath(outputPath)).Models.Single(x => x.ExportIndex == writtenModel);
        var donorDecoded = new NativeBspDecoder().Decode(templatePath, PackagePaths.FromInternalPath(templatePath)).Models.Single(x => x.ExportIndex == closure.Single(y => ClassPath(donor, y) == "/Script/Engine.Model"));
        if (GeometryFingerprint(decoded) != GeometryFingerprint(donorDecoded)) throw new InvalidDataException("Transplanted collision box geometry failed semantic verification.");
        return new ActorCloneResult(targetLevel.Index, remap.Where(x => closure.Contains(x.Key)).Select(x => x.Value).Order().ToArray());
    }

    private static void ValidateClosure(UAsset asset, IReadOnlyList<int> closure)
    {
        var classes = closure.Select(x => ClassPath(asset, x)).ToArray();
        if (closure.Count != 4 || classes.Count(x => x == "/Script/Engine.BlockingVolume") != 1 || classes.Count(x => x == "/Script/Engine.BrushComponent") != 1 ||
            classes.Count(x => x == "/Script/Engine.BodySetup") != 1 || classes.Count(x => x == "/Script/Engine.Model") != 1)
            throw new InvalidDataException("Collision box template must be a four-export BlockingVolume/BrushComponent/BodySetup/UModel closure.");
    }

    private static List<int> Closure(UAsset asset, int actorIndex)
    {
        var result = new List<int> { actorIndex };
        for (var at = 0; at < result.Count; at++) for (var i = 0; i < asset.Exports.Count; i++) if (asset.Exports[i].OuterIndex.Index == result[at]) result.Add(i + 1);
        return result;
    }

    private static void ValidateAxisAlignedBox(BspModel model)
    {
        var points = model.Polygons.SelectMany(x => x.Positions).Select(x => $"{x.X:R},{x.Y:R},{x.Z:R}").Distinct(StringComparer.Ordinal).Count();
        if (model.Polygons.Count != 6 || points != 8) throw new InvalidDataException($"Template geometry must have six faces and eight vertices; found {model.Polygons.Count} and {points}.");
        if (model.Polygons.Any(x => new[] { Math.Abs(x.Normal.X), Math.Abs(x.Normal.Y), Math.Abs(x.Normal.Z) }.Count(v => v > 0.999) != 1)) throw new InvalidDataException("Template faces are not axis-aligned.");
        var d = new[] { model.Metrics.BoundsMaximum.X - model.Metrics.BoundsMinimum.X, model.Metrics.BoundsMaximum.Y - model.Metrics.BoundsMinimum.Y, model.Metrics.BoundsMaximum.Z - model.Metrics.BoundsMinimum.Z };
        if (d.Any(x => x <= 0) || d.Max() - d.Min() > 0.001) throw new InvalidDataException("Template is not a non-zero cube.");
    }

    private static string GeometryFingerprint(BspModel model) => Identity.Sha256Bytes(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { model.Metrics.BoundsMinimum, model.Metrics.BoundsMaximum, model.Metrics.SurfaceArea, model.Metrics.PlaneCoverageFingerprint, polygons = model.Polygons.Count }, ReconJson.Options)));
    private static string CollisionFingerprint(byte[] extras) { var data = (byte[])extras.Clone(); if (data.Length < 36) throw new InvalidDataException("Template BodySetup native data is too short."); Array.Clear(data, 28, 4); return Identity.Sha256Bytes(data); }
    private static string Role(UAsset asset, int index) => ClassPath(asset, index).Split('.').Last();
    private static bool IsStructuralMapActor(string classPath) => classPath.EndsWith(".WorldSettings", StringComparison.Ordinal) || classPath.EndsWith(".UTWorldSettings", StringComparison.Ordinal) ||
        classPath.EndsWith(".Brush", StringComparison.Ordinal) || classPath.EndsWith(".LevelScriptActor", StringComparison.Ordinal) || classPath.EndsWith(".DefaultPhysicsVolume", StringComparison.Ordinal) ||
        classPath.EndsWith(".AbstractNavData", StringComparison.Ordinal);
    private static string ClassPath(UAsset asset, int exportIndex) => ResolveIndex(asset, asset.Exports[exportIndex - 1].ClassIndex.Index);
    private static (int Index, Export Export) SingleClass(UAsset asset, string name) { var items = asset.Exports.Select((x, i) => (Index: i + 1, Export: x)).Where(x => x.Export.GetExportClassType().ToString() == name).ToArray(); return items.Length == 1 ? items[0] : throw new InvalidDataException($"Expected one {name} export, found {items.Length}."); }
    private static string ResolveIndex(UAsset asset, int index, HashSet<int>? seen = null) { if (index == 0) return ""; seen ??= []; if (!seen.Add(index)) return "<cycle>"; var name = index > 0 ? asset.Exports[index - 1].ObjectName.ToString() : asset.Imports[-index - 1].ObjectName.ToString(); var outer = index > 0 ? asset.Exports[index - 1].OuterIndex.Index : asset.Imports[-index - 1].OuterIndex.Index; var parent = ResolveIndex(asset, outer, seen); return string.IsNullOrEmpty(parent) ? name : parent + "." + name; }
    private static void VerifyFile(FileIdentity expected) { var info = new FileInfo(expected.Path); if (!info.Exists || info.Length != expected.Size || Identity.Sha256File(info.FullName) != expected.Sha256) throw new InvalidDataException("Collision box template package identity mismatch."); }
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
    private static byte[] RemapBodySetupExtras(byte[] input, UAsset donor, UAsset target)
    {
        if (input.Length < 36) throw new InvalidDataException("Unsupported template BodySetup native layout."); var data = (byte[])input.Clone(); var name = donor.GetNameReference(BitConverter.ToInt32(data, 28)).ToString(); var names = target.GetNameMapIndexList(); var index = Enumerable.Range(0, names.Count).SingleOrDefault(i => names[i].ToString() == name, -1); if (index < 0) { target.AddNameReference(new FString(name)); names = target.GetNameMapIndexList(); index = Enumerable.Range(0, names.Count).Single(i => names[i].ToString() == name); } WriteInt(data, 28, index); return data;
    }
    private static void SetTransform(NormalExport export, UAsset asset, string name, JsonElement value, object defaultValue)
    {
        var existing = export.Data.SingleOrDefault(x => x.Name.ToString() == name);
        if (existing is null)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(defaultValue, ReconJson.Options));
            if (PropertyPatcher.JsonEquivalent(document.RootElement, value)) return;
            PropertyData vector = name == "RelativeRotation" ? new RotatorPropertyData(new FName(asset, name)) : new VectorPropertyData(new FName(asset, name)); export.Data.Add(vector); existing = vector;
        }
        PropertyPatcher.SetJsonValue(asset, existing, value);
    }
    private static void SkipBulk(BinaryReader reader, int stride) { var at = reader.BaseStream.Position; var actual = reader.ReadInt32(); if (actual != stride) throw new InvalidDataException($"Unsupported template UModel bulk stride {actual} at {at}; expected {stride}."); var count = ReadCount(reader); reader.BaseStream.Position += (long)stride * count; }
    private static int ReadCount(BinaryReader reader) { var value = reader.ReadInt32(); return value is >= 0 and <= 1_000_000 ? value : throw new InvalidDataException("Invalid template native array count."); }
    private static void WriteInt(byte[] data, long offset, int value) => BitConverter.GetBytes(value).CopyTo(data, checked((int)offset));
    private static Guid DeterministicGuid(string value) { var bytes = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)); return new Guid(bytes.AsSpan(0, 16)); }
}
