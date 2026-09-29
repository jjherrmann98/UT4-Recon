using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.EditorProtocol;

public sealed class ProxySceneBuilder
{
    public ProxyScene Build(string stateDirectory, ReconstructionIr ir, string editorMapPackagePath, bool visualOnly = false)
    {
        var maps = ir.Packages.Where(x => x.ContainsMap && x.InternalPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (maps.Length != 1) throw new NotSupportedException($"The first editor proxy profile requires exactly one map package; found {maps.Length}.");
        var package = maps[0]; var physicalPath = BaselinePath(stateDirectory, package.InternalPath);
        var asset = new UAsset(physicalPath, EngineVersion.VER_UE4_15);
        var level = asset.Exports.OfType<LevelExport>().SingleOrDefault() ?? throw new InvalidDataException("Map package has no unique persistent level export.");
        var inventory = package.Objects.ToDictionary(x => x.ExportIndex); var actorIndexes = level.Actors.Where(x => x.IsExport()).Select(x => x.Index).ToHashSet();
        var behaviorPath = Path.Combine(stateDirectory, "behavior-baseline.json"); var behavior = File.Exists(behaviorPath) ? System.Text.Json.JsonSerializer.Deserialize<BehaviorSnapshot>(File.ReadAllText(behaviorPath), ReconJson.Options) : new BehaviorAnalyzer().InspectProject(Path.Combine(stateDirectory, "baseline"), ir);
        var behaviorClasses = (behavior?.Packages ?? []).SelectMany(owner => owner.Classes.Select(item => (ClassPath: owner.PackagePath + "." + item.ObjectPath, Package: owner, Class: item))).ToDictionary(x => x.ClassPath, StringComparer.Ordinal);
        var included = new SortedSet<int>();
        foreach (var actorIndex in actorIndexes)
        {
            var actorClass = inventory.GetValueOrDefault(actorIndex)?.ClassPath;
            if (!visualOnly)
            {
                AddDescendants(asset, actorIndex, included);
                continue;
            }
            if (!SkipEditorProxyActor(actorClass) && actorClass is "/Script/Engine.StaticMeshActor" or "/Script/Engine.BlockingVolume")
            {
                AddDescendants(asset, actorIndex, included);
                continue;
            }
            if (IsInertMarkerClass(actorClass))
            {
                included.Add(actorIndex);
                var markerComponents = DirectCapsules(asset, inventory, actorIndex);
                if (markerComponents.Count != 1)
                {
                    included.Remove(actorIndex);
                    continue;
                }
                foreach (var componentIndex in markerComponents)
                    included.Add(componentIndex);
            }
        }
        var patcher = new PropertyPatcher(); var objects = new List<ProxyObject>(); var diagnostics = new List<string>();
        foreach (var index in included)
        {
            if (!inventory.TryGetValue(index, out var item)) continue;
            var export = asset.Exports[index - 1]; var actorOwner = FindActorOwner(asset, index, actorIndexes);
            var ownerId = actorOwner == index ? null : inventory.GetValueOrDefault(actorOwner)?.Id;
            var kind = actorOwner == index ? "actor" : item.ClassPath?.Contains("Component", StringComparison.Ordinal) == true ? "component" : "subobject";
            var markerClass = inventory.GetValueOrDefault(actorOwner)?.ClassPath;
            var playerStartMarker = visualOnly && IsPlayerStart(markerClass);
            var pickupMarker = visualOnly && IsPickup(markerClass);
            var inertMarker = playerStartMarker || pickupMarker;
            IReadOnlyList<ProxyProperty> properties = export is NormalExport ? patcher.ReadEditableProperties(asset, index) : [];
            if (inertMarker)
                properties = kind == "component"
                    ? properties.Where(property => property.PropertyPath is "RelativeLocation" or "RelativeRotation").ToArray()
                    : [];
            ProxyBehavior? guard = null; if (kind == "actor" && item.ClassPath is not null && behaviorClasses.TryGetValue(item.ClassPath, out var compiled)) guard = new ProxyBehavior("preserve-compiled-class-v1", compiled.ClassPath, compiled.Package.PackagePath, compiled.Class.FunctionPaths.Count, compiled.Class.MetadataFingerprint);
            var fidelity = inertMarker ? FidelityCategory.Reconstructed : FidelityFor(item, kind, guard, properties); var capabilities = CapabilitiesFor(asset, item, kind, actorOwner, properties, inertMarker, pickupMarker ? "pickup" : "player-start");
            var collision = CollisionFor(asset, inventory, item, kind, actorOwner, properties); var evidence = EvidenceFor(item, kind, guard, properties, collision);
            var invented = InventedMetadataFor(item, guard, collision).ToList(); var limitations = LimitationsFor(item, guard, collision).ToList();
            if (inertMarker)
            {
                invented.Add(pickupMarker
                    ? "The visible sphere is an inert editor marker generated from the cooked pickup capsule transform."
                    : "The visible cylinder is an inert editor marker generated from the cooked player-start capsule transform.");
                limitations.Add(kind == "actor"
                    ? pickupMarker
                        ? "This marker preserves the original cooked pickup. Deletion, cloning, item type, respawn settings, and runtime pickup behavior edits are blocked."
                        : "This marker preserves the original cooked player start. Deletion, cloning, team changes, and runtime spawn behavior edits are blocked."
                    : "Only location and rotation are captured; marker shape, scale, collision, and capsule dimensions are editor-only.");
            }
            objects.Add(new ProxyObject(item.Id, item.ObjectPath, item.ClassPath, index, ownerId, kind, export.ObjectName.ToString(), item.Support,
                fidelity, evidence, invented, limitations, capabilities, collision, guard, properties));
            if (kind == "actor" && item.Support == SupportLevel.ProxyOnly) diagnostics.Add($"{item.ObjectPath}: compiled behavior is represented by an editor proxy and preserved in the cooked baseline.");
        }
        var markerCount = objects.Count(x => x.Kind == "actor" && IsPlayerStart(x.ClassPath));
        if (markerCount != 0) diagnostics.Add($"{markerCount} cooked player starts are represented by inert transform-only editor markers.");
        var pickupCount = objects.Count(x => x.Kind == "actor" && IsPickup(x.ClassPath));
        if (pickupCount != 0) diagnostics.Add($"{pickupCount} cooked pickups are represented by inert transform-only editor markers.");
        var counts = Enum.GetValues<FidelityCategory>().Select(category => new FidelityCount(category, objects.Count(x => x.Fidelity == category))).ToArray();
        return new ProxyScene(FormatVersions.ProxyScene, ir.Profile, ir.InputPak, Identity.File(Path.Combine(stateDirectory, "reconstruction-ir.json"), "reconstruction-ir.json"),
            package.PackagePath, editorMapPackagePath, counts, objects, diagnostics);
    }

    private static FidelityCategory FidelityFor(PackageObject item, string kind, ProxyBehavior? behavior, IReadOnlyList<ProxyProperty> properties)
    {
        if (behavior is not null) return FidelityCategory.BehavioralProxy;
        if (item.Support == SupportLevel.Reconstructable) return FidelityCategory.Reconstructed;
        if (item.Support == SupportLevel.PropertyEditable && properties.Count != 0) return FidelityCategory.ExactEditable;
        if (kind == "actor" && item.Support is SupportLevel.Preserve or SupportLevel.PropertyEditable && item.ClassPath?.StartsWith("/Script/", StringComparison.Ordinal) == true)
            return FidelityCategory.ExactEditable;
        return FidelityCategory.PreserveOnly;
    }

    private static IReadOnlyList<ProxyCapability> CapabilitiesFor(UAsset asset, PackageObject item, string kind, int actorOwner, IReadOnlyList<ProxyProperty> properties, bool inertMarker, string markerKind)
    {
        var result = properties.Select(property => EditorDiffExporter.CapturesProperty(property.PropertyPath)
            ? new ProxyCapability("set-property", property.PropertyPath, CapabilityState.Available, "editor-protocol-v2", property.SupportRule,
                "Changing this value in the workspace is captured and written to the original cooked export.")
            : new ProxyCapability("set-property", property.PropertyPath, CapabilityState.Available, "cli-manifest", property.SupportRule,
                "A validated cooked writer exists, but the current editor exporter does not capture this Details-panel change; use a direct manifest or external operation.")).ToList();
        result.Add(new ProxyCapability("preserve-cooked-payload", null, CapabilityState.Available, "build", "immutable-baseline-v1", "Unedited cooked bytes remain authoritative."));
        if (kind == "actor")
        {
            var deletionAllowed = !inertMarker && item.Support is not (SupportLevel.Unsupported or SupportLevel.ProxyOnly);
            result.Add(new ProxyCapability("delete-actor", null, deletionAllowed ? CapabilityState.Available : CapabilityState.Blocked, "editor-protocol-v2",
                deletionAllowed ? "persistent-level-actor-delete-v1" : null, deletionAllowed ? "Deletes only the persistent-level reference and preserves the original closure." :
                inertMarker ? $"{markerKind} deletion is outside the transform-only marker profile." : "This actor has no validated deletion profile."));
            if (item.ClassPath == "/Script/Engine.StaticMeshActor")
            {
                var descendants = Descendants(asset, actorOwner); var valid = descendants.Count == 1 && asset.Exports[descendants[0] - 1].GetExportClassType().ToString().Contains("StaticMeshComponent", StringComparison.Ordinal);
                result.Add(new ProxyCapability("clone-as-template", null, valid ? CapabilityState.Available : CapabilityState.Blocked, "editor-protocol-v2",
                    valid ? "simple-static-mesh-actor-clone-v1" : null, valid ? "A new actor may reuse this exact two-export cooked closure." : "The actor closure is outside the simple clone profile."));
            }
            else if (item.ClassPath == "/Script/Engine.BlockingVolume")
                result.Add(new ProxyCapability("clone-as-template", null, CapabilityState.Available, "cli-manifest", "blocking-volume-closure-clone-v1",
                    "The cooked closure can be cloned; editor-authored size/scale export is the next milestone slice."));
        }
        return result;
    }

    private static ProxyCollision? CollisionFor(UAsset asset, IReadOnlyDictionary<int, PackageObject> inventory, PackageObject item, string kind, int actorOwner, IReadOnlyList<ProxyProperty> properties)
    {
        var editable = properties.Where(x => x.SupportRule.Contains("collision", StringComparison.Ordinal)).Select(x => x.PropertyPath).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var className = item.ClassPath?.Split('.').Last() ?? "";
        if (className is "BoxComponent" or "SphereComponent" or "CapsuleComponent")
            return new ProxyCollision("serialized-primitive", "exact-cooked-properties", item.Id, "native-editor-component", editable,
                "Only serialized dimensions and responses listed as capabilities can be exported.");
        if (className == "BrushComponent")
        {
            var model = Descendants(asset, actorOwner).Select(index => inventory.GetValueOrDefault(index)).SingleOrDefault(x => x?.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) == true);
            return new ProxyCollision("cooked-u-model", "exact-runtime-arrays", model?.Id, "decoded-not-yet-overlaid", editable,
                "The cooked collision geometry exists, but protocol v2 does not yet import its selectable overlay into the editor.");
        }
        if (className == "StaticMeshComponent")
            return new ProxyCollision("referenced-static-mesh-body-setup", "reference-preserved-geometry-not-bound", null, "editor-resolved-or-placeholder", editable,
                "Collision belongs to the referenced cooked StaticMesh; this scene object does not yet bind its decoded BodySetup workspace.");
        return null;
    }

