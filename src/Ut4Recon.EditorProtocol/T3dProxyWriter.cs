using System.Globalization;
using System.Text;
using System.Text.Json;
using Ut4Recon.Core;

namespace Ut4Recon.EditorProtocol;

public sealed class T3dProxyWriter
{
    private static readonly HashSet<string> SkippedProperties = new(StringComparer.Ordinal)
    {
        "ActorLabel", "Tags", "ComponentTags", "LightGuid", "LightingGuid", "VisibilityId", "MaterialStreamingRelativeBoxes",
        "UCSModifiedProperties", "OldPosition", "GoodSprite", "BadSprite", "LevelSummary", "BlueprintCreatedComponents",
        "CreationMethod", "bNetAddressable", "TextureStreamingResourceGuids"
    };

    public void Write(string stateDirectory, ReconstructionIr ir, ProxyScene scene, string outputPath)
    {
        var package = ir.Packages.Single(x => x.PackagePath == scene.MapPackagePath);
        var path = BaselinePath(stateDirectory, package.InternalPath);
        using var document = JsonDocument.Parse(new UAssetAPI.UAsset(path, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15).SerializeJson());
        var root = document.RootElement; var exports = root.GetProperty("Exports"); var byIndex = scene.Objects.ToDictionary(x => x.ExportIndex);
        var templateRoots = LoadTemplateRoots(stateDirectory, ir);
        var mapRoot = ResolveIndex(root, exports.EnumerateArray().Select((x, i) => (x, i)).Single(x => x.x.TryGetProperty("Actors", out _)).i + 1)!;
        var lines = new List<string> { $"Begin Map Name={scene.EditorMapPackagePath}", "   Begin Level NAME=PersistentLevel" };
        foreach (var actor in scene.Objects.Where(x => x.Kind == "actor").OrderBy(x => x.ExportIndex))
        {
            if (actor.ClassPath is "/Script/Engine.AbstractNavData" or "/Script/Foliage.InstancedFoliageActor" or "/Script/UnrealTournament.UTRecastNavMesh" or "/Script/UnrealTournament.UTWorldSettings") continue;
            if (actor.ClassPath == "/Script/Engine.BlockingVolume" && scene.Objects.Any(x => x.OwnerReconstructionId == actor.ReconstructionId && x.Collision?.Visualization == "exact-overlay-t3d")) continue;
            var markerChildren = scene.Objects.Where(x => x.OwnerReconstructionId == actor.ReconstructionId).ToArray();
            if (actor.Fidelity == FidelityCategory.Reconstructed && markerChildren.Length == 1 && markerChildren[0].ClassPath == "/Script/Engine.CapsuleComponent" &&
                (ProxySceneBuilder.IsPlayerStart(actor.ClassPath) || ProxySceneBuilder.IsPickup(actor.ClassPath)))
            {
                WriteInertGameplayMarker(lines, actor, markerChildren, ProxySceneBuilder.IsPickup(actor.ClassPath));
                continue;
            }
            var actorClass = IsUnavailableCustomClass(actor.ClassPath) ? "/Script/Engine.Actor" : actor.ClassPath ?? "/Script/Engine.Actor";
            lines.Add($"      Begin Actor Class={actorClass} Name={Quote(actor.EditorName)}");
            var children = scene.Objects.Where(x => x.OwnerReconstructionId == actor.ReconstructionId && x.Kind == "component").OrderBy(x => x.ExportIndex).ToArray();
            foreach (var child in children)
            {
                lines.Add($"         Begin Object Class={child.ClassPath} Name={Quote(child.EditorName)}");
                lines.Add("         End Object");
            }
            foreach (var child in children)
            {
                lines.Add($"         Begin Object Name={Quote(child.EditorName)}");
                lines.Add($"            ComponentTags(0)={Quote("UT4RECON:" + child.ReconstructionId)}");
                lines.Add($"            ComponentTags(1)={Quote("UT4RECON_FIDELITY:" + FidelityToken(child.Fidelity))}");
                var childExport = exports[child.ExportIndex - 1];
                var properties = childExport.GetProperty("Data");
                if (IsUnavailableCustomClass(actor.ClassPath) && TryTemplateData(actor.ClassPath!, child.EditorName, templateRoots, out var templateRoot, out var templateData))
                {
                    foreach (var propertyLine in PropertyLines(templateRoot, MissingTemplateProperties(templateData, properties), "            ", mapRoot)) lines.Add(propertyLine);
                }
                foreach (var propertyLine in PropertyLines(root, properties, "            ", mapRoot)) lines.Add(propertyLine);
                if (IsUnavailableCustomClass(actor.ClassPath)) lines.Add("            CreationMethod=Instance");
                lines.Add("         End Object");
            }
            var actorExport = exports[actor.ExportIndex - 1];
            foreach (var propertyLine in PropertyLines(root, actorExport.GetProperty("Data"), "         ", mapRoot)) lines.Add(propertyLine);
            lines.Add($"         Tags(0)={Quote("UT4RECON:" + actor.ReconstructionId)}");
            lines.Add($"         Tags(1)={Quote("UT4RECON_FIDELITY:" + FidelityToken(actor.Fidelity))}");
            if (actor.Behavior is not null) lines.Add($"         Tags(2)={Quote("UT4RECON_BEHAVIOR_PRESERVED")}");
            lines.Add($"         ActorLabel={Quote("[" + FidelityLabel(actor.Fidelity) + "] " + actor.EditorName)}");
            if (IsUnavailableCustomClass(actor.ClassPath))
                for (var i = 0; i < children.Length; i++) lines.Add($"         InstanceComponents({i})=Object'{actor.EditorName}.{children[i].EditorName}'");
            lines.Add("      End Actor");
        }
        lines.AddRange(["   End Level", "Begin Surface", "End Surface", "End Map"]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static void WriteInertGameplayMarker(List<string> lines, ProxyObject actor, IReadOnlyList<ProxyObject> children, bool pickup)
    {
        var component = children.SingleOrDefault(x => x.ClassPath == "/Script/Engine.CapsuleComponent")
            ?? throw new InvalidDataException($"Gameplay marker {actor.ObjectPath} has no unique cooked CapsuleComponent.");
        var location = component.EditableProperties.SingleOrDefault(x => x.PropertyPath == "RelativeLocation")?.Value;
        var rotation = component.EditableProperties.SingleOrDefault(x => x.PropertyPath == "RelativeRotation")?.Value;
        lines.Add($"      Begin Actor Class=/Script/Engine.StaticMeshActor Name={Quote(actor.EditorName)}");
        lines.Add($"         Begin Object Class=/Script/Engine.StaticMeshComponent Name={Quote(component.EditorName)}");
        lines.Add("         End Object");
        lines.Add($"         Begin Object Name={Quote(component.EditorName)}");
        lines.Add($"            ComponentTags(0)={Quote("UT4RECON:" + component.ReconstructionId)}");
        lines.Add("            ComponentTags(1)=\"UT4RECON_FIDELITY:RECONSTRUCTED\"");
        lines.Add($"            ComponentTags(2)=\"{(pickup ? "UT4RECON_PICKUP_MARKER" : "UT4RECON_PLAYER_START_MARKER")}\"");
        lines.Add($"            StaticMesh=StaticMesh'/Engine/BasicShapes/{(pickup ? "Sphere.Sphere" : "Cylinder.Cylinder")}'");
        lines.Add("            OverrideMaterials(0)=Material'/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial'");
        lines.Add("            BodyInstance=(CollisionEnabled=NoCollision)");
        lines.Add(pickup ? "            RelativeScale3D=(X=0.35,Y=0.35,Z=0.35)" : "            RelativeScale3D=(X=0.25,Y=0.25,Z=0.8)");
        if (location is JsonElement locationValue) lines.Add($"            RelativeLocation={Transform(locationValue, false)}");
        if (rotation is JsonElement rotationValue) lines.Add($"            RelativeRotation={Transform(rotationValue, true)}");
        lines.Add("         End Object");
        lines.Add($"         Tags(0)={Quote("UT4RECON:" + actor.ReconstructionId)}");
        lines.Add("         Tags(1)=\"UT4RECON_FIDELITY:RECONSTRUCTED\"");
        lines.Add($"         Tags(2)=\"{(pickup ? "UT4RECON_PICKUP_MARKER" : "UT4RECON_PLAYER_START_MARKER")}\"");
        lines.Add("         bActorEnableCollision=False");
        lines.Add($"         ActorLabel={Quote((pickup ? "[Pickup marker; move/rotate only] " : "[Player start marker; move/rotate only] ") + actor.EditorName)}");
        lines.Add($"         StaticMeshComponent=StaticMeshComponent'{component.EditorName}'");
        lines.Add($"         RootComponent=StaticMeshComponent'{component.EditorName}'");
        lines.Add("      End Actor");
    }

    private static string Transform(JsonElement value, bool rotation)
    {
        var names = rotation ? new[] { "pitch", "yaw", "roll" } : new[] { "x", "y", "z" };
        return "(" + string.Join(',', names.Select(name => char.ToUpperInvariant(name[0]) + name[1..] + "=" + Number(value.GetProperty(name)))) + ")";
    }

    private static IEnumerable<string> PropertyLines(JsonElement root, JsonElement properties, string indent, string mapRoot)
    {
        foreach (var property in properties.EnumerateArray())
        {
            var name = property.GetProperty("Name").GetString()!;
            if (SkippedProperties.Contains(name)) continue;
            string value;
            try { value = Value(root, property, mapRoot); }
            catch (NotSupportedException) { continue; }
            if (Kind(property) == "ArrayPropertyData")
            {
                var index = 0;
                foreach (var item in property.GetProperty("Value").EnumerateArray()) yield return $"{indent}{name}({index++})={Value(root, item, mapRoot)}";
            }
            else yield return $"{indent}{name}={value}";
        }
    }

    private static string Value(JsonElement root, JsonElement property, string mapRoot)
    {
        var kind = Kind(property); var value = property.TryGetProperty("Value", out var item) ? item : default;
        if (kind == "ObjectPropertyData") return Reference(root, value.GetInt32(), mapRoot);
        if (kind == "BoolPropertyData") return value.GetBoolean() ? "True" : "False";
        if (kind == "BytePropertyData") return property.TryGetProperty("EnumValue", out var enumValue) && enumValue.ValueKind == JsonValueKind.String ? enumValue.GetString()!.Split("::")[^1] : value.GetRawText();
        if (kind == "EnumPropertyData") return value.GetString()!.Split("::")[^1];
        if (kind is "StrPropertyData" or "NamePropertyData") return Quote(value.GetString() ?? "");
        if (kind == "StructPropertyData")
        {
            var children = value.EnumerateArray().ToArray();
            if (children.Length == 1 && Kind(children[0]) is "VectorPropertyData" or "RotatorPropertyData" or "ColorPropertyData" or "LinearColorPropertyData" or "GuidPropertyData") return Value(root, children[0], mapRoot);
            return "(" + string.Join(',', children.Select(x => x.GetProperty("Name").GetString() + "=" + Value(root, x, mapRoot))) + ")";
        }
        if (kind is "VectorPropertyData" or "RotatorPropertyData" or "LinearColorPropertyData")
            return "(" + string.Join(',', value.EnumerateObject().Where(x => !x.Name.StartsWith('$')).Select(x => x.Name.ToUpperInvariant() + "=" + Number(x.Value))) + ")";
        if (kind == "ColorPropertyData")
        {
            var parts = value.GetString()!.Split(',').Select(x => int.Parse(x.Trim(), CultureInfo.InvariantCulture)).ToList(); if (parts.Count == 3) parts.Insert(0, 255);
            return "(" + string.Join(',', new[] { "A", "R", "G", "B" }.Zip(parts).Select(x => x.First + "=" + x.Second)) + ")";
        }
        if (kind == "GuidPropertyData")
        {
            var raw = value.GetString()!.Replace("{", "").Replace("}", "").Replace("-", "");
            return "(" + string.Join(',', "ABCD".Select((name, i) => name + "=" + Convert.ToUInt32(raw.Substring(i * 8, 8), 16))) + ")";
        }
        if (kind == "ArrayPropertyData") return "(" + string.Join(',', value.EnumerateArray().Select(x => Value(root, x, mapRoot))) + ")";
        if (kind == "MulticastDelegatePropertyData") throw new NotSupportedException();
        if (value.ValueKind == JsonValueKind.Number) return Number(value);
        if (value.ValueKind == JsonValueKind.String && value.GetString() is "+0" or "-0") return "0";
        throw new NotSupportedException(kind);
    }

    private static string Number(JsonElement value) => value.ValueKind == JsonValueKind.String && value.GetString() is "+0" or "-0" ? "0" : value.GetRawText();
    private static string Kind(JsonElement value) => value.GetProperty("$type").GetString()!.Split(',')[0].Split('.')[^1];
    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static string FidelityToken(FidelityCategory fidelity) => fidelity.ToString().ToUpperInvariant();
    private static string FidelityLabel(FidelityCategory fidelity) => fidelity switch
    {
        FidelityCategory.ExactEditable => "Exact editable",
        FidelityCategory.Reconstructed => "Reconstructed",
        FidelityCategory.BehavioralProxy => "Behavior preserved; proxy",
        _ => "Preserve only"
    };

    private static string Reference(JsonElement root, int index, string mapRoot)
    {
        if (index == 0) return "None";
        var path = ResolveIndex(root, index)!;
        if (path.StartsWith("/Game/Maps/", StringComparison.Ordinal) && path.Contains("/Materials/", StringComparison.Ordinal))
            path = "/Engine/EngineMaterials/WorldGridMaterial.WorldGridMaterial";
        var prefix = mapRoot + ".";
        if (path.StartsWith(prefix, StringComparison.Ordinal)) path = path[prefix.Length..];
        return "Object'" + path + "'";
    }

    private static bool IsUnavailableCustomClass(string? classPath) => classPath?.StartsWith("/Game/Maps/", StringComparison.Ordinal) == true;

    private static Dictionary<string, JsonElement> LoadTemplateRoots(string stateDirectory, ReconstructionIr ir)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var package in ir.Packages.Where(x => !x.ContainsMap))
        {
            try
            {
                using var document = JsonDocument.Parse(new UAssetAPI.UAsset(BaselinePath(stateDirectory, package.InternalPath), UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15).SerializeJson());
                result[package.PackagePath] = document.RootElement.Clone();
            }
            catch { }
        }
        return result;
    }

