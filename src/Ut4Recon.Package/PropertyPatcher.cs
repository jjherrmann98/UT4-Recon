using System.Text.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;

namespace Ut4Recon.Package;

public sealed record PropertyPatchResult(JsonElement Before, JsonElement After, string ExtrasHashBefore, string ExtrasHashAfter);
public sealed record ActorDeletionResult(int LevelExportIndex, int ActorExportIndex);
public sealed record ActorCloneResult(int LevelExportIndex, IReadOnlyList<int> AddedExportIndexes);

public sealed class PropertyPatcher
{
    public bool IsPersistentLevelActor(string packagePath, int exportIndex)
    {
        var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15);
        return asset.Exports.OfType<LevelExport>().Single().Actors.Count(x => x.Index == exportIndex) == 1;
    }

    public ActorDeletionResult DeleteActor(string inputPath, string outputPath, int actorExportIndex)
    {
        var asset = new UAsset(inputPath, EngineVersion.VER_UE4_15); var levels = asset.Exports.Select((item, index) => (item, index)).Where(x => x.item is LevelExport).ToArray();
        if (levels.Length != 1) throw new InvalidDataException($"Expected one level export, found {levels.Length}.");
        var level = (LevelExport)levels[0].item; var matches = level.Actors.Select((item, index) => (item, index)).Where(x => x.item.Index == actorExportIndex).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Actor export {actorExportIndex} occurs {matches.Length} times in the level actor list.");
        level.Actors[matches[0].index] = new FPackageIndex(0);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!); asset.Write(outputPath);
        var written = new UAsset(outputPath, EngineVersion.VER_UE4_15); var writtenLevel = (LevelExport)written.Exports[levels[0].index];
        if (writtenLevel.Actors.Any(x => x.Index == actorExportIndex) || writtenLevel.Actors[matches[0].index].Index != 0) throw new InvalidDataException("Deleted actor reference failed reload verification.");
        return new ActorDeletionResult(levels[0].index + 1, actorExportIndex);
    }

    public ActorCloneResult CloneStaticMeshActor(string inputPath, string outputPath, int actorExportIndex, string newActorName, JsonElement location)
        => CloneActorClosure(inputPath, outputPath, actorExportIndex, newActorName, location, null, null, "/Script/Engine.StaticMeshActor");

    public ActorCloneResult CloneBlockingVolume(string inputPath, string outputPath, int actorExportIndex, string newActorName, JsonElement location, JsonElement rotation, JsonElement? scale = null)
        => CloneActorClosure(inputPath, outputPath, actorExportIndex, newActorName, location, rotation, scale, "/Script/Engine.BlockingVolume");

    private ActorCloneResult CloneActorClosure(string inputPath, string outputPath, int actorExportIndex, string newActorName, JsonElement location, JsonElement? rotation, JsonElement? scale, string requiredClass)
    {
        if (string.IsNullOrWhiteSpace(newActorName) || newActorName.Any(x => !char.IsLetterOrDigit(x) && x != '_')) throw new InvalidDataException("New actor name must contain only letters, digits, and underscores.");
        var asset = new UAsset(inputPath, EngineVersion.VER_UE4_15); var levels = asset.Exports.Select((item, index) => (item, index)).Where(x => x.item is LevelExport).ToArray();
        if (levels.Length != 1) throw new InvalidDataException("Expected one level export."); var level = (LevelExport)levels[0].item;
        if (level.Actors.Count(x => x.Index == actorExportIndex) != 1) throw new InvalidDataException("Template is not a unique persistent-level actor.");
        if (asset.Exports.Any(x => x.ObjectName.ToString() == newActorName && x.OuterIndex.Index == levels[0].index + 1)) throw new InvalidDataException("An actor with the requested name already exists.");
        if (!asset.Exports[actorExportIndex - 1].GetExportClassType().ToString().Contains(requiredClass.Split('.').Last(), StringComparison.Ordinal))
            throw new NotSupportedException($"Clone template is not a {requiredClass.Split('.').Last()}.");
        var closure = new List<int> { actorExportIndex };
        for (var at = 0; at < closure.Count; at++) for (var i = 0; i < asset.Exports.Count; i++) if (asset.Exports[i].OuterIndex.Index == closure[at]) closure.Add(i + 1);
        var componentIndexes = closure.Skip(1).Where(index => asset.Exports[index - 1].GetExportClassType().ToString().Contains(requiredClass.EndsWith("BlockingVolume", StringComparison.Ordinal) ? "BrushComponent" : "StaticMeshComponent", StringComparison.Ordinal)).ToArray();
        if (componentIndexes.Length != 1 || asset.Exports[componentIndexes[0] - 1] is not NormalExport)
            throw new NotSupportedException("Clone addition requires one supported root component.");
        if (requiredClass.EndsWith("StaticMeshActor", StringComparison.Ordinal) && closure.Count != 2)
            throw new NotSupportedException("StaticMeshActor clone requires exactly one component export.");
        if (requiredClass.EndsWith("BlockingVolume", StringComparison.Ordinal))
        {
            var types = closure.Skip(1).Select(index => asset.Exports[index - 1].GetExportClassType().ToString()).ToArray();
            if (closure.Count != 4 || types.Count(x => x.Contains("BrushComponent", StringComparison.Ordinal)) != 1 || types.Count(x => x.Contains("BodySetup", StringComparison.Ordinal)) != 1 || types.Count(x => x.EndsWith("Model", StringComparison.Ordinal)) != 1)
                throw new NotSupportedException("BlockingVolume clone requires the actor, one BrushComponent, one BodySetup, and one UModel export.");
        }
        var firstNewIndex = asset.Exports.Count + 1; var remap = closure.Select((oldIndex, offset) => (oldIndex, newIndex: firstNewIndex + offset)).ToDictionary(x => x.oldIndex, x => x.newIndex);
        var clones = closure.Select(index => (Export)asset.Exports[index - 1].Clone()).ToArray();
        for (var i = 0; i < clones.Length; i++)
        {
            var clone = clones[i]; var original = asset.Exports[closure[i] - 1]; clone.Asset = asset;
            clone.SerializationBeforeSerializationDependencies = [.. original.SerializationBeforeSerializationDependencies]; clone.CreateBeforeSerializationDependencies = [.. original.CreateBeforeSerializationDependencies];
            clone.SerializationBeforeCreateDependencies = [.. original.SerializationBeforeCreateDependencies]; clone.CreateBeforeCreateDependencies = [.. original.CreateBeforeCreateDependencies]; clone.Extras = (byte[])original.Extras.Clone();
            clone.OuterIndex = Remap(clone.OuterIndex, remap); clone.ClassIndex = Remap(clone.ClassIndex, remap); clone.SuperIndex = Remap(clone.SuperIndex, remap); clone.TemplateIndex = Remap(clone.TemplateIndex, remap);
            RemapList(clone.SerializationBeforeSerializationDependencies, remap); RemapList(clone.CreateBeforeSerializationDependencies, remap); RemapList(clone.SerializationBeforeCreateDependencies, remap); RemapList(clone.CreateBeforeCreateDependencies, remap);
            if (clone is NormalExport normal)
            {
                normal.Data = ((NormalExport)original).Data.Select(x => (PropertyData)x.Clone()).ToList();
                if (normal.ObjectGuid is not null) normal.ObjectGuid = DeterministicGuid(newActorName + "\n" + i);
                foreach (var property in normal.Data) RemapProperty(property, remap);
            }
            if (clone.GetExportClassType().ToString().EndsWith("Model", StringComparison.Ordinal)) RemapModelSurfaceActors(clone.Extras, remap);
        }
        clones[0].ObjectName = new FName(asset, newActorName);
        var componentClone = (NormalExport)clones[closure.IndexOf(componentIndexes[0])];
        SetTransformIfSerialized(asset, componentClone, "RelativeLocation", location, new { x = 0.0, y = 0.0, z = 0.0 });
        if (rotation is JsonElement rotationValue) SetTransformIfSerialized(asset, componentClone, "RelativeRotation", rotationValue, new { pitch = 0.0, yaw = 0.0, roll = 0.0 });
        if (scale is JsonElement scaleValue) SetTransformIfSerialized(asset, componentClone, "RelativeScale3D", scaleValue, new { x = 1.0, y = 1.0, z = 1.0 });
        asset.Exports.AddRange(clones); level.Actors.Add(new FPackageIndex(remap[actorExportIndex])); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!); asset.Write(outputPath);
        var written = new UAsset(outputPath, EngineVersion.VER_UE4_15); if (!((LevelExport)written.Exports[levels[0].index]).Actors.Any(x => x.Index == remap[actorExportIndex])) throw new InvalidDataException("Added actor failed reload verification.");
        return new ActorCloneResult(levels[0].index + 1, remap.Values.OrderBy(x => x).ToArray());
    }

    private static void RemapModelSurfaceActors(byte[] data, IReadOnlyDictionary<int, int> remap)
    {
        if (data.Length < 30) throw new InvalidDataException("Cloned UModel native data is too short.");
        var at = 30;
        SkipBulk(data, ref at, 12); SkipBulk(data, ref at, 12); SkipBulk(data, ref at, 64);
        var count = ReadCount(data, ref at); const int surfaceSize = 56;
        if (at + checked(count * surfaceSize) > data.Length) throw new InvalidDataException("Cloned UModel surfaces extend beyond native data.");
        for (var i = 0; i < count; i++)
        {
            var actorOffset = at + i * surfaceSize + 28; var actor = BitConverter.ToInt32(data, actorOffset);
            if (remap.TryGetValue(actor, out var mapped)) BitConverter.GetBytes(mapped).CopyTo(data, actorOffset);
        }
    }

    private static void SkipBulk(byte[] data, ref int at, int expectedStride)
    {
        var stride = ReadCount(data, ref at); var count = ReadCount(data, ref at);
        if (stride != expectedStride || at + checked(stride * count) > data.Length) throw new InvalidDataException("Unsupported cloned UModel bulk array.");
        at += stride * count;
    }

    private static int ReadCount(byte[] data, ref int at)
    {
        if (at + 4 > data.Length) throw new InvalidDataException("Cloned UModel native data ended unexpectedly.");
        var value = BitConverter.ToInt32(data, at); at += 4;
        if (value < 0 || value > 1_000_000) throw new InvalidDataException("Invalid cloned UModel array count.");
        return value;
    }

    private static FPackageIndex Remap(FPackageIndex value, IReadOnlyDictionary<int, int> remap) => new(remap.GetValueOrDefault(value.Index, value.Index));
    private static void RemapList(IList<FPackageIndex> values, IReadOnlyDictionary<int, int> remap) { for (var i = 0; i < values.Count; i++) values[i] = Remap(values[i], remap); }
    private static void RemapProperty(PropertyData property, IReadOnlyDictionary<int, int> remap)
    {
        if (property is ObjectPropertyData reference) reference.Value = Remap(reference.Value, remap);
        else if (property is StructPropertyData structure) foreach (var child in structure.Value) RemapProperty(child, remap);
        else if (property is ArrayPropertyData array) foreach (var child in array.Value) RemapProperty(child, remap);
    }
    private static Guid DeterministicGuid(string value) { var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)); return new Guid(bytes.AsSpan(0, 16)); }

    public IReadOnlyList<ProxyProperty> ReadEditableProperties(string packagePath, int exportIndex)
    {
        var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15);
        return ReadEditableProperties(asset, exportIndex);
    }

    public IReadOnlyList<ProxyProperty> ReadEditableProperties(UAsset asset, int exportIndex)
    {
        var export = GetNormalExport(asset, exportIndex);
        var paths = new SortedSet<string>(EditRules.KnownPaths, StringComparer.Ordinal);
        try
        {
            var responses = FindProperty(export, "BodyInstance.CollisionResponses") as ArrayPropertyData;
            if (responses is not null)
            {
                foreach (var item in responses.Value.OfType<StructPropertyData>())
                {
                    var channel = item.Value.OfType<NamePropertyData>().SingleOrDefault(x => x.Name.ToString() == "Channel")?.Value.ToString();
                    if (!string.IsNullOrEmpty(channel)) paths.Add("BodyInstance.CollisionResponses." + channel);
                }
            }
        }
        catch (KeyNotFoundException) { }

        var result = new List<ProxyProperty>();
        foreach (var path in paths)
        {
            try
            {
                var property = FindProperty(export, path); var rule = EditRules.RuleFor(path);
                if (rule is not null) result.Add(new ProxyProperty(path, ToJson(asset, property), rule));
            }
            catch (KeyNotFoundException) { }
        }
        return result;
    }

    public JsonElement ReadValue(string packagePath, int exportIndex, string propertyPath)
    {
        var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15);
        return ReadValue(asset, exportIndex, propertyPath);
    }

    public JsonElement ReadValue(UAsset asset, int exportIndex, string propertyPath) => ToJson(asset, FindProperty(asset, exportIndex, propertyPath));

    public PropertyPatchResult Apply(string inputPath, string outputPath, int exportIndex, string propertyPath, JsonElement expectedBefore, JsonElement after)
    {
        var rule = EditRules.RuleFor(propertyPath) ?? throw new InvalidOperationException($"Property is not allowlisted: {propertyPath}");
        _ = rule;
        var asset = new UAsset(inputPath, EngineVersion.VER_UE4_15);
        var export = GetNormalExport(asset, exportIndex);
        if (rule == "ut-level-summary-title-v1" && !export.GetExportClassType().ToString().Contains("UTLevelSummary", StringComparison.Ordinal))
            throw new InvalidOperationException("The Title editing rule is limited to UUTLevelSummary exports.");
        var property = FindProperty(export, propertyPath);
        var originalExportData = ExportDataJson(asset, exportIndex);
        var originalNames = RootSectionJson(asset, "NameMap");
        var originalImports = RootSectionJson(asset, "Imports");
        var actualBefore = ToJson(asset, property);
        if (!JsonEquivalent(actualBefore, expectedBefore)) throw new InvalidDataException($"Expected value does not match {propertyPath}. Actual: {actualBefore.GetRawText()}");

        var extrasBefore = Identity.Sha256Bytes(export.Extras);
        SetJsonValue(asset, property, after);
        var actualAfter = ToJson(asset, property);
        if (!JsonEquivalent(actualAfter, after)) throw new InvalidDataException($"Encoded value does not match requested value for {propertyPath}.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        asset.Write(outputPath);
        var written = new UAsset(outputPath, EngineVersion.VER_UE4_15);
        var writtenExport = GetNormalExport(written, exportIndex);
        var writtenAfter = ToJson(written, FindProperty(writtenExport, propertyPath));
        if (!JsonEquivalent(writtenAfter, after)) throw new InvalidDataException($"Written value failed reload verification for {propertyPath}.");
        var extrasAfter = Identity.Sha256Bytes(writtenExport.Extras);
        if (!extrasBefore.Equals(extrasAfter, StringComparison.Ordinal)) throw new InvalidDataException($"Opaque native data changed while patching {propertyPath}.");
        if (!JsonEquivalent(originalImports, RootSectionJson(written, "Imports")))
            throw new InvalidDataException($"Package imports changed while patching {propertyPath}.");
        ValidateNameMapChange(originalNames, RootSectionJson(written, "NameMap"), property, after);
        SetJsonValue(written, FindProperty(writtenExport, propertyPath), expectedBefore);
        if (!JsonEquivalent(originalExportData, ExportDataJson(written, exportIndex)))
            throw new InvalidDataException($"Unapproved tagged-property data changed in export {exportIndex} while patching {propertyPath}.");
        return new PropertyPatchResult(actualBefore, writtenAfter, extrasBefore, extrasAfter);
    }

    private static NormalExport GetNormalExport(UAsset asset, int exportIndex)
    {
        if (exportIndex < 1 || exportIndex > asset.Exports.Count) throw new IndexOutOfRangeException($"Export index is outside the package: {exportIndex}");
        return asset.Exports[exportIndex - 1] as NormalExport ?? throw new NotSupportedException($"Export {exportIndex} is not a tagged-property export.");
    }

    private static PropertyData FindProperty(UAsset asset, int exportIndex, string propertyPath) => FindProperty(GetNormalExport(asset, exportIndex), propertyPath);

    private static void SetTransformIfSerialized(UAsset asset, NormalExport export, string path, JsonElement requested, object identity)
    {
        try { SetJsonValue(asset, FindProperty(export, path), requested); }
        catch (KeyNotFoundException) when (JsonEquivalent(requested, JsonSerializer.SerializeToElement(identity))) { }
        catch (KeyNotFoundException) { throw new NotSupportedException($"The clone template does not serialize {path}; a non-default value cannot be added safely."); }
    }

    private static PropertyData FindProperty(NormalExport export, string propertyPath)
    {
        const string collisionResponsePrefix = "BodyInstance.CollisionResponses.";
        if (propertyPath.StartsWith(collisionResponsePrefix, StringComparison.Ordinal))
        {
            var channel = propertyPath[collisionResponsePrefix.Length..];
            var responses = FindProperty(export, "BodyInstance.CollisionResponses") as ArrayPropertyData
                ?? throw new InvalidDataException("CollisionResponses is not serialized as an array.");
            var matches = responses.Value.OfType<StructPropertyData>().Where(item =>
            {
                var channelProperty = item.Value.OfType<NamePropertyData>().SingleOrDefault(x => x.Name.ToString() == "Channel");
                return channelProperty?.Value.ToString().Equals(channel, StringComparison.Ordinal) == true;
            }).ToArray();
            if (matches.Length != 1) throw new KeyNotFoundException($"Collision response channel resolved to {matches.Length} entries: {channel}");
            return matches[0].Value.Single(x => x.Name.ToString() == "Response");
        }

        var segments = propertyPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) throw new ArgumentException("Property path is empty.", nameof(propertyPath));
        IReadOnlyList<PropertyData> properties = export.Data;
        PropertyData? current = null;
        foreach (var segment in segments)
        {
            current = properties.SingleOrDefault(x => x.Name.ToString().Equals(segment, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException($"Property was not serialized: {propertyPath}");
            properties = current is StructPropertyData structure ? structure.Value : [];
        }
        return current is StructPropertyData { Value.Count: 1 } wrapper ? wrapper.Value[0] : current!;
    }

    private static JsonElement ToJson(UAsset asset, PropertyData property) => property switch
    {
        VectorPropertyData vector => JsonSerializer.SerializeToElement(new { x = vector.Value.X, y = vector.Value.Y, z = vector.Value.Z }),
        RotatorPropertyData rotator => JsonSerializer.SerializeToElement(new { pitch = rotator.Value.Pitch, yaw = rotator.Value.Yaw, roll = rotator.Value.Roll }),
        BoolPropertyData boolean => JsonSerializer.SerializeToElement(boolean.Value),
        FloatPropertyData number => JsonSerializer.SerializeToElement(number.Value),
        NamePropertyData name => JsonSerializer.SerializeToElement(name.Value.ToString()),
        EnumPropertyData item => JsonSerializer.SerializeToElement(item.Value.ToString()),
        BytePropertyData value when value.ByteType.ToString().Equals("FName", StringComparison.Ordinal) => JsonSerializer.SerializeToElement(value.EnumValue.ToString()),
        BytePropertyData value => JsonSerializer.SerializeToElement(value.Value),
        StrPropertyData text => JsonSerializer.SerializeToElement(text.Value.ToString()),
        ArrayPropertyData array when array.Value.All(x => x is ObjectPropertyData) => JsonSerializer.SerializeToElement(
            array.Value.Cast<ObjectPropertyData>().Select(x => ResolveIndex(asset, x.Value.Index)).ToArray()),
        _ => throw new NotSupportedException($"Allowlisted property has unsupported serialized type: {property.GetType().Name}")
    };

    internal static void SetJsonValue(UAsset asset, PropertyData property, JsonElement value)
    {
        switch (property)
        {
            case VectorPropertyData vector:
                vector.Value = new FVector(RequiredNumber(value, "x"), RequiredNumber(value, "y"), RequiredNumber(value, "z"));
                break;
            case RotatorPropertyData rotator:
                rotator.Value = new FRotator(RequiredNumber(value, "pitch"), RequiredNumber(value, "yaw"), RequiredNumber(value, "roll"));
                break;
            case BoolPropertyData boolean when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                boolean.Value = value.GetBoolean();
                break;
            case FloatPropertyData number when value.ValueKind == JsonValueKind.Number:
                var floatValue = value.GetSingle(); if (!float.IsFinite(floatValue)) throw new InvalidDataException("Float value must be finite."); number.Value = floatValue;
                break;
            case NamePropertyData name when value.ValueKind == JsonValueKind.String:
                name.Value = new FName(asset, value.GetString()!);
                break;
            case EnumPropertyData item when value.ValueKind == JsonValueKind.String:
                item.Value = new FName(asset, value.GetString()!);
                break;
            case BytePropertyData item when item.ByteType.ToString().Equals("FName", StringComparison.Ordinal) && value.ValueKind == JsonValueKind.String:
                item.EnumValue = new FName(asset, value.GetString()!);
                break;
            case BytePropertyData item when value.ValueKind == JsonValueKind.Number && value.TryGetByte(out var byteValue):
                item.Value = byteValue;
                break;
            case StrPropertyData text when value.ValueKind == JsonValueKind.String:
                text.Value = new FString(value.GetString()!);
                break;
            case ArrayPropertyData array when array.Value.All(x => x is ObjectPropertyData) && value.ValueKind == JsonValueKind.Array:
                var requested = value.EnumerateArray().ToArray();
                if (requested.Length != array.Value.Length) throw new InvalidDataException("Material override slot count cannot change in the direct writer.");
                for (var i = 0; i < requested.Length; i++)
                {
                    var reference = requested[i];
                    var index = reference.ValueKind switch
                    {
                        JsonValueKind.Null => 0,
                        JsonValueKind.String => ResolveExistingIndex(asset, reference.GetString()!),
                        _ => throw new InvalidDataException("Material override entries must be object-path strings or null.")
                    };
                    ((ObjectPropertyData)array.Value[i]).Value = new FPackageIndex(index);
                }
                break;
            default:
                throw new InvalidDataException($"Value {value.GetRawText()} is invalid for {property.GetType().Name}.");
        }
    }

    private static void ValidateNameMapChange(JsonElement before, JsonElement after, PropertyData property, JsonElement requested)
    {
        if (JsonEquivalent(before, after)) return;
        var isNameValue = property is NamePropertyData or EnumPropertyData || property is BytePropertyData item && item.ByteType.ToString().Equals("FName", StringComparison.Ordinal);
        if (!isNameValue || requested.ValueKind != JsonValueKind.String) throw new InvalidDataException("The package name table changed for a property that cannot require a new name.");
        var oldNames = before.EnumerateArray().Select(x => x.GetString()).ToArray(); var newNames = after.EnumerateArray().Select(x => x.GetString()).ToArray();
        if (newNames.Length != oldNames.Length + 1 || !newNames.Take(oldNames.Length).SequenceEqual(oldNames) || newNames[^1] != requested.GetString())
            throw new InvalidDataException("The package writer made an unexplained name-table change.");
    }

    private static JsonElement ExportDataJson(UAsset asset, int exportIndex)
    {
        using var document = JsonDocument.Parse(asset.SerializeJson());
        return document.RootElement.GetProperty("Exports")[exportIndex - 1].GetProperty("Data").Clone();
    }

    private static JsonElement RootSectionJson(UAsset asset, string section)
    {
        using var document = JsonDocument.Parse(asset.SerializeJson());
        return document.RootElement.GetProperty(section).Clone();
    }

    private static int ResolveExistingIndex(UAsset asset, string objectPath)
    {
        var matches = Enumerable.Range(1, asset.Exports.Count).Concat(Enumerable.Range(1, asset.Imports.Count).Select(x => -x))
            .Where(index => string.Equals(ResolveIndex(asset, index), objectPath, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new KeyNotFoundException($"Material object path resolved to {matches.Length} existing package references: {objectPath}");
        return matches[0];
    }

    private static string? ResolveIndex(UAsset asset, int index, HashSet<int>? seen = null)
    {
        if (index == 0) return null;
        seen ??= [];
        if (!seen.Add(index)) return "<cycle>";
        var name = index > 0 ? asset.Exports[index - 1].ObjectName.ToString() : asset.Imports[-index - 1].ObjectName.ToString();
        var outer = index > 0 ? asset.Exports[index - 1].OuterIndex.Index : asset.Imports[-index - 1].OuterIndex.Index;
        var parent = ResolveIndex(asset, outer, seen);
        return parent is null ? name : parent + "." + name;
    }

    private static double RequiredNumber(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var component) || component.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"Expected numeric component '{name}'.");
        var result = component.GetDouble();
        if (!double.IsFinite(result)) throw new InvalidDataException($"Component '{name}' must be finite.");
        return result;
    }

    public static bool JsonEquivalent(JsonElement left, JsonElement right)
    {
        if (left.ValueKind == JsonValueKind.Number && right.ValueKind == JsonValueKind.Number)
            return Math.Abs(left.GetDouble() - right.GetDouble()) <= 1e-9;
        if (left.ValueKind != right.ValueKind) return false;
        if (left.ValueKind == JsonValueKind.Object)
        {
            var leftProperties = left.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            var rightProperties = right.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal).ToArray();
            return leftProperties.Length == rightProperties.Length && leftProperties.Zip(rightProperties).All(pair => pair.First.Name == pair.Second.Name && JsonEquivalent(pair.First.Value, pair.Second.Value));
        }
        if (left.ValueKind == JsonValueKind.Array)
        {
            var leftItems = left.EnumerateArray().ToArray(); var rightItems = right.EnumerateArray().ToArray();
            return leftItems.Length == rightItems.Length && leftItems.Zip(rightItems).All(pair => JsonEquivalent(pair.First, pair.Second));
        }
        return left.GetRawText() == right.GetRawText();
    }
}