    private static IReadOnlyList<string> EvidenceFor(PackageObject item, string kind, ProxyBehavior? behavior, IReadOnlyList<ProxyProperty> properties, ProxyCollision? collision)
    {
        var result = new List<string> { $"Cooked export {item.ExportIndex} payload SHA-256 {item.PayloadHash}.", item.SupportReason };
        if (properties.Count != 0) result.Add($"{properties.Count} serialized properties have direct writer rules.");
        if (behavior is not null) result.Add($"Compiled behavior fingerprint {behavior.MetadataFingerprint} is guarded by {behavior.Policy}.");
        if (collision is not null) result.Add($"Collision source: {collision.SourceKind}; geometry fidelity: {collision.GeometryFidelity}.");
        if (kind == "actor") result.Add("Actor occurs in the cooked persistent-level actor list.");
        return result;
    }

    private static IReadOnlyList<string> InventedMetadataFor(PackageObject item, ProxyBehavior? behavior, ProxyCollision? collision)
    {
        var result = new List<string> { "Editor ActorLabel and folder placement are generated because original editor organization was cooked out." };
        if (behavior is not null) result.Add("The native proxy class and warning label are editor-only substitutes for the unavailable Blueprint source graph.");
        if (collision?.Visualization is "editor-resolved-or-placeholder") result.Add("Viewport collision appearance may come from an installed editor asset or placeholder rather than decoded cooked collision.");
        return result;
    }