    private static bool TryTemplateData(string classPath, string componentName, IReadOnlyDictionary<string, JsonElement> roots, out JsonElement root, out JsonElement data)
    {
        var separator = classPath.LastIndexOf('.'); var packagePath = separator > 0 ? classPath[..separator] : classPath;
        if (roots.TryGetValue(packagePath, out root))
        {
            foreach (var export in root.GetProperty("Exports").EnumerateArray())
                if (export.GetProperty("ObjectName").GetString() == componentName + "_GEN_VARIABLE" && export.TryGetProperty("Data", out data)) return true;
        }
        root = default; data = default; return false;
    }

    private static JsonElement MissingTemplateProperties(JsonElement template, JsonElement instance)
    {
        var names = instance.EnumerateArray().Select(x => x.GetProperty("Name").GetString()).ToHashSet(StringComparer.Ordinal);
        return JsonSerializer.SerializeToElement(template.EnumerateArray().Where(x => !names.Contains(x.GetProperty("Name").GetString())).Select(x => x.Clone()).ToArray());
    }

    private static string? ResolveIndex(JsonElement root, int index)
    {
        if (index == 0) return null;
        var value = index > 0 ? root.GetProperty("Exports")[index - 1] : root.GetProperty("Imports")[-index - 1];
        var parent = ResolveIndex(root, value.GetProperty("OuterIndex").GetInt32()); var name = value.GetProperty("ObjectName").GetString()!;
        return parent is null ? name : parent + "." + name;
    }

    private static string BaselinePath(string stateDirectory, string internalPath) => Path.Combine(stateDirectory, "baseline", internalPath.Replace('/', Path.DirectorySeparatorChar));
}
