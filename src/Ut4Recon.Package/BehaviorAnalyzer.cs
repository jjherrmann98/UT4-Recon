using System.Text;
using System.Text.Json;
using Ut4Recon.Core;

namespace Ut4Recon.Package;

public sealed class BehaviorAnalyzer
{
    public BehaviorSnapshot InspectProject(string packageRoot, ReconstructionIr ir)
    {
        packageRoot = Path.GetFullPath(packageRoot); var packages = new List<BehaviorPackage>(); var diagnostics = new List<string>();
        foreach (var package in ir.Packages)
        {
            var path = Path.GetFullPath(Path.Combine(packageRoot, package.InternalPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(packageRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) { diagnostics.Add($"{package.InternalPath}: package path escapes the supplied root."); continue; }
            try { var behavior = InspectPackage(path, package.InternalPath); if (behavior.Classes.Count != 0 || behavior.Functions.Count != 0) packages.Add(behavior); }
            catch (Exception error) { diagnostics.Add($"{package.InternalPath}: {error.GetType().Name}: {error.Message}"); }
        }
        return new BehaviorSnapshot(FormatVersions.BehaviorSnapshot, ir.Profile, packages.OrderBy(x => x.InternalPath, StringComparer.Ordinal).ToArray(), diagnostics);
    }

    public BehaviorPackage InspectPackage(string physicalPath, string internalPath)
    {
        physicalPath = Path.GetFullPath(physicalPath); var inventory = new PackageInspector().Inspect(physicalPath, internalPath);
        if (inventory.Diagnostics.Count != 0) throw new InvalidDataException(string.Join("; ", inventory.Diagnostics));
        var asset = new UAssetAPI.UAsset(physicalPath, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15);
        using var document = JsonDocument.Parse(asset.SerializeJson()); var root = document.RootElement; var exports = root.GetProperty("Exports");
        var functions = new List<BehaviorFunction>();
        for (var i = 0; i < exports.GetArrayLength(); i++)
        {
            var export = exports[i]; if (!TypeName(export).EndsWith("FunctionExport", StringComparison.Ordinal)) continue;
            var objectInfo = inventory.Objects[i]; var bytecode = export.GetProperty("ScriptBytecode"); var opcodes = new SortedDictionary<string, int>(StringComparer.Ordinal); var calls = new SortedSet<string>(StringComparer.Ordinal);
            Visit(bytecode, element =>
            {
                if (element.ValueKind != JsonValueKind.Object) return; var type = TypeName(element); var marker = type.LastIndexOf(".EX_", StringComparison.Ordinal);
                if (marker >= 0) { var opcode = type[(marker + 1)..]; opcodes[opcode] = opcodes.GetValueOrDefault(opcode) + 1; }
                if (element.TryGetProperty("StackNode", out var stack) && stack.ValueKind == JsonValueKind.Number) { var path = ResolveIndex(root, stack.GetInt32()); if (path is not null) calls.Add(path); }
                if (element.TryGetProperty("VirtualFunctionName", out var virtualName) && virtualName.ValueKind == JsonValueKind.String) calls.Add("virtual:" + virtualName.GetString());
            });
            functions.Add(new BehaviorFunction(objectInfo.ObjectPath, i + 1, export.GetProperty("ScriptBytecodeSize").GetInt32(), Hash(bytecode.GetRawText()), objectInfo.PayloadHash, opcodes, calls.ToArray()));
        }

        var classes = new List<BehaviorClass>();
        for (var i = 0; i < exports.GetArrayLength(); i++)
        {
            var export = exports[i]; if (!TypeName(export).EndsWith("ClassExport", StringComparison.Ordinal)) continue;
            var flags = export.GetProperty("ClassFlags").GetString() ?? ""; var objectInfo = inventory.Objects[i];
            if (!flags.Contains("CLASS_CompiledFromBlueprint", StringComparison.Ordinal) && !objectInfo.ObjectPath.EndsWith("_C", StringComparison.Ordinal)) continue;
            var classIndex = i + 1; var defaultIndex = export.GetProperty("ClassDefaultObject").GetInt32(); var owned = inventory.Objects.Where(x => x.ExportIndex != classIndex && (IsOwnedBy(root, x.ExportIndex, classIndex) || x.ExportIndex == defaultIndex || IsOwnedBy(root, x.ExportIndex, defaultIndex)))
                .Select(x => new BehaviorOwnedObject(x.ObjectPath, x.ClassPath, x.ExportIndex, x.PayloadHash)).OrderBy(x => x.ObjectPath, StringComparer.Ordinal).ToArray();
            var functionPaths = functions.Where(x => IsOwnedBy(root, x.ExportIndex, classIndex)).Select(x => x.ObjectPath).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var delegateValues = new List<string>(); foreach (var member in owned.Prepend(new BehaviorOwnedObject(objectInfo.ObjectPath, objectInfo.ClassPath, classIndex, objectInfo.PayloadHash)))
                Visit(exports[member.ExportIndex - 1], value => { if (value.ValueKind == JsonValueKind.Object && TypeName(value).Contains("Delegate", StringComparison.Ordinal)) delegateValues.Add(value.GetRawText()); });
            var metadata = new
            {
                classFlags = flags,
                classWithin = export.GetProperty("ClassWithin"), classConfigName = export.GetProperty("ClassConfigName"), interfaces = export.GetProperty("Interfaces"),
                classGeneratedBy = export.GetProperty("ClassGeneratedBy"), cooked = export.GetProperty("bCooked"), classDefaultObject = defaultIndex,
                superStruct = export.GetProperty("SuperStruct"), children = export.GetProperty("Children"), functionMap = export.GetProperty("FuncMap"), data = export.GetProperty("Data")
            };
            classes.Add(new BehaviorClass(objectInfo.ObjectPath, classIndex, ResolveIndex(root, export.GetProperty("SuperStruct").GetInt32()), ResolveIndex(root, defaultIndex),
                Hash(JsonSerializer.Serialize(metadata, ReconJson.Options)), inventory.Objects.SingleOrDefault(x => x.ExportIndex == defaultIndex)?.PayloadHash,
                Hash(string.Join("\n", delegateValues.OrderBy(x => x, StringComparer.Ordinal))), functionPaths, owned));
        }
        var dependencies = inventory.ImportPackages.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return new BehaviorPackage(internalPath, inventory.PackagePath, inventory.File.Sha256, dependencies, Hash(root.GetProperty("Imports").GetRawText()), classes.OrderBy(x => x.ObjectPath, StringComparer.Ordinal).ToArray(), functions.OrderBy(x => x.ObjectPath, StringComparer.Ordinal).ToArray());
    }

    private static bool IsOwnedBy(JsonElement root, int index, int owner)
    {
        if (owner <= 0) return false; var seen = new HashSet<int>(); var exports = root.GetProperty("Exports");
        while (index > 0 && index <= exports.GetArrayLength() && seen.Add(index)) { index = exports[index - 1].GetProperty("OuterIndex").GetInt32(); if (index == owner) return true; }
        return false;
    }

    private static string? ResolveIndex(JsonElement root, int index, HashSet<int>? seen = null)
    {
        if (index == 0) return null; seen ??= []; if (!seen.Add(index)) return "<cycle>";
        var values = index > 0 ? root.GetProperty("Exports") : root.GetProperty("Imports"); var at = index > 0 ? index - 1 : -index - 1; if (at < 0 || at >= values.GetArrayLength()) return $"<invalid:{index}>";
        var value = values[at]; var name = value.GetProperty("ObjectName").GetString()!; var parent = ResolveIndex(root, value.GetProperty("OuterIndex").GetInt32(), seen); return parent is null ? name : parent + "." + name;
    }

    private static string TypeName(JsonElement value) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("$type", out var type) ? type.GetString()?.Split(',')[0] ?? "" : "";
    private static string Hash(string value) => Identity.Sha256Bytes(Encoding.UTF8.GetBytes(value));
    private static void Visit(JsonElement value, Action<JsonElement> visitor) { visitor(value); if (value.ValueKind == JsonValueKind.Array) foreach (var child in value.EnumerateArray()) Visit(child, visitor); else if (value.ValueKind == JsonValueKind.Object) foreach (var property in value.EnumerateObject()) Visit(property.Value, visitor); }
}

public sealed class BehaviorValidator
{
    public BehaviorValidationReport Compare(BehaviorSnapshot expected, BehaviorSnapshot candidate,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? allowedDependencyAdditions = null)
    {
        var differences = new List<string>(); var packageCount = 0; var classCount = 0; var functionCount = 0;
        if (expected.Profile != candidate.Profile) differences.Add($"Profile changed from {expected.Profile} to {candidate.Profile}.");
        foreach (var diagnostic in expected.Diagnostics.Except(candidate.Diagnostics, StringComparer.Ordinal)) differences.Add("Baseline-only diagnostic: " + diagnostic);
        foreach (var diagnostic in candidate.Diagnostics.Except(expected.Diagnostics, StringComparer.Ordinal)) differences.Add("Candidate-only diagnostic: " + diagnostic);
        var expectedPackages = expected.Packages.ToDictionary(x => x.InternalPath, StringComparer.Ordinal); var candidatePackages = candidate.Packages.ToDictionary(x => x.InternalPath, StringComparer.Ordinal);
        foreach (var path in expectedPackages.Keys.Except(candidatePackages.Keys, StringComparer.Ordinal)) differences.Add("Behavior package disappeared: " + path);
        foreach (var path in candidatePackages.Keys.Except(expectedPackages.Keys, StringComparer.Ordinal)) differences.Add("Behavior package appeared: " + path);
        foreach (var (path, left) in expectedPackages)
        {
            if (!candidatePackages.TryGetValue(path, out var right)) continue; var packageOkay = true;
            if (left.PackagePath != right.PackagePath) { differences.Add("Behavior package path changed: " + path); packageOkay = false; }
            if (left.DependencyFingerprint != right.DependencyFingerprint)
            {
                var removed = left.ImportPackages.Except(right.ImportPackages, StringComparer.Ordinal).ToArray();
                var added = right.ImportPackages.Except(left.ImportPackages, StringComparer.Ordinal).ToArray();
                var permitted = allowedDependencyAdditions is not null && allowedDependencyAdditions.TryGetValue(path, out var allowed) &&
                    removed.Length == 0 && added.All(allowed.Contains);
                if (!permitted) { differences.Add("Behavior package dependencies changed: " + path); packageOkay = false; }
            }
            var leftClasses = left.Classes.ToDictionary(x => x.ObjectPath, StringComparer.Ordinal); var rightClasses = right.Classes.ToDictionary(x => x.ObjectPath, StringComparer.Ordinal);
            foreach (var name in leftClasses.Keys.Union(rightClasses.Keys, StringComparer.Ordinal))
            {
                if (!leftClasses.TryGetValue(name, out var a) || !rightClasses.TryGetValue(name, out var b)) { differences.Add("Generated class set changed: " + name); packageOkay = false; continue; }
                if (Canonical(a) != Canonical(b)) { differences.Add("Generated class metadata/default/template/delegate closure changed: " + name); packageOkay = false; } else classCount++;
            }
            var leftFunctions = left.Functions.ToDictionary(x => x.ObjectPath, StringComparer.Ordinal); var rightFunctions = right.Functions.ToDictionary(x => x.ObjectPath, StringComparer.Ordinal);
            foreach (var name in leftFunctions.Keys.Union(rightFunctions.Keys, StringComparer.Ordinal))
            {
                if (!leftFunctions.TryGetValue(name, out var a) || !rightFunctions.TryGetValue(name, out var b)) { differences.Add("Function set changed: " + name); packageOkay = false; continue; }
                if (Canonical(a) != Canonical(b)) { differences.Add("Compiled function changed: " + name); packageOkay = false; } else functionCount++;
            }
            if (packageOkay) packageCount++;
        }
        return new BehaviorValidationReport(FormatVersions.BehaviorValidationReport, expected.Profile, Fingerprint(expected), Fingerprint(candidate), packageCount, classCount, functionCount, differences, differences.Count == 0);
    }

    private static string Canonical<T>(T value) => JsonSerializer.Serialize(value, ReconJson.Options);
    private static string Fingerprint(BehaviorSnapshot snapshot)
    {
        var normalized = new { snapshot.Profile, Packages = snapshot.Packages.Select(x => new { x.InternalPath, x.PackagePath, x.ImportPackages, x.DependencyFingerprint, x.Classes, x.Functions }).ToArray(), snapshot.Diagnostics };
        return Identity.Sha256Bytes(JsonSerializer.SerializeToUtf8Bytes(normalized, ReconJson.Options));
    }
}