    private static IReadOnlyList<string> LimitationsFor(PackageObject item, ProxyBehavior? behavior, ProxyCollision? collision)
    {
        var result = new List<string>();
        if (item.OpaqueNativeBytes != 0) result.Add($"{item.OpaqueNativeBytes} opaque native bytes remain preserve-only outside a declared closure graft.");
        if (behavior is not null) result.Add("Blueprint graphs, comments, construction source, and arbitrary behavior edits are unavailable; compiled behavior remains immutable.");
        if (item.Support is SupportLevel.ProxyOnly or SupportLevel.Unsupported) result.Add("No general edit operation is supported for this export.");
        if (collision is not null && collision.Visualization != "native-editor-component") result.Add(collision.Limitation);
        return result;
    }

    private static IReadOnlyList<int> Descendants(UAsset asset, int parent)
    {
        var result = new List<int>(); var pending = new Queue<int>(); pending.Enqueue(parent);
        while (pending.Count != 0)
        {
            var owner = pending.Dequeue();
            for (var i = 0; i < asset.Exports.Count; i++) if (asset.Exports[i].OuterIndex.Index == owner) { result.Add(i + 1); pending.Enqueue(i + 1); }
        }
        return result;
    }

    private static void AddDescendants(UAsset asset, int parent, ISet<int> result)
    {
        if (!result.Add(parent)) return;
        for (var i = 0; i < asset.Exports.Count; i++) if (asset.Exports[i].OuterIndex.Index == parent) AddDescendants(asset, i + 1, result);
    }

