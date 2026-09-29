using System.Text.Json;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;

namespace Ut4Recon.Package;

public sealed class PackageInspector
{
    public PackageInventory Inspect(string physicalPath, string internalPath)
    {
        var diagnostics = new List<string>();
        try
        {
            var asset = new UAsset(physicalPath, EngineVersion.VER_UE4_15);
            using var document = JsonDocument.Parse(asset.SerializeJson()); var root = document.RootElement;
            var exports = root.GetProperty("Exports"); var imports = root.GetProperty("Imports"); var packagePath = PackagePaths.FromInternalPath(internalPath);
            if (packagePath.StartsWith("/Unknown/", StringComparison.Ordinal)) diagnostics.Add("Package path is outside known UT4 content roots.");
            var objects = new List<PackageObject>(exports.GetArrayLength());
            for (var i = 0; i < exports.GetArrayLength(); i++)
            {
                var export = exports[i]; var objectPath = ResolveIndex(root, i + 1) ?? export.GetProperty("ObjectName").GetString()!;
                var classPath = ResolveIndex(root, export.GetProperty("ClassIndex").GetInt32()); var support = Classify(classPath, objectPath, export);
                var serialSize = export.GetProperty("SerialSize").GetInt64(); var serialOffset = export.GetProperty("SerialOffset").GetInt64();
                var propertyCount = export.TryGetProperty("Data", out var data) && data.ValueKind == JsonValueKind.Array ? data.GetArrayLength() : 0;
                var opaqueBytes = export.TryGetProperty("Extras", out var extras) && extras.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(extras.GetString()) ? Convert.FromBase64String(extras.GetString()!).Length : 0;
                objects.Add(new PackageObject(Identity.ObjectId(packagePath, objectPath, classPath), objectPath, classPath, i + 1, serialSize,
                    HashPayload(physicalPath, serialOffset, serialSize), propertyCount, opaqueBytes, support.Level, support.Reason));
            }
            var dependencies = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var import in imports.EnumerateArray()) if (import.GetProperty("OuterIndex").GetInt32() == 0)
                { var name = import.GetProperty("ObjectName").GetString(); if (name?.StartsWith('/') == true) dependencies.Add(name); }
            var flags = root.GetProperty("PackageFlags").GetString() ?? "PKG_None";
            var objectVersion = root.GetProperty("ObjectVersion").GetString() ?? "UNKNOWN";
            if (!objectVersion.Equals("VER_UE4_64BIT_EXPORTMAP_SERIALSIZES", StringComparison.Ordinal)) diagnostics.Add($"Object version is outside the supported UT4 4.15 profile: {objectVersion}.");
            var customVersions = root.GetProperty("CustomVersionContainer").EnumerateArray().Select(x => new Ut4Recon.Core.CustomVersion(x.GetProperty("Key").GetString() ?? "", x.GetProperty("FriendlyName").GetString() ?? "", x.GetProperty("Version").GetInt32())).OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
            var recorded = root.GetProperty("RecordedEngineVersion"); var recordedVersion = $"{recorded.GetProperty("Major").GetInt32()}.{recorded.GetProperty("Minor").GetInt32()}.{recorded.GetProperty("Patch").GetInt32()}-{recorded.GetProperty("Changelist").GetInt32()}+{recorded.GetProperty("Branch").GetString()}";
            var unavailableAuthoringFields = new SortedSet<string>(StringComparer.Ordinal);
            if (flags.Contains("PKG_ContainsMap", StringComparison.Ordinal))
            {
                unavailableAuthoringFields.Add("originalActorLabelsAndEditorFolderOrganization");
                unavailableAuthoringFields.Add("levelBlueprintSourceGraphCommentsAndLayout");
            }
            if (objects.Any(x => x.ClassPath?.Contains("BlueprintGeneratedClass", StringComparison.Ordinal) == true)) unavailableAuthoringFields.Add("blueprintSourceGraphCommentsAndLayout");
            if (!flags.Contains("PKG_ContainsMap", StringComparison.Ordinal) && objects.Any(x => x.ClassPath?.Contains(".Material", StringComparison.Ordinal) == true)) unavailableAuthoringFields.Add("materialAuthoringGraphCommentsAndLayout");
            return new PackageInventory(internalPath, packagePath, Identity.File(physicalPath, internalPath), [], objectVersion, recordedVersion, flags,
                root.GetProperty("IsFilterEditorOnly").GetBoolean(), flags.Contains("PKG_ContainsMap"), root.GetProperty("NameMap").GetArrayLength(), imports.GetArrayLength(), exports.GetArrayLength(),
                customVersions, objects, dependencies.ToArray(), unavailableAuthoringFields.ToArray(), diagnostics);
        }
        catch (Exception error)
        {
            diagnostics.Add(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.GetType().Name + ": " + error.Message);
            return new PackageInventory(internalPath, PackagePaths.FromInternalPath(internalPath), Identity.File(physicalPath, internalPath), [], "UNREADABLE", "UNKNOWN", "UNKNOWN", false,
                physicalPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase), 0, 0, 0, [], [], [], [], diagnostics);
        }
    }

    private static string HashPayload(string path, long offset, long size)
    {
        var payloadPath = path; var payloadOffset = offset; var mainLength = new FileInfo(path).Length;
        if (offset >= mainLength)
        {
            payloadPath = Path.ChangeExtension(path, ".uexp");
            payloadOffset -= mainLength;
        }
        if (payloadOffset < 0 || size < 0 || !File.Exists(payloadPath) || payloadOffset + size > new FileInfo(payloadPath).Length)
            throw new InvalidDataException("Export payload lies outside the package and its .uexp sidecar");
        using var stream = File.OpenRead(payloadPath); stream.Position = payloadOffset; using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        var remaining = size; var buffer = new byte[81920];
        while (remaining > 0) { var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining)); if (read == 0) throw new EndOfStreamException(); hash.AppendData(buffer, 0, read); remaining -= read; }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string? ResolveIndex(JsonElement root, int index, HashSet<int>? seen = null)
    {
        if (index == 0) return null; seen ??= []; if (!seen.Add(index)) return "<cycle>";
        var value = index > 0 ? root.GetProperty("Exports")[index - 1] : root.GetProperty("Imports")[-index - 1];
        var name = value.GetProperty("ObjectName").GetString()!; var parent = ResolveIndex(root, value.GetProperty("OuterIndex").GetInt32(), seen);
        return parent is null ? name : parent + "." + name;
    }

    private static (SupportLevel Level, string Reason) Classify(string? classPath, string objectPath, JsonElement export)
    {
        var value = classPath ?? "";
        if (value.EndsWith(".Model", StringComparison.Ordinal)) return (SupportLevel.Reconstructable, "UModel geometry can be decoded into a recovered-brush workspace and replaced through the constrained cooked-donor BSP closure graft.");
        if (HasAllowlistedProperty(export)) return (SupportLevel.PropertyEditable, "One or more serialized properties are supported by the direct package writer.");
        if (value.EndsWith(".BodySetup", StringComparison.Ordinal)) return (SupportLevel.Reconstructable, "BodySetup is eligible for the constrained cooked-donor collision graft; the geometry profile must still approve its shapes and payloads.");
        if (value.EndsWith(".StaticMesh", StringComparison.Ordinal)) return (SupportLevel.ProxyOnly, "Native render buffers are decodable and exportable; cooked render replacement is not implemented.");
        if (value.Contains("BlueprintGeneratedClass", StringComparison.Ordinal) || (!value.StartsWith("/Script/", StringComparison.Ordinal) && value.EndsWith("_C", StringComparison.Ordinal))) return (SupportLevel.ProxyOnly, "Compiled Blueprint behavior requires preservation through a proxy.");
        if (value.EndsWith(".Function", StringComparison.Ordinal)) return (SupportLevel.Preserve, "Compiled behavior is immutable in the initial profile.");
        if (value.EndsWith(".Material", StringComparison.Ordinal) || value.Contains(".MaterialInstance", StringComparison.Ordinal) || value.Contains(".MaterialFunction", StringComparison.Ordinal) || objectPath.Contains("MaterialExpression", StringComparison.Ordinal)) return (SupportLevel.Preserve, "Material authoring graphs are not reconstructed.");
        if (value.StartsWith("/Script/", StringComparison.Ordinal)) return (SupportLevel.Preserve, "Native object is inventoried; the property edit allowlist and writer are pending.");
        return (SupportLevel.Preserve, "Unknown or asset-specific data is preserved unchanged.");
    }

    private static bool HasAllowlistedProperty(JsonElement export)
    {
        if (!export.TryGetProperty("Data", out var data) || data.ValueKind != JsonValueKind.Array) return false;
        return ContainsAllowlistedProperty(data, null);
    }

    private static bool ContainsAllowlistedProperty(JsonElement properties, string? parent)
    {
        foreach (var property in properties.EnumerateArray())
        {
            if (property.ValueKind != JsonValueKind.Object) continue;
            if (!property.TryGetProperty("Name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String) continue;
            var name = nameElement.GetString()!; var path = parent is null ? name : parent + "." + name;
            if (EditRules.IsAllowlisted(path)) return true;
            if (path.Equals("BodyInstance.CollisionResponses", StringComparison.Ordinal)) return true;
            if (property.TryGetProperty("Value", out var children) && children.ValueKind == JsonValueKind.Array && ContainsAllowlistedProperty(children, path)) return true;
        }
        return false;
    }
}
