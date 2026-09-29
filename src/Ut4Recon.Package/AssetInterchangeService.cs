using System.Text;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.Package;

public sealed class AssetInterchangeService
{
    public AssetExtractionManifest Extract(string stateDirectory, ReconstructionIr ir, string objectId, string outputDirectory)
    {
        stateDirectory = Path.GetFullPath(stateDirectory); outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any()) throw new IOException("Asset bundle output directory is not empty.");
        Directory.CreateDirectory(outputDirectory);
        var matches = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item)))
            .Where(x => x.Object.Id.Equals(objectId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Object id resolved to {matches.Length} objects.");
        var selected = matches[0]; var package = selected.Package;
        var baselineRoot = Path.Combine(stateDirectory, "baseline"); var sourcePackage = ResolveUnder(baselineRoot, package.InternalPath);
        Verify(sourcePackage, package.File);
        var kind = KindFor(selected.Object, sourcePackage);

        var baselineFiles = CopyPackageFiles(package, baselineRoot, Path.Combine(outputDirectory, "baseline"), "baseline-package");
        var bundlePackage = ResolveUnder(Path.Combine(outputDirectory, "baseline"), package.InternalPath);
        var closure = ClosureFor(bundlePackage, package, kind, selected.Object.ExportIndex).Select(x => new AssetClosureObject(x.Role, x.Object.Id, x.Object.ObjectPath,
            x.Object.ClassPath, x.Object.ExportIndex, x.Object.PayloadHash, x.Object.OpaqueNativeBytes)).ToArray();
        if (!closure.Any(x => x.ReconstructionId == objectId)) throw new InvalidDataException("Selected object is outside the supported replacement closure.");

        var dependencies = CopyDependencies(ir, package, baselineRoot, Path.Combine(outputDirectory, "dependencies"));
        var interchange = new List<AssetBundleFile>(); var interchangeRoot = Path.Combine(outputDirectory, "interchange");
        if (kind == AssetReplacementKind.PropertySet)
        {
            var properties = new PropertyPatcher().ReadEditableProperties(sourcePackage, selected.Object.ExportIndex);
            var document = new ExternalPropertyDocument(FormatVersions.ExternalPropertyDocument, ir.Profile, package.PackagePath, selected.Object.Id, selected.Object.ObjectPath, selected.Object.PayloadHash, properties);
            var propertyRoot = Path.Combine(interchangeRoot, "properties"); Directory.CreateDirectory(propertyRoot);
            var original = Path.Combine(propertyRoot, "original-properties.json"); var modified = Path.Combine(propertyRoot, "modified-properties.json"); ReconJson.Write(original, document); ReconJson.Write(modified, document);
            interchange.AddRange(FilesUnder(outputDirectory, propertyRoot, "property-interchange"));
        }
        else if (kind == AssetReplacementKind.StaticMeshCollision)
        {
            var mesh = new NativeMeshDecoder().Decode(Path.Combine(outputDirectory, "baseline"), package.InternalPath);
            var workspace = new MeshEditorWorkspaceGenerator().Create(mesh, Path.Combine(interchangeRoot, "mesh"), "/Game/UT4Recon/ExternalDonors");
            var workspacePath = Path.Combine(interchangeRoot, "mesh", "mesh-editor-workspace.json");
            ReconJson.Write(workspacePath, workspace with { MeshIr = Path.GetFileName(workspace.MeshIr), EditorObj = Path.GetFileName(workspace.EditorObj),
                RenderObj = Path.GetFileName(workspace.RenderObj), CollisionObj = Path.GetFileName(workspace.CollisionObj), ImportSettings = Path.GetFileName(workspace.ImportSettings) });
            var settings = File.ReadAllText(workspace.ImportSettings).Replace(workspace.EditorObj.Replace('\\', '/'), Path.GetFileName(workspace.EditorObj), StringComparison.Ordinal);
            File.WriteAllText(workspace.ImportSettings, settings, new UTF8Encoding(false));
            interchange.AddRange(FilesUnder(outputDirectory, Path.Combine(interchangeRoot, "mesh"), "mesh-interchange"));
        }
        else
        {
            var map = new NativeBspDecoder().Decode(bundlePackage, package.InternalPath);
            var workspace = new BspInterchangeWriter().CreateWorkspace(map, Path.Combine(interchangeRoot, "bsp"), "/Game/UT4Recon/ExternalDonors");
            ReconJson.Write(Path.Combine(interchangeRoot, "bsp", "bsp-editor-workspace.json"), workspace with { BspIr = Path.GetFileName(workspace.BspIr), T3d = Path.GetFileName(workspace.T3d), Obj = Path.GetFileName(workspace.Obj) });
            interchange.AddRange(FilesUnder(outputDirectory, Path.Combine(interchangeRoot, "bsp"), "bsp-interchange"));
        }

        var contractRelative = "replacement-contract.json";
        var contract = ContractFor(ir, selected.Object, package, kind, closure);
        var contractPath = Path.Combine(outputDirectory, contractRelative); ReconJson.Write(contractPath, contract);
        var contractFile = new AssetBundleFile("replacement-contract", contractRelative, Identity.File(contractPath, contractRelative));
        IReadOnlyList<string> limitations = kind switch
        {
            AssetReplacementKind.PropertySet => new[] { "Only listed allowlisted properties can change; the remainder of the export and every other export remain baseline-owned.", "The modified donor is JSON and cannot add properties or change object identity." },
            AssetReplacementKind.StaticMeshCollision => new[] { "Only the BodySetup and matching NavCollision closure become donor-owned; render mesh data remains baseline-owned.", "The initial donor profile requires an identical name/import/export shell." },
            _ => new[] { "Recovered BSP represents compiled surfaces rather than the original CSG brush history.", "Compatible-shell and validated fresh-shell donors are supported; the Level export and unrelated exports remain baseline-owned." }
        };
        var input = Read<InputManifest>(Path.Combine(stateDirectory, "input-manifest.json"));
        var manifest = new AssetExtractionManifest(FormatVersions.AssetExtractionManifest, ir.Profile, input.CreatedUtc, ir.InputPak,
            Identity.File(Path.Combine(stateDirectory, "reconstruction-ir.json"), "reconstruction-ir.json"), kind, package.PackagePath, package.InternalPath,
            selected.Object.Id, selected.Object.ObjectPath, selected.Object.ClassPath, package.ObjectVersion, package.RecordedEngineVersion, package.PackageFlags, package.CustomVersions,
            baselineFiles, closure, dependencies, interchange, contractFile, limitations);
        ReconJson.Write(Path.Combine(outputDirectory, "extraction-manifest.json"), manifest);
        File.WriteAllText(Path.Combine(outputDirectory, "README.md"), Guide(manifest, contract), new UTF8Encoding(false));
        return manifest;
    }

    public DonorValidationReport ValidateDonor(string bundleDirectory, string donorPath, DonorMode mode)
    {
        bundleDirectory = Path.GetFullPath(bundleDirectory); donorPath = Path.GetFullPath(donorPath);
        var manifest = Read<AssetExtractionManifest>(Path.Combine(bundleDirectory, "extraction-manifest.json"));
        VerifyBundleFile(bundleDirectory, manifest.ReplacementContract); var contract = Read<ReplacementContract>(ResolveUnder(bundleDirectory, manifest.ReplacementContract.RelativePath));
        foreach (var file in manifest.BaselineFiles) VerifyBundleFile(bundleDirectory, file);
        foreach (var dependency in manifest.Dependencies.Where(x => x.Included)) foreach (var file in dependency.Files) VerifyBundleFile(bundleDirectory, file);
        VerifyContract(manifest, contract);
        if (!contract.AllowedDonorModes.Contains(mode)) throw new NotSupportedException($"Donor mode {mode} is not allowed by this replacement contract.");
        var baselineEntry = manifest.BaselineFiles.Single(x => x.Role == "baseline-package");
        var baseline = ResolveUnder(bundleDirectory, baselineEntry.RelativePath); Verify(baseline, baselineEntry.File);
        VerifyBaselineClosure(manifest, baseline);
        if (!File.Exists(donorPath)) throw new FileNotFoundException("Cooked donor package is missing.", donorPath);
        var checks = new List<DonorValidationCheck>(); var diagnostics = new List<string>(); var changed = new List<int>(); var unrelatedVerified = 0;
        var tempRoot = Path.Combine(bundleDirectory, ".validation-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(tempRoot);
        var preview = Path.Combine(tempRoot, Path.GetFileName(baseline));
        try
        {
            if (manifest.Kind == AssetReplacementKind.PropertySet)
            {
                var changes = PropertyChanges(manifest, baseline, donorPath);
                var current = baseline; var step = 0; var patcher = new PropertyPatcher();
                foreach (var change in changes)
                {
                    var next = Path.Combine(tempRoot, $"property-{++step}{Path.GetExtension(baseline)}"); patcher.Apply(current, next, manifest.Closure.Single().ExportIndex,
                        change.Before.PropertyPath, change.Before.Value, change.After.Value); current = next;
                }
                if (changes.Count != 0) changed.Add(manifest.Closure.Single().ExportIndex);
                checks.Add(new("property-document-identity", true, "Profile, package, object identity, payload hash, property paths, and writer rules match the baseline."));
                checks.Add(new("allowlisted-property-write", changes.Count != 0, changes.Count == 0 ? "The external property document contains no changes." : $"{changes.Count} changed properties passed their native writers."));
                unrelatedVerified = new PackageInspector().Inspect(baseline, manifest.InternalPath).Objects.Count - 1;
            }
            else if (manifest.Kind == AssetReplacementKind.StaticMeshCollision)
            {
                var report = new CollisionGraftEngine().Graft(baseline, donorPath, preview, manifest.InternalPath);
                checks.Add(new("compatible-package-shell", report.Passed, report.Passed ? "Name, import, export, and native collision layouts are compatible." : string.Join("; ", report.Diagnostics)));
                checks.Add(new("render-preserved", report.RenderPreserved, "The baseline StaticMesh render export must remain byte-identical."));
                checks.Add(new("collision-matches-donor", report.CollisionMatchesDonor && report.NavigationCollisionMatchesDonor, "BodySetup and NavCollision output must match the donor."));
            }
            else
            {
                var report = mode == DonorMode.FreshShell
                    ? new FreshBspGraftEngine().Graft(baseline, donorPath, preview, manifest.InternalPath)
                    : new BspGraftEngine().Graft(baseline, donorPath, preview, manifest.InternalPath);
                changed.AddRange(report.ChangedClosureExportIndices);
                checks.Add(new("bsp-closure-compatible", report.Passed, report.Passed ? "The donor closure can be remapped and grafted." : string.Join("; ", report.Diagnostics)));
                checks.Add(new("level-preserved", report.LevelExportsPreserved, "Persistent Level exports must remain baseline-owned."));
                checks.Add(new("geometry-matches-donor", report.GeometryMatchesDonor && report.CollisionClosureMatchesDonor, "Decoded geometry and collision closure must match the donor."));
                unrelatedVerified = report.UnrelatedExportsVerified + report.PreservedLevelExportCount;
            }

            if (manifest.Kind != AssetReplacementKind.PropertySet)
            {
                var before = new PackageInspector().Inspect(baseline, manifest.InternalPath); var donor = new PackageInspector().Inspect(donorPath, manifest.InternalPath);
                if (manifest.Kind == AssetReplacementKind.StaticMeshCollision)
                {
                    unrelatedVerified = before.Objects.Count - manifest.Closure.Count(x => x.Role is "collision-body-setup" or "navigation-collision");
                    foreach (var item in manifest.Closure.Where(x => x.Role is "collision-body-setup" or "navigation-collision"))
                        if (item.ExportIndex <= donor.Objects.Count && before.Objects.Single(x => x.ExportIndex == item.ExportIndex).PayloadHash != donor.Objects.Single(x => x.ExportIndex == item.ExportIndex).PayloadHash) changed.Add(item.ExportIndex);
                }
            }
            var rootChanged = changed.Contains(manifest.Closure.Single(x => x.ReconstructionId == manifest.RootObjectId).ExportIndex);
            checks.Add(new("selected-root-changed", rootChanged, rootChanged ? "The donor changes the selected replacement root." : "The selected replacement root is unchanged."));
            if (!rootChanged) diagnostics.Add("The donor does not change the selected root object.");
        }
        catch (Exception error)
        {
            diagnostics.Add(error.Message); checks.Add(new("graft-preflight", false, error.Message));
        }
        finally { Directory.Delete(tempRoot, true); }
        var passed = diagnostics.Count == 0 && checks.Count != 0 && checks.All(x => x.Passed);
        return new DonorValidationReport(FormatVersions.DonorValidationReport, contract.ContractId, manifest.Kind, mode, Identity.File(baseline), Identity.File(donorPath),
            changed.Distinct().Order().ToArray(), contract.UserOwnedScope, contract.PreservedScope, unrelatedVerified, checks, diagnostics, passed);
    }

    public IReadOnlyList<(ProxyProperty Before, ProxyProperty After)> PropertyChanges(string bundleDirectory, string donorPath)
    {
        bundleDirectory = Path.GetFullPath(bundleDirectory); var manifest = Read<AssetExtractionManifest>(Path.Combine(bundleDirectory, "extraction-manifest.json"));
        if (manifest.Kind != AssetReplacementKind.PropertySet) throw new NotSupportedException("Asset bundle is not a property-set contract.");
        var baselineEntry = manifest.BaselineFiles.Single(x => x.Role == "baseline-package"); var baseline = ResolveUnder(bundleDirectory, baselineEntry.RelativePath); Verify(baseline, baselineEntry.File);
        return PropertyChanges(manifest, baseline, Path.GetFullPath(donorPath));
    }

    private static IReadOnlyList<(ProxyProperty Before, ProxyProperty After)> PropertyChanges(AssetExtractionManifest manifest, string baseline, string donorPath)
    {
        var document = Read<ExternalPropertyDocument>(donorPath); var root = manifest.Closure.Single();
        if (document.SchemaVersion != FormatVersions.ExternalPropertyDocument || document.Profile != manifest.Profile || document.PackagePath != manifest.PackagePath || document.ObjectId != manifest.RootObjectId ||
            document.ObjectPath != manifest.RootObjectPath || document.OriginalPayloadHash != root.PayloadHash) throw new InvalidDataException("External property document identity does not match the replacement contract.");
        var expected = new PropertyPatcher().ReadEditableProperties(baseline, root.ExportIndex); var supplied = document.Properties.ToDictionary(x => x.PropertyPath, StringComparer.Ordinal);
        if (supplied.Count != document.Properties.Count || expected.Count != supplied.Count) throw new InvalidDataException("External property document must contain every original property exactly once.");
        var changes = new List<(ProxyProperty, ProxyProperty)>();
        foreach (var before in expected)
        {
            if (!supplied.TryGetValue(before.PropertyPath, out var after) || after.SupportRule != before.SupportRule || EditRules.RuleFor(after.PropertyPath) != after.SupportRule)
                throw new InvalidDataException("External property path or support rule differs from the baseline contract: " + before.PropertyPath);
            if (!PropertyPatcher.JsonEquivalent(before.Value, after.Value)) changes.Add((before, after));
        }
        return changes;
    }

    private static AssetReplacementKind KindFor(PackageObject item, string packagePath)
    {
        if (item.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) == true) return AssetReplacementKind.StaticMeshCollision;
        if (item.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) == true) return AssetReplacementKind.BspModelClosure;
        if (new PropertyPatcher().ReadEditableProperties(packagePath, item.ExportIndex).Count != 0) return AssetReplacementKind.PropertySet;
        throw new NotSupportedException("Initial extract-asset supports allowlisted property sets, BodySetup collision, and UModel BSP roots.");
    }

    private static IReadOnlyList<(string Role, PackageObject Object)> ClosureFor(string packagePath, PackageInventory package, AssetReplacementKind kind, int selectedExportIndex)
    {
        if (kind == AssetReplacementKind.PropertySet) return [("property-root", package.Objects.Single(x => x.ExportIndex == selectedExportIndex))];
        if (kind == AssetReplacementKind.StaticMeshCollision)
            return package.Objects.Where(x => x.ClassPath?.EndsWith(".StaticMesh", StringComparison.Ordinal) == true || x.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) == true || x.ClassPath?.EndsWith(".NavCollision", StringComparison.Ordinal) == true)
                .Select(x => (x.ClassPath!.EndsWith(".StaticMesh", StringComparison.Ordinal) ? "preserved-render-root" : x.ClassPath.EndsWith(".BodySetup", StringComparison.Ordinal) ? "collision-body-setup" : "navigation-collision", x)).ToArray();
        var asset = new UAsset(packagePath, EngineVersion.VER_UE4_15); var componentIndices = asset.Exports.Select((x, i) => (x, Index: i + 1)).Where(x => x.x.GetExportClassType().ToString() == "ModelComponent").Select(x => x.Index).ToHashSet();
        return package.Objects.Where(x => x.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) == true || x.ClassPath?.EndsWith(".ModelComponent", StringComparison.Ordinal) == true ||
                (x.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) == true && asset.Exports[x.ExportIndex - 1].OuterIndex.Index > 0 && componentIndices.Contains(asset.Exports[x.ExportIndex - 1].OuterIndex.Index)))
            .Select(x => (x.ClassPath!.EndsWith(".Model", StringComparison.Ordinal) ? "bsp-model" : x.ClassPath.EndsWith(".ModelComponent", StringComparison.Ordinal) ? "bsp-model-component" : "bsp-body-setup", x)).ToArray();
    }

    private static ReplacementContract ContractFor(ReconstructionIr ir, PackageObject root, PackageInventory package, AssetReplacementKind kind, IReadOnlyList<AssetClosureObject> closure)
    {
        var id = Identity.Sha256Bytes(Encoding.UTF8.GetBytes(string.Join("\n", ir.Profile, ir.InputPak.Sha256, package.PackagePath, root.Id, kind)));
        var collision = kind == AssetReplacementKind.StaticMeshCollision; var properties = kind == AssetReplacementKind.PropertySet;
        return new ReplacementContract(FormatVersions.ReplacementContract, id, ir.Profile, kind, package.PackagePath, package.InternalPath, root.Id, root.ObjectPath,
            root.PayloadHash, ClosureFingerprint(closure), properties ? [DonorMode.ExternalDocument] : collision ? [DonorMode.CompatibleShell] : [DonorMode.CompatibleShell, DonorMode.FreshShell],
            properties
                ? ["Edit only values in modified-properties.json.", "Retain all identity fields, property paths, and support rules.", "Change at least one value."]
                : collision
                ? ["Cook with the matching UT4 4.15 editor.", "Retain the original package name/import/export shell.", "Change the selected BodySetup collision closure."]
                : ["Cook with the matching UT4 4.15 editor.", "Produce a supported compatible-shell or fresh-shell UModel closure.", "Change the selected UModel geometry."],
            closure.Where(x => x.Role != "preserved-render-root").Select(x => x.Role + ": " + x.ObjectPath).ToArray(),
            properties ? ["all unlisted properties", "opaque native bytes", "all unrelated exports"] : collision ? ["StaticMesh render export", "all unrelated exports"] : ["persistent Level exports", "compiled behavior", "all unrelated exports"],
            ["unresolved native references", "undeclared export changes", "compiled behavior changes", "package/profile mismatch"]);
    }

    private static IReadOnlyList<AssetDependency> CopyDependencies(ReconstructionIr ir, PackageInventory source, string baselineRoot, string destinationRoot)
    {
        var packages = ir.Packages.ToDictionary(x => x.PackagePath, StringComparer.OrdinalIgnoreCase); var result = new List<AssetDependency>();
        var pending = new Queue<string>(source.ImportPackages.Distinct(StringComparer.OrdinalIgnoreCase)); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { source.PackagePath };
        while (pending.Count != 0)
        {
            var path = pending.Dequeue(); if (!seen.Add(path)) continue;
            if (!packages.TryGetValue(path, out var dependency)) { result.Add(new(path, false, [])); continue; }
            var files = CopyPackageFiles(dependency, baselineRoot, destinationRoot, "dependency"); result.Add(new(path, true, files));
            foreach (var child in dependency.ImportPackages) if (!seen.Contains(child)) pending.Enqueue(child);
        }
        return result.OrderBy(x => x.PackagePath, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<AssetBundleFile> CopyPackageFiles(PackageInventory package, string sourceRoot, string destinationRoot, string role)
    {
        var identities = new[] { package.File }.Concat(package.Sidecars); var files = new List<AssetBundleFile>();
        foreach (var identity in identities)
        {
            var source = ResolveUnder(sourceRoot, identity.Path); Verify(source, identity); var relative = Path.GetRelativePath(sourceRoot, source).Replace('\\', '/');
            var destination = ResolveUnder(destinationRoot, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination);
            files.Add(new(role, Path.GetRelativePath(Path.GetDirectoryName(destinationRoot)!, destination).Replace('\\', '/'), Identity.File(destination, relative)));
        }
        return files;
    }

    private static IReadOnlyList<AssetBundleFile> FilesUnder(string bundleRoot, string directory, string role) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(x => x, StringComparer.Ordinal).Select(path => new AssetBundleFile(role, Path.GetRelativePath(bundleRoot, path).Replace('\\', '/'), Identity.File(path, Path.GetRelativePath(bundleRoot, path).Replace('\\', '/')))).ToArray();
    private static string ResolveUnder(string root, string relative)
    {
        root = Path.GetFullPath(root); var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Bundle path escapes its root."); return path;
    }
    private static void Verify(string path, FileIdentity identity)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != identity.Size || Identity.Sha256File(path) != identity.Sha256) throw new InvalidDataException("File identity mismatch: " + path);
    }
    private static void VerifyBundleFile(string root, AssetBundleFile file) => Verify(ResolveUnder(root, file.RelativePath), file.File);
    private static void VerifyContract(AssetExtractionManifest manifest, ReplacementContract contract)
    {
        if (contract.Profile != manifest.Profile || contract.Kind != manifest.Kind || contract.PackagePath != manifest.PackagePath || contract.InternalPath != manifest.InternalPath ||
            contract.RootObjectId != manifest.RootObjectId || contract.RootObjectPath != manifest.RootObjectPath || contract.OriginalRootPayloadHash != manifest.Closure.Single(x => x.ReconstructionId == manifest.RootObjectId).PayloadHash)
            throw new InvalidDataException("Replacement contract does not match its extraction manifest.");
        if (contract.ClosureFingerprint != ClosureFingerprint(manifest.Closure)) throw new InvalidDataException("Replacement closure fingerprint does not match the extraction manifest.");
        var expectedId = Identity.Sha256Bytes(Encoding.UTF8.GetBytes(string.Join("\n", manifest.Profile, manifest.InputPak.Sha256, manifest.PackagePath, manifest.RootObjectId, manifest.Kind)));
        if (contract.ContractId != expectedId) throw new InvalidDataException("Replacement contract ID is invalid.");
    }
    private static void VerifyBaselineClosure(AssetExtractionManifest manifest, string baseline)
    {
        var package = new PackageInspector().Inspect(baseline, manifest.InternalPath);
        if (package.PackagePath != manifest.PackagePath || package.InternalPath != manifest.InternalPath ||
            package.ObjectVersion != manifest.ObjectVersion || package.RecordedEngineVersion != manifest.RecordedEngineVersion ||
            package.PackageFlags != manifest.PackageFlags || !package.CustomVersions.SequenceEqual(manifest.CustomVersions))
            throw new InvalidDataException("Extraction manifest package metadata does not match the immutable baseline.");
        var root = package.Objects.SingleOrDefault(x => x.Id == manifest.RootObjectId)
            ?? throw new InvalidDataException("Extraction root does not exist in the immutable baseline.");
        if (root.ObjectPath != manifest.RootObjectPath || root.ClassPath != manifest.RootClassPath)
            throw new InvalidDataException("Extraction root identity does not match the immutable baseline.");
        var expected = ClosureFor(baseline, package, manifest.Kind, root.ExportIndex)
            .Select(x => new AssetClosureObject(x.Role, x.Object.Id, x.Object.ObjectPath, x.Object.ClassPath, x.Object.ExportIndex, x.Object.PayloadHash, x.Object.OpaqueNativeBytes))
            .OrderBy(x => x.ExportIndex).ToArray();
        var declared = manifest.Closure.OrderBy(x => x.ExportIndex).ToArray();
        if (!expected.SequenceEqual(declared) || ClosureFingerprint(expected) != ClosureFingerprint(declared))
            throw new InvalidDataException("Extraction manifest closure does not match the immutable baseline.");
    }
    private static string ClosureFingerprint(IEnumerable<AssetClosureObject> closure) => Identity.Sha256Bytes(Encoding.UTF8.GetBytes(string.Join("\n", closure.OrderBy(x => x.ExportIndex).Select(x => $"{x.Role}|{x.ReconstructionId}|{x.ExportIndex}|{x.PayloadHash}|{x.OpaqueNativeBytes}"))));
    private static T Read<T>(string path) => System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException(path);
    private static string Guide(AssetExtractionManifest manifest, ReplacementContract contract) => $"""
# UT4Recon external asset bundle

Kind: **{manifest.Kind}**<br>
Package: `{manifest.PackagePath}`<br>
Selected root: `{manifest.RootObjectPath}`

The `baseline` directory is an immutable cooked reference. Modify the files under `interchange`, create and cook a donor with the matching UT4 editor or another declared converter, then run:

1. `ut4recon validate-donor <bundle> <donor> [--mode external|compatible|fresh]`
2. `ut4recon inject <recovery-project> <bundle> --donor <donor> [--mode external|compatible|fresh]`
3. `ut4recon set-map-title <recovery-project> --title <distinct-name> --output <generated-manifest>`
4. `ut4recon build <recovery-project> --manifest <generated-manifest> --rename-map <distinct-name> --output <output.pak>`

Only the contract's user-owned closure is copied from the donor. The surrounding cooked package remains baseline-owned and hash-validated.

Contract: `{manifest.ReplacementContract.RelativePath}`<br>
Contract ID: `{contract.ContractId}`
""" + Environment.NewLine;
}