    private static bool SkipEditorProxyActor(string? classPath) => classPath is
        "/Script/Engine.AbstractNavData" or
        "/Script/Foliage.InstancedFoliageActor" or
        "/Script/UnrealTournament.UTRecastNavMesh" or
        "/Script/UnrealTournament.UTWorldSettings";

    internal static bool IsPlayerStart(string? classPath) => classPath is
        "/Script/Engine.PlayerStart" or
        "/Script/UnrealTournament.UTPlayerStart" or
        "/Script/UnrealTournament.UTTeamPlayerStart";

    internal static bool IsPickup(string? classPath)
    {
        if (classPath is null) return false;
        if (classPath.StartsWith("/Script/UnrealTournament.UTPickup", StringComparison.Ordinal)) return true;
        if (classPath.Contains("/Pickups/", StringComparison.Ordinal) || classPath.Contains("AmmoPickup.", StringComparison.Ordinal) || classPath.Contains("SkillTokenPickup.", StringComparison.Ordinal)) return true;
        return classPath.EndsWith("/Weapons/WeaponBase.WeaponBase_C", StringComparison.Ordinal);
    }

    private static bool IsInertMarkerClass(string? classPath) => IsPlayerStart(classPath) || IsPickup(classPath);

    private static IReadOnlyList<int> DirectCapsules(UAsset asset, IReadOnlyDictionary<int, PackageObject> inventory, int actorIndex) =>
        Enumerable.Range(1, asset.Exports.Count)
            .Where(index => asset.Exports[index - 1].OuterIndex.Index == actorIndex && inventory.GetValueOrDefault(index)?.ClassPath == "/Script/Engine.CapsuleComponent")
            .ToArray();

    private static int FindActorOwner(UAsset asset, int index, IReadOnlySet<int> actorIndexes)
    {
        var current = index; var seen = new HashSet<int>();
        while (current > 0 && seen.Add(current))
        {
            if (actorIndexes.Contains(current)) return current;
            current = asset.Exports[current - 1].OuterIndex.Index;
        }
        throw new InvalidDataException($"Export {index} is not owned by a persistent-level actor.");
    }

    private static string BaselinePath(string stateDirectory, string internalPath)
    {
        var root = Path.GetFullPath(Path.Combine(stateDirectory, "baseline"));
        var path = Path.GetFullPath(Path.Combine(root, internalPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package path escapes baseline.");
        return path;
    }
}
