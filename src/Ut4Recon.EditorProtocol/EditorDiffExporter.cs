using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.EditorProtocol;

public sealed class EditorDiffExporter
{
    internal static readonly HashSet<string> BridgeProperties = new(StringComparer.Ordinal)
    {
        "RelativeLocation", "RelativeRotation", "RelativeScale3D", "BoxExtent", "CapsuleHalfHeight", "CapsuleRadius", "SphereRadius", "CollisionProfileName"
    };

    internal static bool CapturesProperty(string propertyPath) => BridgeProperties.Contains(propertyPath);

    public (EditManifest Manifest, EditorDiffReport Report) Export(string stateDirectory, EditorWorkspace workspace)
    {
        var irPath = Path.Combine(stateDirectory, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var scene = ReadJson<ProxyScene>(workspace.ProxyScene);
        var expected = Regex.Matches(File.ReadAllText(workspace.ImportT3d), "UT4RECON:([0-9a-f]{64})").Cast<Match>().Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var mapFile = Path.Combine(Path.GetDirectoryName(workspace.ProjectFile)!, "Content", workspace.MapPackagePath[6..].Replace('/', Path.DirectorySeparatorChar) + ".umap");
        var asset = new UAsset(mapFile, EngineVersion.VER_UE4_15); using var document = JsonDocument.Parse(asset.SerializeJson()); var exports = document.RootElement.GetProperty("Exports");
        var level = asset.Exports.OfType<UAssetAPI.ExportTypes.LevelExport>().Single(); var liveActors = level.Actors.Where(x => x.IsExport()).Select(x => x.Index).ToHashSet();
        var childrenByOuter = Enumerable.Range(1, exports.GetArrayLength()).GroupBy(index => exports[index - 1].GetProperty("OuterIndex").GetInt32()).ToDictionary(group => group.Key, group => group.ToArray());
        var tagged = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < exports.GetArrayLength(); i++)
        {
            if (!BelongsToLiveActor(asset, i + 1, liveActors)) continue;
            if (!exports[i].TryGetProperty("Data", out var data)) continue;
            foreach (var id in ReconstructionIds(data))
                if (!tagged.TryAdd(id, i + 1)) throw new InvalidDataException($"Duplicate reconstruction ID in editor map: {id}");
        }
        BridgeSavedMarkerComponents(scene, document.RootElement, exports, childrenByOuter, tagged);
        var missing = expected.Except(tagged.Keys, StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var operations = new List<EditOperation>(); var diagnostics = new List<string>(); var patcher = new PropertyPatcher();
        var originalObjects = ir.Packages.SelectMany(package => package.Objects.Select(item => (package, item))).ToDictionary(x => x.item.Id, StringComparer.Ordinal);
        var handledMissing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var actor in scene.Objects.Where(x => x.Kind == "actor" && missing.Contains(x.ReconstructionId, StringComparer.Ordinal)))
        {
            if (!actor.Capabilities.Any(capability => capability.Operation == "delete-actor" && capability.State == CapabilityState.Available)) continue;
            var original = originalObjects[actor.ReconstructionId];
            operations.Add(new EditOperation("delete-actor", original.package.PackagePath, actor.ReconstructionId, actor.ObjectPath, original.item.PayloadHash, "$levelActorReference",
                JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement(false), "editor-proxy", "persistent-level-actor-delete-v1"));
            handledMissing.Add(actor.ReconstructionId);
            foreach (var child in scene.Objects.Where(x => x.OwnerReconstructionId == actor.ReconstructionId)) handledMissing.Add(child.ReconstructionId);
        }
        foreach (var proxy in scene.Objects.Where(x => tagged.ContainsKey(x.ReconstructionId)))
        {
            var original = originalObjects[proxy.ReconstructionId];
            foreach (var property in proxy.EditableProperties.Where(x => BridgeProperties.Contains(x.PropertyPath)))
            {
                JsonElement editorValue;
                try { editorValue = patcher.ReadValue(asset, tagged[proxy.ReconstructionId], property.PropertyPath); }
                catch (KeyNotFoundException) { diagnostics.Add($"{proxy.ObjectPath}.{property.PropertyPath}: editor omitted the property; treated as unchanged in protocol v1."); continue; }
                if (PropertyPatcher.JsonEquivalent(property.Value, editorValue)) continue;
                operations.Add(new EditOperation("set-property", original.package.PackagePath, proxy.ReconstructionId, proxy.ObjectPath, original.item.PayloadHash,
                    property.PropertyPath, property.Value, editorValue, "editor-proxy", property.SupportRule));
            }
        }
        var collisionOverlaysResolved = ExportCollisionOverlayEdits(workspace, scene, asset, document.RootElement, exports, liveActors, childrenByOuter, originalObjects, operations, diagnostics, patcher);
        var mapPackage = ir.Packages.Single(x => x.PackagePath == scene.MapPackagePath);
        var taggedExports = tagged.Values.ToHashSet(); var unsupportedAdditions = new List<string>();
        var staticMeshAdditions = new List<int>();
        foreach (var actorIndex in liveActors.Where(x => !taggedExports.Contains(x)))
        {
            if (HasTag(exports[actorIndex - 1], "UT4RECON_COLLISION_OVERLAY:")) continue;
            if (HasTag(exports[actorIndex - 1], "UT4RECON_VISUALIZATION:")) continue;
            var editorClass = ResolveIndex(document.RootElement, exports[actorIndex - 1].GetProperty("ClassIndex").GetInt32());
            if (editorClass is "/Script/Engine.Brush" or "/Script/Engine.AbstractNavData" or "/Script/UnrealTournament.UTWorldSettings" or "/Script/Foliage.InstancedFoliageActor") continue;
            if (editorClass != "/Script/Engine.StaticMeshActor") { unsupportedAdditions.Add(exports[actorIndex - 1].GetProperty("ObjectName").GetString()!); continue; }
            staticMeshAdditions.Add(actorIndex);
        }
        if (staticMeshAdditions.Count != 0)
        {
            var baselineMap = Path.Combine(stateDirectory, "baseline", mapPackage.InternalPath.Replace('/', Path.DirectorySeparatorChar));
            using var baselineDocument = JsonDocument.Parse(new UAsset(baselineMap, EngineVersion.VER_UE4_15).SerializeJson()); var baselineRoot = baselineDocument.RootElement;
            var sceneChildren = scene.Objects.Where(x => x.OwnerReconstructionId is not null).GroupBy(x => x.OwnerReconstructionId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var actorIndex in staticMeshAdditions)
            {
            var components = childrenByOuter.GetValueOrDefault(actorIndex, []).Where(index => ResolveIndex(document.RootElement, exports[index - 1].GetProperty("ClassIndex").GetInt32()) == "/Script/Engine.StaticMeshComponent").ToArray();
            if (components.Length != 1) { unsupportedAdditions.Add(exports[actorIndex - 1].GetProperty("ObjectName").GetString()!); continue; }
            var mesh = ObjectReference(document.RootElement, components[0], "StaticMesh"); var materials = ObjectReferences(document.RootElement, components[0], "OverrideMaterials");
            var candidates = scene.Objects.Where(x => x.Kind == "actor" && x.ClassPath == "/Script/Engine.StaticMeshActor").Where(actor =>
            {
                var child = sceneChildren.GetValueOrDefault(actor.ReconstructionId, []).SingleOrDefault(x => x.ClassPath == "/Script/Engine.StaticMeshComponent");
                return child is not null && ObjectReference(baselineRoot, child.ExportIndex, "StaticMesh") == mesh && ObjectReferences(baselineRoot, child.ExportIndex, "OverrideMaterials").SequenceEqual(materials, StringComparer.Ordinal);
            }).ToArray();
            if (candidates.Length == 0) { unsupportedAdditions.Add(exports[actorIndex - 1].GetProperty("ObjectName").GetString()!); continue; }
            var template = candidates.OrderBy(x => x.ReconstructionId, StringComparer.Ordinal).First(); var original = mapPackage.Objects.Single(x => x.Id == template.ReconstructionId);
            JsonElement location; try { location = patcher.ReadValue(asset, components[0], "RelativeLocation"); } catch (KeyNotFoundException) { location = JsonSerializer.SerializeToElement(new { x = 0, y = 0, z = 0 }); }
            var editorName = exports[actorIndex - 1].GetProperty("ObjectName").GetString()!;
            var name = RuntimeCloneActorName("Mesh", template.ReconstructionId, editorName);
            var after = JsonSerializer.SerializeToElement(new { name, location });
            operations.Add(new EditOperation("clone-actor", mapPackage.PackagePath, template.ReconstructionId, template.ObjectPath, original.PayloadHash, "$actorClone",
                JsonSerializer.SerializeToElement<object?>(null), after, "editor-proxy", "simple-static-mesh-actor-clone-v1"));
            }
        }
        var unresolvedMissing = missing.Where(x => !handledMissing.Contains(x)).ToArray();
        if (unresolvedMissing.Length != 0) diagnostics.Add("One or more missing proxy objects could not be represented as a supported actor deletion.");
        if (unsupportedAdditions.Count != 0) diagnostics.Add("Unsupported added actors: " + string.Join(", ", unsupportedAdditions.OrderBy(x => x, StringComparer.Ordinal)));
        var manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, Identity.File(irPath, "reconstruction-ir.json"), operations);
        var report = new EditorDiffReport(1, ir.Profile, manifest.ReconstructionIr, expected.Count, expected.Count - missing.Length, operations.Count, unresolvedMissing, diagnostics, unresolvedMissing.Length == 0 && unsupportedAdditions.Count == 0 && collisionOverlaysResolved);
        return (manifest, report);
    }

    private static void BridgeSavedMarkerComponents(ProxyScene scene, JsonElement root, JsonElement exports,
        IReadOnlyDictionary<int, int[]> childrenByOuter, IDictionary<string, int> tagged)
    {
        // UT4 4.15 canonicalizes an imported StaticMeshActor on save: its named preview component
        // becomes StaticMeshComponent0 and ComponentTags are discarded. The actor marker tag survives.
        // Re-associate only the deliberately inert player-start/pickup marker actors with their single
        // saved StaticMeshComponent so this editor normalization cannot look like a runtime deletion.
        foreach (var component in scene.Objects.Where(x => x.Kind == "component" && x.OwnerReconstructionId is not null && !tagged.ContainsKey(x.ReconstructionId)))
        {
            if (!tagged.TryGetValue(component.OwnerReconstructionId!, out var actorIndex)) continue;
            var actorExport = exports[actorIndex - 1];
            if (!HasTag(actorExport, "UT4RECON_PLAYER_START_MARKER") && !HasTag(actorExport, "UT4RECON_PICKUP_MARKER")) continue;
            var candidates = childrenByOuter.GetValueOrDefault(actorIndex, [])
                .Where(index => ResolveIndex(root, exports[index - 1].GetProperty("ClassIndex").GetInt32()) == "/Script/Engine.StaticMeshComponent")
                .ToArray();
            if (candidates.Length == 1) tagged.Add(component.ReconstructionId, candidates[0]);
        }
    }

    private static bool BelongsToLiveActor(UAsset asset, int exportIndex, IReadOnlySet<int> liveActors)
    {
        var current = exportIndex; var seen = new HashSet<int>();
        while (current > 0 && seen.Add(current))
        {
            if (liveActors.Contains(current)) return true;
            current = asset.Exports[current - 1].OuterIndex.Index;
        }
        return false;
    }

    private static IEnumerable<string> ReconstructionIds(JsonElement properties)
    {
        foreach (var property in properties.EnumerateArray())
        {
            var name = property.GetProperty("Name").GetString(); if (name is not "Tags" and not "ComponentTags" || !property.TryGetProperty("Value", out var values)) continue;
            foreach (var item in values.EnumerateArray())
            {
                var value = item.TryGetProperty("Value", out var inner) ? inner.GetString() : null;
                const string prefix = "UT4RECON:";
                if (value?.StartsWith(prefix, StringComparison.Ordinal) == true && value.Length == prefix.Length + 64) yield return value[prefix.Length..];
            }
        }
    }

    private static bool HasTag(JsonElement export, string prefix)
    {
        if (!export.TryGetProperty("Data", out var properties)) return false;
        foreach (var property in properties.EnumerateArray())
        {
            if (property.GetProperty("Name").GetString() is not "Tags" || !property.TryGetProperty("Value", out var values)) continue;
            foreach (var item in values.EnumerateArray())
            {
                var value = item.TryGetProperty("Value", out var inner) ? inner.GetString() : null;
                if (value?.StartsWith(prefix, StringComparison.Ordinal) == true) return true;
            }
        }
        return false;
    }

    private static bool ExportCollisionOverlayEdits(EditorWorkspace workspace, ProxyScene scene, UAsset asset, JsonElement root, JsonElement exports, IReadOnlySet<int> liveActors,
        IReadOnlyDictionary<int, int[]> childrenByOuter,
        IReadOnlyDictionary<string, (PackageInventory package, PackageObject item)> originals, IList<EditOperation> operations, IList<string> diagnostics, PropertyPatcher patcher)
    {
        if (string.IsNullOrEmpty(workspace.CollisionOverlayIndex) || !File.Exists(workspace.CollisionOverlayIndex)) return true;
        var resolved = true;
        var overlayIndex = ReadJson<CollisionOverlayIndex>(workspace.CollisionOverlayIndex);
        var objectsById = scene.Objects.ToDictionary(x => x.ReconstructionId, StringComparer.Ordinal);
        var overlayActors = liveActors.SelectMany(index => TagValues(exports[index - 1], "UT4RECON_COLLISION_OVERLAY:").Select(sourceId => (sourceId, index)))
            .GroupBy(x => x.sourceId, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Select(x => x.index).ToArray(), StringComparer.Ordinal);
        foreach (var overlay in overlayIndex.Entries)
        {
            var source = objectsById[overlay.SourceComponentId]; var owner = objectsById[source.OwnerReconstructionId!];
            var actors = overlayActors.GetValueOrDefault(overlay.SourceComponentId, []);
            if (actors.Length == 0)
            {
                var deletion = owner.Capabilities.SingleOrDefault(capability => capability.Operation == "delete-actor" && capability.State == CapabilityState.Available);
                if (deletion is null || string.IsNullOrEmpty(deletion.SupportRule))
                {
                    diagnostics.Add($"{source.ObjectPath}: deleting this collision overlay has no validated cooked actor deletion rule.");
                    resolved = false;
                    continue;
                }
                if (!operations.Any(operation => operation.Operation == "delete-actor" && operation.ObjectId == owner.ReconstructionId))
                {
                    var original = originals[owner.ReconstructionId];
                    operations.Add(new EditOperation("delete-actor", original.package.PackagePath, owner.ReconstructionId, owner.ObjectPath, original.item.PayloadHash,
                        "$levelActorReference", JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement(false), "editor-collision-overlay", deletion.SupportRule));
                }
                continue;
            }
            // UT4's T3D importer replaces every imported brush name with an ordinal suffix (_0 through
            // _N), even though the requested names are already unique. ActorLabel retains the stable
            // source path and duplicated overlays receive a distinct editor label, so it is the durable
            // identity for the canonical overlay after an editor save.
            var expectedLabel = "[Exact collision overlay] " + overlay.SourceObjectPath;
            var canonical = actors.Where(index =>
            {
                try
                {
                    var label = patcher.ReadValue(asset, index, "ActorLabel");
                    return label.ValueKind == JsonValueKind.String && label.GetString() == expectedLabel;
                }
                catch (KeyNotFoundException) { return false; }
            }).ToArray();
            if (canonical.Length == 0 && actors.Length == 1) canonical = actors;
            if (canonical.Length != 1) { diagnostics.Add($"{source.ObjectPath}: canonical collision overlay resolved to {canonical.Length} actors."); resolved = false; continue; }
            var canonicalComponent = BrushComponent(root, canonical[0], childrenByOuter);
            foreach (var property in source.EditableProperties.Where(x => x.PropertyPath is "RelativeLocation" or "RelativeRotation" or "RelativeScale3D"))
            {
                JsonElement editorValue;
                try { editorValue = patcher.ReadValue(asset, canonicalComponent, property.PropertyPath); }
                catch (KeyNotFoundException) { diagnostics.Add($"{source.ObjectPath}.{property.PropertyPath}: collision overlay omitted a serialized transform."); continue; }
                if (!PropertyPatcher.JsonEquivalent(property.Value, editorValue))
                {
                    var original = originals[source.ReconstructionId];
                    operations.Add(new EditOperation("set-property", original.package.PackagePath, source.ReconstructionId, source.ObjectPath, original.item.PayloadHash,
                        property.PropertyPath, property.Value, editorValue, "editor-collision-overlay", property.SupportRule));
                }
            }
            foreach (var cloneActor in actors.Where(x => x != canonical[0]))
            {
                var componentIndex = BrushComponent(root, cloneActor, childrenByOuter);
                var editorName = exports[cloneActor - 1].GetProperty("ObjectName").GetString()!;
                var name = CollisionCloneActorName(overlay.SourceComponentId, editorName);
                var location = ReadTransform(patcher, asset, componentIndex, "RelativeLocation", new { x = 0.0, y = 0.0, z = 0.0 });
                var rotation = ReadTransform(patcher, asset, componentIndex, "RelativeRotation", new { pitch = 0.0, yaw = 0.0, roll = 0.0 });
                var values = new Dictionary<string, JsonElement> { ["name"] = JsonSerializer.SerializeToElement(name), ["location"] = location, ["rotation"] = rotation };
                var sourceScale = source.EditableProperties.SingleOrDefault(x => x.PropertyPath == "RelativeScale3D");
                var cloneScale = ReadTransform(patcher, asset, componentIndex, "RelativeScale3D", new { x = 1.0, y = 1.0, z = 1.0 });
                if (sourceScale is not null) values["scale"] = cloneScale;
                else if (!PropertyPatcher.JsonEquivalent(cloneScale, JsonSerializer.SerializeToElement(new { x = 1.0, y = 1.0, z = 1.0 })))
                {
                    diagnostics.Add($"{name}: source blocker does not serialize RelativeScale3D; the duplicate's scale cannot be exported."); continue;
                }
                var original = originals[owner.ReconstructionId];
                operations.Add(new EditOperation("clone-actor", original.package.PackagePath, owner.ReconstructionId, owner.ObjectPath, original.item.PayloadHash, "$actorClone",
                    JsonSerializer.SerializeToElement<object?>(null), JsonSerializer.SerializeToElement(values, ReconJson.Options), "editor-collision-overlay", "blocking-volume-closure-clone-v1"));
            }
        }
        return resolved;
    }

    internal static string CollisionCloneActorName(string sourceComponentId, string editorObjectName)
        => RuntimeCloneActorName("Blocker", sourceComponentId, editorObjectName);

    private static string RuntimeCloneActorName(string kind, string sourceId, string editorObjectName)
    {
        // Editor-generated duplicate names can inherit punctuation from the map/package name
        // (for example CTF-Switchback-PRO2). Cooked actor names accept only identifier characters.
        // Hash the stable source identity together with Unreal's unique editor object name so every
        // duplicate gets a deterministic, collision-resistant runtime name without trusting that text.
        var sourceToken = sourceId[..Math.Min(12, sourceId.Length)];
        var cloneToken = Identity.Sha256Bytes(Encoding.UTF8.GetBytes(sourceId + "\n" + editorObjectName))[..12];
        return $"UT4Recon_{kind}_{sourceToken}_{cloneToken}";
    }

    private static int BrushComponent(JsonElement root, int actorIndex)
    {
        var exports = root.GetProperty("Exports"); var matches = Enumerable.Range(1, exports.GetArrayLength()).Where(index => exports[index - 1].GetProperty("OuterIndex").GetInt32() == actorIndex && ResolveIndex(root, exports[index - 1].GetProperty("ClassIndex").GetInt32()) == "/Script/Engine.BrushComponent").ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"Collision overlay actor {actorIndex} has {matches.Length} BrushComponents.");
    }

    private static int BrushComponent(JsonElement root, int actorIndex, IReadOnlyDictionary<int, int[]> childrenByOuter)
    {
        var exports = root.GetProperty("Exports");
        var matches = childrenByOuter.GetValueOrDefault(actorIndex, []).Where(index => ResolveIndex(root, exports[index - 1].GetProperty("ClassIndex").GetInt32()) == "/Script/Engine.BrushComponent").ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidDataException($"Collision overlay actor {actorIndex} has {matches.Length} BrushComponents.");
    }

    private static JsonElement ReadTransform(PropertyPatcher patcher, UAsset asset, int componentIndex, string property, object fallback)
    {
        try { return patcher.ReadValue(asset, componentIndex, property); }
        catch (KeyNotFoundException) { return JsonSerializer.SerializeToElement(fallback); }
    }

    private static IReadOnlyList<string> TagValues(JsonElement export, string prefix)
    {
        var result = new List<string>();
        if (!export.TryGetProperty("Data", out var properties)) return result;
        foreach (var property in properties.EnumerateArray())
        {
            if (property.GetProperty("Name").GetString() is not "Tags" || !property.TryGetProperty("Value", out var values)) continue;
            foreach (var item in values.EnumerateArray())
            {
                var value = item.TryGetProperty("Value", out var inner) ? inner.GetString() : null;
                if (value?.StartsWith(prefix, StringComparison.Ordinal) == true) result.Add(value[prefix.Length..]);
            }
        }
        return result;
    }

    private static string? ObjectReference(JsonElement root, int exportIndex, string propertyName)
    {
        var property = root.GetProperty("Exports")[exportIndex - 1].GetProperty("Data").EnumerateArray().SingleOrDefault(x => x.GetProperty("Name").GetString() == propertyName);
        return property.ValueKind == JsonValueKind.Undefined ? null : ResolveIndex(root, property.GetProperty("Value").GetInt32());
    }

    private static IReadOnlyList<string> ObjectReferences(JsonElement root, int exportIndex, string propertyName)
    {
        var property = root.GetProperty("Exports")[exportIndex - 1].GetProperty("Data").EnumerateArray().SingleOrDefault(x => x.GetProperty("Name").GetString() == propertyName);
        if (property.ValueKind == JsonValueKind.Undefined) return [];
        return property.GetProperty("Value").EnumerateArray().Select(x => NormalizePreviewReference(ResolveIndex(root, x.GetProperty("Value").GetInt32()) ?? "None")).ToArray();
    }
    private static string NormalizePreviewReference(string path) => path.StartsWith("/Game/Maps/", StringComparison.Ordinal) && path.Contains("/Materials/", StringComparison.Ordinal)
        ? "/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial" : path;

    private static string? ResolveIndex(JsonElement root, int index)
    {
        if (index == 0) return null; var item = index > 0 ? root.GetProperty("Exports")[index - 1] : root.GetProperty("Imports")[-index - 1];
        var parent = ResolveIndex(root, item.GetProperty("OuterIndex").GetInt32()); var name = item.GetProperty("ObjectName").GetString()!; return parent is null ? name : parent + "." + name;
    }

    private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException(path);
}
