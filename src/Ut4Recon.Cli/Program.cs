using System.Reflection;
using System.Text.Json;
using Ut4Recon.Core;
using Ut4Recon.EditorProtocol;
using Ut4Recon.Geometry;
using Ut4Recon.Package;
using Ut4Recon.Pak;

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help") { PrintUsage(); return 0; }
    if (args[0].Equals("create-editor-project", StringComparison.OrdinalIgnoreCase)) return CreateEditorProject(args);
    if (args[0].Equals("run-editor-import", StringComparison.OrdinalIgnoreCase)) return RunEditorImport(args);
    if (args[0].Equals("open-editor-workspace", StringComparison.OrdinalIgnoreCase)) return OpenEditorWorkspace(args);
    if (args[0].Equals("run-editor-export", StringComparison.OrdinalIgnoreCase)) return RunEditorExport(args);
    if (args[0].Equals("export-editor-edits", StringComparison.OrdinalIgnoreCase)) return ExportEditorEdits(args);
    if (args[0].Equals("extract-asset", StringComparison.OrdinalIgnoreCase)) return ExtractAsset(args);
    if (args[0].Equals("list-extractable", StringComparison.OrdinalIgnoreCase)) return ListExtractable(args);
    if (args[0].Equals("validate-donor", StringComparison.OrdinalIgnoreCase)) return ValidateDonor(args);
    if (args[0].Equals("inject", StringComparison.OrdinalIgnoreCase)) return InjectAsset(args);
    if (args[0].Equals("delete-actor", StringComparison.OrdinalIgnoreCase)) return DeleteActor(args);
    if (args[0].Equals("clone-actor", StringComparison.OrdinalIgnoreCase)) return CloneActor(args);
    if (args[0].Equals("certify-collision-box-template", StringComparison.OrdinalIgnoreCase)) return CertifyCollisionBoxTemplate(args);
    if (args[0].Equals("add-collision-box", StringComparison.OrdinalIgnoreCase)) return AddCollisionBox(args);
    if (args[0].Equals("set-map-title", StringComparison.OrdinalIgnoreCase)) return SetMapTitle(args);
    if (args[0].Equals("create-custom-collision-workspace", StringComparison.OrdinalIgnoreCase)) return CreateCustomCollisionWorkspace(args);
    if (args[0].Equals("run-custom-collision-import", StringComparison.OrdinalIgnoreCase)) return RunCustomCollisionImport(args);
    if (args[0].Equals("open-custom-collision-workspace", StringComparison.OrdinalIgnoreCase)) return OpenCustomCollisionWorkspace(args);
    if (args[0].Equals("cook-custom-collision-donor", StringComparison.OrdinalIgnoreCase)) return CookCustomCollisionDonor(args);
    if (args[0].Equals("add-custom-collision", StringComparison.OrdinalIgnoreCase)) return AddCustomCollision(args);
    if (args[0].Equals("inspect-mesh", StringComparison.OrdinalIgnoreCase)) return InspectMesh(args);
    if (args[0].Equals("create-mesh-workspace", StringComparison.OrdinalIgnoreCase)) return CreateMeshWorkspace(args);
    if (args[0].Equals("compare-mesh", StringComparison.OrdinalIgnoreCase)) return CompareMesh(args);
    if (args[0].Equals("graft-collision", StringComparison.OrdinalIgnoreCase)) return GraftCollision(args);
    if (args[0].Equals("replace-collision", StringComparison.OrdinalIgnoreCase)) return ReplaceCollision(args);
    if (args[0].Equals("inspect-bsp", StringComparison.OrdinalIgnoreCase)) return InspectBsp(args);
    if (args[0].Equals("create-bsp-workspace", StringComparison.OrdinalIgnoreCase)) return CreateBspWorkspace(args);
    if (args[0].Equals("compare-bsp", StringComparison.OrdinalIgnoreCase)) return CompareBsp(args);
    if (args[0].Equals("graft-bsp", StringComparison.OrdinalIgnoreCase)) return GraftBsp(args);
    if (args[0].Equals("graft-bsp-fresh", StringComparison.OrdinalIgnoreCase)) return GraftFreshBsp(args);
    if (args[0].Equals("replace-bsp", StringComparison.OrdinalIgnoreCase)) return ReplaceBsp(args);
    if (args[0].Equals("inspect-behavior", StringComparison.OrdinalIgnoreCase)) return InspectBehavior(args);
    if (args[0].Equals("compare-behavior", StringComparison.OrdinalIgnoreCase)) return CompareBehavior(args);
    if (args[0].Equals("inspect-object", StringComparison.OrdinalIgnoreCase)) return InspectObject(args);
    if (args[0].Equals("inspect-objects", StringComparison.OrdinalIgnoreCase)) return InspectObjects(args);
    if (args[0].Equals("extract-entry", StringComparison.OrdinalIgnoreCase)) return ExtractEntry(args);
    if (args[0].Equals("new-edit", StringComparison.OrdinalIgnoreCase)) return NewEdit(args);
    if (args[0].Equals("certify-runtime-load", StringComparison.OrdinalIgnoreCase)) return CertifyRuntimeLoad(args);
    if (args[0].Equals("build", StringComparison.OrdinalIgnoreCase)) return BuildProject(args);
    var createProject = args[0].Equals("create-project", StringComparison.OrdinalIgnoreCase);
    if (!createProject && !args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase)) return Fail($"Unknown command: {args[0]}");
    if (args.Length < 2) return Fail($"{args[0]} requires an input pak");
    try
    {
        var pakPath = Path.GetFullPath(args[1]); var output = GetOption(args, "--output") ?? Path.Combine(Path.GetDirectoryName(pakPath)!, Path.GetFileNameWithoutExtension(pakPath) + ".ut4recon");
        var state = Path.Combine(Path.GetFullPath(output), ".ut4recon"); var baseline = Path.Combine(state, "baseline"); Directory.CreateDirectory(Path.Combine(state, "reports"));
        var unrealPak = ResolveUnrealPak(args); using var pak = new PakExtractor(pakPath, unrealPak); var inventory = pak.ExtractTo(baseline); var packages = new List<PackageInventory>(); var inspector = new PackageInspector();
        foreach (var entry in inventory.Entries.Where(x => x.Path.EndsWith(".umap", StringComparison.OrdinalIgnoreCase) || x.Path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)))
        {
            var package = inspector.Inspect(Path.Combine(baseline, entry.Path.Replace('/', Path.DirectorySeparatorChar)), entry.Path); var stem = Path.ChangeExtension(entry.Path, null);
            var sidecars = inventory.Entries.Where(x => Path.ChangeExtension(x.Path, null).Equals(stem, StringComparison.OrdinalIgnoreCase) && !x.Path.Equals(entry.Path, StringComparison.OrdinalIgnoreCase))
                .Select(x => new FileIdentity(x.Path, x.Size, x.Sha256)).ToArray(); packages.Add(package with { Sidecars = sidecars });
        }
        const string profile = "ut4-4.15-windows-no-editor-v1";
        var tool = new ToolIdentity("ut4recon", Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.1.0");
        var manifest = new InputManifest(FormatVersions.InputManifest, tool, DateTimeOffset.UtcNow, profile, inventory, inventory.Entries.Select(x => new FileIdentity(x.Path, x.Size, x.Sha256)).ToArray());
        var ir = new ReconstructionIr(FormatVersions.ReconstructionIr, profile, inventory.Source, packages); var allObjects = packages.SelectMany(x => x.Objects).ToArray();
        var diagnostics = packages.SelectMany(x => x.Diagnostics.Select(d => x.InternalPath + ": " + d)).ToArray();
        var report = new SupportReport(FormatVersions.SupportReport, profile, Enum.GetValues<SupportLevel>().Select(level => new SupportCount(level, allObjects.Count(x => x.Support == level))).ToArray(), diagnostics,
            diagnostics.Length == 0 && allObjects.All(x => x.Support != SupportLevel.Unsupported));
        ReconJson.Write(Path.Combine(state, "input-manifest.json"), manifest); ReconJson.Write(Path.Combine(state, "reconstruction-ir.json"), ir); ReconJson.Write(Path.Combine(state, "support-report.json"), report);
        var behavior = new BehaviorAnalyzer().InspectProject(baseline, ir); ReconJson.Write(Path.Combine(state, "behavior-baseline.json"), behavior);
        if (createProject)
        {
            Directory.CreateDirectory(Path.Combine(state, "donors"));
            var meshRoot = Path.Combine(Path.GetFullPath(output), "MeshWorkspaces"); var meshIndex = new RecoveryMeshWorkspaceGenerator().Create(baseline, ir, meshRoot);
            var bspRoot = Path.Combine(Path.GetFullPath(output), "BspWorkspaces"); var bspIndex = new RecoveryBspWorkspaceGenerator().Create(baseline, ir, bspRoot);
            ReconJson.Write(Path.Combine(state, "project.json"), new { schemaVersion = 1, profile, sourcePak = inventory.Source, inputManifest = "input-manifest.json", reconstructionIr = "reconstruction-ir.json", supportReport = "support-report.json", behaviorBaseline = "behavior-baseline.json", meshWorkspaceIndex = Path.Combine(meshRoot, "mesh-workspace-index.json"), bspWorkspaceIndex = Path.Combine(bspRoot, "bsp-workspace-index.json"), editorProjectStatus = "available" });
            Console.WriteLine($"Generated mesh workspaces: {meshIndex.Entries.Count(x => x.Status != "unsupported")}; unsupported meshes: {meshIndex.Entries.Count(x => x.Status == "unsupported")}");
            Console.WriteLine($"Generated BSP workspaces: {bspIndex.Entries.Count(x => x.Status != "unsupported")}; unsupported BSP maps: {bspIndex.Entries.Count(x => x.Status == "unsupported")}");
            foreach (var path in Directory.EnumerateFiles(baseline, "*", SearchOption.AllDirectories)) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        else
        {
            Directory.Delete(baseline, true);
        }
        Console.WriteLine($"Inspected {inventory.Entries.Count} pak entries and {packages.Count} packages."); Console.WriteLine($"Mount point: {inventory.MountPoint}");
        foreach (var count in report.Counts) Console.WriteLine($"{count.Level}: {count.Count}"); Console.WriteLine(createProject ? $"Project state: {state}" : $"Reports: {state}"); return diagnostics.Length == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int InspectObject(string[] args)
{
    if (args.Length < 2) return Fail("inspect-object requires a recovery project.");
    try
    {
        var project = Path.GetFullPath(args[1]);
        var objectId = GetOption(args, "--object") ?? throw new ArgumentException("inspect-object requires --object <id>.");
        var state = Path.Combine(project, ".ut4recon");
        var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json"));
        var matches = ir.Packages
            .SelectMany(package => package.Objects.Select(item => (Package: package, Object: item)))
            .Where(x => x.Object.Id.Equals(objectId, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Object id resolved to {matches.Length} objects.");
        var match = matches[0];
        var baseline = Path.Combine(state, "baseline", match.Package.InternalPath.Replace('/', Path.DirectorySeparatorChar));
        var properties = new PropertyPatcher().ReadEditableProperties(baseline, match.Object.ExportIndex);
        JsonElement? rawExport = null;
        if (args.Contains("--raw", StringComparer.OrdinalIgnoreCase))
        {
            var asset = new UAssetAPI.UAsset(baseline, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15);
            using var document = JsonDocument.Parse(asset.SerializeJson());
            rawExport = document.RootElement.GetProperty("Exports")[match.Object.ExportIndex - 1].Clone();
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            match.Package.PackagePath,
            match.Object.Id,
            match.Object.ObjectPath,
            match.Object.ClassPath,
            match.Object.ExportIndex,
            EditableProperties = properties,
            RawExport = rawExport
        }, ReconJson.Options));
        return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int InspectObjects(string[] args)
{
    if (args.Length < 2) return Fail("inspect-objects requires a recovery project.");
    try
    {
        var project = Path.GetFullPath(args[1]);
        var pattern = GetOption(args, "--match") ?? throw new ArgumentException("inspect-objects requires --match <substring>.");
        var state = Path.Combine(project, ".ut4recon");
        var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json"));
        var results = new List<object>(); var patcher = new PropertyPatcher();
        foreach (var package in ir.Packages)
        {
            var matches = package.Objects.Where(item => item.ObjectPath.Contains(pattern, StringComparison.OrdinalIgnoreCase) || (item.ClassPath?.Contains(pattern, StringComparison.OrdinalIgnoreCase) ?? false)).ToArray();
            if (matches.Length == 0) continue;
            var baseline = Path.Combine(state, "baseline", package.InternalPath.Replace('/', Path.DirectorySeparatorChar));
            var asset = new UAssetAPI.UAsset(baseline, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15);
            foreach (var item in matches)
            {
                IReadOnlyList<ProxyProperty> properties;
                try { properties = patcher.ReadEditableProperties(asset, item.ExportIndex); }
                catch (NotSupportedException) { properties = []; }
                results.Add(new { package.PackagePath, item.Id, item.ObjectPath, item.ClassPath, item.ExportIndex, EditableProperties = properties });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(results, ReconJson.Options));
        return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int ExtractEntry(string[] args)
{
    if (args.Length < 3) return Fail("extract-entry requires a pak and internal entry path.");
    try
    {
        var pakPath = Path.GetFullPath(args[1]); var internalPath = args[2];
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, Path.GetFileName(internalPath)));
        using var pak = new PakExtractor(pakPath, ResolveUnrealPak(args)); var file = pak.ExtractEntryTo(internalPath, output);
        Console.WriteLine($"Extracted {file.Path}: {file.Size} bytes; sha256 {file.Sha256}; output: {output}");
        return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int InspectBehavior(string[] args)
{
    if (args.Length < 2) return Fail("inspect-behavior requires a cooked package.");
    try
    {
        var input = Path.GetFullPath(args[1]); var internalPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(input); var package = new BehaviorAnalyzer().InspectPackage(input, internalPath);
        var snapshot = new BehaviorSnapshot(FormatVersions.BehaviorSnapshot, "ut4-4.15-windows-no-editor-v1", package.Classes.Count == 0 && package.Functions.Count == 0 ? [] : [package], []);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(input) + ".behavior.json")); ReconJson.Write(output, snapshot);
        Console.WriteLine($"Behavior classes: {package.Classes.Count}; compiled functions: {package.Functions.Count}; output: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CompareBehavior(string[] args)
{
    if (args.Length < 3) return Fail("compare-behavior requires expected and candidate behavior snapshots.");
    try
    {
        var expected = ReadJson<BehaviorSnapshot>(args[1]); var candidate = ReadJson<BehaviorSnapshot>(args[2]); var report = new BehaviorValidator().Compare(expected, candidate);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "behavior-validation.json")); ReconJson.Write(output, report);
        Console.WriteLine($"Behavior preserved: {report.Passed}; classes: {report.ClassesVerified}; functions: {report.FunctionsVerified}; output: {output}"); return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int InspectMesh(string[] args)
{
    if (args.Length < 3) return Fail("inspect-mesh requires a provider root and virtual package path.");
    try
    {
        var mesh = new NativeMeshDecoder().Decode(args[1], args[2]); var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(args[2]) + ".mesh-ir.json"));
        ReconJson.Write(output, mesh); var writer = new MeshInterchangeWriter();
        var obj = GetOption(args, "--obj"); if (obj is not null) writer.WriteObj(mesh, Path.GetFullPath(obj));
        var editorObj = GetOption(args, "--editor-obj"); if (editorObj is not null) writer.WriteEditorObj(mesh, Path.GetFullPath(editorObj));
        var collisionObj = GetOption(args, "--collision-obj"); if (collisionObj is not null) writer.WriteCollisionObj(mesh, Path.GetFullPath(collisionObj));
        Console.WriteLine($"Decoded {mesh.ObjectName}: {mesh.Lods.Count} LOD(s), {mesh.Lods.Sum(x => x.Positions.Count)} render vertices, {mesh.Lods.Sum(x => x.Indices.Count / 3)} triangles.");
        Console.WriteLine($"Convex hulls: {mesh.Collision?.ConvexHulls.Count ?? 0}; cooked collision payloads: {mesh.Collision?.CookedPayloads.Count ?? 0}"); Console.WriteLine($"Mesh IR: {output}"); return mesh.Diagnostics.Count == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CreateMeshWorkspace(string[] args)
{
    if (args.Length < 4) return Fail("create-mesh-workspace requires a provider root, virtual package path, and output directory.");
    try
    {
        var mesh = new NativeMeshDecoder().Decode(args[1], args[2]); var destination = GetOption(args, "--destination") ?? "/Game/UT4Recon/MeshDonors";
        var workspace = new MeshEditorWorkspaceGenerator().Create(mesh, args[3], destination);
        Console.WriteLine($"Mesh editor workspace: {Path.GetDirectoryName(workspace.MeshIr)}"); Console.WriteLine($"Editor OBJ: {workspace.EditorObj}"); Console.WriteLine($"Import destination: {workspace.DestinationPath}"); return mesh.Diagnostics.Count == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CompareMesh(string[] args)
{
    if (args.Length < 3) return Fail("compare-mesh requires two mesh IR files.");
    try
    {
        var expected = ReadJson<StaticMeshIr>(args[1]); var actual = ReadJson<StaticMeshIr>(args[2]); var report = new MeshValidator().Compare(expected, actual);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "mesh-comparison.json")); ReconJson.Write(output, report);
        Console.WriteLine($"Render equivalent: {report.RenderEquivalent}; collision equivalent: {report.CollisionEquivalent}"); Console.WriteLine($"Report: {output}"); return report.RenderEquivalent && report.CollisionEquivalent ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int GraftCollision(string[] args)
{
    if (args.Length < 4) return Fail("graft-collision requires baseline, donor, and output packages.");
    try
    {
        var baseline = Path.GetFullPath(args[1]); var donor = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]);
        if (output.Equals(baseline, StringComparison.OrdinalIgnoreCase) || output.Equals(donor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Collision graft output must be a new file.");
        var internalPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(baseline);
        var report = new CollisionGraftEngine().Graft(baseline, donor, output, internalPath); var reportPath = Path.GetFullPath(GetOption(args, "--report") ?? output + ".collision-graft.json"); ReconJson.Write(reportPath, report);
        Console.WriteLine($"Render preserved: {report.RenderPreserved}; collision matches donor: {report.CollisionMatchesDonor}"); Console.WriteLine($"Report: {reportPath}"); return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int ReplaceCollision(string[] args)
{
    if (args.Length < 2) return Fail("replace-collision requires a recovery project.");
    string? preview = null;
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var objectId = RequiredOption(args, "--object");
        var donorPath = Path.GetFullPath(RequiredOption(args, "--donor")); var match = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item))).Single(x => x.Object.Id == objectId);
        if (match.Object.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) != true) throw new NotSupportedException("Collision replacement must target a BodySetup export.");
        EnsureDirectWritablePackage(match.Package); var baseline = BaselinePath(state, match.Package.InternalPath); VerifyFile(baseline, match.Package.File); var donor = Identity.File(donorPath);
        preview = Path.Combine(Path.GetTempPath(), "ut4recon-collision-preview-" + Guid.NewGuid().ToString("N") + Path.GetExtension(baseline));
        var preflight = new CollisionGraftEngine().Graft(baseline, donorPath, preview, match.Package.InternalPath); if (!preflight.Passed) throw new InvalidDataException("Collision donor failed graft preflight: " + string.Join("; ", preflight.Diagnostics));
        var before = JsonSerializer.SerializeToElement(new { bodySetupPayloadHash = match.Object.PayloadHash }, ReconJson.Options); var after = JsonSerializer.SerializeToElement(new { donor }, ReconJson.Options);
        var operation = new EditOperation("replace-collision", match.Package.PackagePath, match.Object.Id, match.Object.ObjectPath, match.Object.PayloadHash, "$collisionClosure", before, after, "cli", "cooked-bodysetup-navcollision-graft-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var identity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != identity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.PackagePath == match.Package.PackagePath && x.Operation == "replace-collision")) throw new InvalidDataException("Existing manifest already replaces collision in this package.");
            manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, identity, [operation]);
        ReconJson.Write(output, manifest); Console.WriteLine($"Created collision replacement for {match.Object.ObjectPath}"); Console.WriteLine($"Cooked donor: {donorPath}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
    finally { if (preview is not null && File.Exists(preview)) File.Delete(preview); }
}

static int InspectBsp(string[] args)
{
    if (args.Length < 2) return Fail("inspect-bsp requires a cooked map package.");
    try
    {
        var input = Path.GetFullPath(args[1]); var virtualPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(input);
        var map = new NativeBspDecoder().Decode(input, virtualPath); var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, Path.GetFileNameWithoutExtension(input) + ".bsp-ir.json")); ReconJson.Write(output, map);
        var principal = map.Models.OrderByDescending(x => x.Polygons.Count).FirstOrDefault(); Console.WriteLine($"Decoded UModels: {map.Models.Count}; principal polygons: {principal?.Polygons.Count ?? 0}; surfaces: {principal?.Surfaces.Count ?? 0}"); Console.WriteLine($"BSP IR: {output}"); return map.Diagnostics.Count == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CreateBspWorkspace(string[] args)
{
    if (args.Length < 3) return Fail("create-bsp-workspace requires a cooked map package and output directory.");
    try
    {
        var input = Path.GetFullPath(args[1]); var virtualPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(input); var destination = GetOption(args, "--destination") ?? "/Game/UT4Recon/BspDonors";
        var map = new NativeBspDecoder().Decode(input, virtualPath); var workspace = new BspInterchangeWriter().CreateWorkspace(map, args[2], destination); Console.WriteLine($"BSP workspace: {Path.GetDirectoryName(workspace.BspIr)}"); Console.WriteLine($"T3D: {workspace.T3d}"); Console.WriteLine("Original CSG brush history is recorded as unavailable authoring metadata."); return map.Diagnostics.Count == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CompareBsp(string[] args)
{
    if (args.Length < 3) return Fail("compare-bsp requires two BSP IR files.");
    try
    {
        var expected = ReadJson<BspMapIr>(args[1]); var actual = ReadJson<BspMapIr>(args[2]); var report = new BspValidator().Compare(expected, actual);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "bsp-comparison.json")); ReconJson.Write(output, report); Console.WriteLine($"Within tolerance: {report.WithinTolerance}; area ratio: {report.SurfaceAreaDifferenceRatio:R}; bounds displacement: {report.MaximumBoundsDisplacement:R}"); Console.WriteLine($"Report: {output}"); return report.WithinTolerance ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int GraftBsp(string[] args)
{
    if (args.Length < 4) return Fail("graft-bsp requires baseline, donor, and output map packages.");
    try
    {
        var baseline = Path.GetFullPath(args[1]); var donor = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]);
        if (output.Equals(baseline, StringComparison.OrdinalIgnoreCase) || output.Equals(donor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("BSP graft output must be a new file.");
        var internalPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(baseline); var report = new BspGraftEngine().Graft(baseline, donor, output, internalPath);
        var reportPath = Path.GetFullPath(GetOption(args, "--report") ?? output + ".bsp-graft.json"); ReconJson.Write(reportPath, report); Console.WriteLine($"Geometry matches donor: {report.GeometryMatchesDonor}; level exports preserved: {report.LevelExportsPreserved}; changed closure exports: {report.ChangedClosureExportIndices.Count}"); Console.WriteLine($"Report: {reportPath}"); return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int GraftFreshBsp(string[] args)
{
    if (args.Length < 4) return Fail("graft-bsp-fresh requires baseline, fresh donor, and output map packages.");
    try
    {
        var baseline = Path.GetFullPath(args[1]); var donor = Path.GetFullPath(args[2]); var output = Path.GetFullPath(args[3]); if (output.Equals(baseline, StringComparison.OrdinalIgnoreCase) || output.Equals(donor, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("BSP graft output must be a new file.");
        var internalPath = GetOption(args, "--internal-path") ?? "UnrealTournament/Content/Recovered/" + Path.GetFileName(baseline); var report = new FreshBspGraftEngine().Graft(baseline, donor, output, internalPath); var reportPath = Path.GetFullPath(GetOption(args, "--report") ?? output + ".fresh-bsp-graft.json"); ReconJson.Write(reportPath, report);
        Console.WriteLine($"Fresh donor geometry matches: {report.GeometryMatchesDonor}; collision closure matches: {report.CollisionClosureMatchesDonor}; added closure exports: {report.AddedClosureExportIndices.Count}"); Console.WriteLine($"Report: {reportPath}"); return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int ReplaceBsp(string[] args)
{
    if (args.Length < 2) return Fail("replace-bsp requires a recovery project."); string? preview = null;
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var objectId = RequiredOption(args, "--object"); var donorPath = Path.GetFullPath(RequiredOption(args, "--donor"));
        var match = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item))).Single(x => x.Object.Id == objectId);
        if (match.Object.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) != true) throw new NotSupportedException("BSP replacement must target a UModel export."); EnsureDirectWritablePackage(match.Package); var baseline = BaselinePath(state, match.Package.InternalPath); VerifyFile(baseline, match.Package.File); var donor = Identity.File(donorPath);
        var freshShell = HasOption(args, "--fresh-shell"); preview = Path.Combine(Path.GetTempPath(), "ut4recon-bsp-preview-" + Guid.NewGuid().ToString("N") + Path.GetExtension(baseline)); var preflight = freshShell ? new FreshBspGraftEngine().Graft(baseline, donorPath, preview, match.Package.InternalPath) : new BspGraftEngine().Graft(baseline, donorPath, preview, match.Package.InternalPath); if (!preflight.Passed) throw new InvalidDataException("BSP donor failed graft preflight: " + string.Join("; ", preflight.Diagnostics));
        if (!preflight.ChangedClosureExportIndices.Contains(match.Object.ExportIndex)) throw new InvalidDataException("Selected UModel export is not changed in the cooked donor.");
        var before = JsonSerializer.SerializeToElement(new { modelPayloadHash = match.Object.PayloadHash }, ReconJson.Options); var after = JsonSerializer.SerializeToElement(new { donor, freshShell }, ReconJson.Options); var operation = new EditOperation("replace-bsp", match.Package.PackagePath, match.Object.Id, match.Object.ObjectPath, match.Object.PayloadHash, "$bspClosure", before, after, "cli", freshShell ? "fresh-cooked-bsp-model-closure-graft-v1" : "cooked-bsp-model-closure-graft-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var identity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output)) { var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != identity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state."); if (existing.Operations.Any(x => x.PackagePath == match.Package.PackagePath && x.Operation == "replace-bsp")) throw new InvalidDataException("Existing manifest already replaces BSP in this package."); manifest = existing with { Operations = [.. existing.Operations, operation] }; }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, identity, [operation]); ReconJson.Write(output, manifest); Console.WriteLine($"Created BSP replacement for {match.Object.ObjectPath}"); Console.WriteLine($"Changed donor closure exports: {preflight.ChangedClosureExportIndices.Count}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
    finally { if (preview is not null && File.Exists(preview)) File.Delete(preview); }
}

static int CreateEditorProject(string[] args)
{
    if (args.Length < 2) return Fail("create-editor-project requires a recovery project.");
    try
    {
        var projectRoot = Path.GetFullPath(args[1]); var state = ResolveState(projectRoot); var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json"));
        var editorRoot = ResolveEditorRoot(args);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(projectRoot, "EditorWorkspace"));
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("Current process path is unavailable.");
        var hostedByDotnet = Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        var backendArguments = hostedByDotnet
            ? new[] { Path.Combine(AppContext.BaseDirectory, (Assembly.GetEntryAssembly()?.GetName().Name ?? throw new InvalidOperationException("CLI assembly name is unavailable.")) + ".dll") }
            : Array.Empty<string>();
        if (hostedByDotnet && !File.Exists(backendArguments[0])) throw new InvalidOperationException("CLI assembly path is unavailable.");
        var includeRuntimeProxies = HasOption(args, "--include-runtime-proxies");
        var workspace = new EditorWorkspaceGenerator().Create(state, ir, editorRoot, output, !includeRuntimeProxies, processPath, backendArguments);
        var meshIndex = new RecoveryMeshWorkspaceGenerator().Create(Path.Combine(state, "baseline"), ir, Path.Combine(output, "MeshWorkspaces"));
        var previewManifestPath = Path.Combine(output, "mesh-preview-import.json");
        var previewManifest = new MeshPreviewImportWriter().Write(meshIndex, Path.Combine(output, "MeshPreviewSources"), previewManifestPath);
        workspace = workspace with { MeshPreviewImport = previewManifestPath };
        ReconJson.Write(Path.Combine(output, "editor-workspace.json"), workspace);
        var scene = ReadJson<ProxyScene>(workspace.ProxyScene); var overlays = ReadJson<CollisionOverlayIndex>(workspace.CollisionOverlayIndex);
        Console.WriteLine($"Generated editor workspace: {output}"); Console.WriteLine($"Mode: {workspace.WorkspaceMode}"); Console.WriteLine($"Proxy objects: {scene.Objects.Count}; exact collision overlays: {overlays.Entries.Count}");
        Console.WriteLine($"Mesh workspaces: {meshIndex.Entries.Count(x => x.Status != "unsupported")}; unsupported meshes: {meshIndex.Entries.Count(x => x.Status == "unsupported")}");
        Console.WriteLine($"Recovered mesh previews prepared: {previewManifest.Entries.Count}");
        Console.WriteLine($"Editor map: {workspace.MapPackagePath}"); Console.WriteLine("Run run-editor-import to materialize and export the proxy map in a separate editor process."); return meshIndex.Entries.Any(x => x.Status == "unsupported") ? 2 : 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int RunEditorImport(string[] args)
{
    if (args.Length < 2) return Fail("run-editor-import requires an editor workspace.");
    try
    {
        var root = Path.GetFullPath(args[1]); var workspacePath = Path.Combine(root, "editor-workspace.json"); var workspace = ReadJson<EditorWorkspace>(workspacePath);
        VerifyFile(workspace.Editor.Path, workspace.Editor); var log = Path.Combine(root, "editor-import.log"); var exitCode = new EditorWorkspaceGenerator().RunImport(workspace, log);
        if (exitCode != 0 || !File.Exists(workspace.ExportT3d)) throw new InvalidOperationException($"Editor import/export failed with exit code {exitCode}. See {log}");
        ReconJson.Write(workspacePath, workspace with { Status = "imported-and-exported" });
        Console.WriteLine($"Editor proxy map imported and exported: {workspace.ExportT3d}"); Console.WriteLine($"Log: {log}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int OpenEditorWorkspace(string[] args)
{
    if (args.Length < 2) return Fail("open-editor-workspace requires an editor workspace.");
    try
    {
        var root = Path.GetFullPath(args[1]); var workspace = ReadJson<EditorWorkspace>(Path.Combine(root, "editor-workspace.json"));
        VerifyFile(workspace.Editor.Path, workspace.Editor); var processId = new EditorWorkspaceGenerator().Open(workspace);
        Console.WriteLine($"Opened editor workspace in process {processId}."); Console.WriteLine("Save the map and close the editor before running run-editor-export."); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int RunEditorExport(string[] args)
{
    if (args.Length < 2) return Fail("run-editor-export requires an editor workspace.");
    try
    {
        var root = Path.GetFullPath(args[1]); var workspacePath = Path.Combine(root, "editor-workspace.json"); var workspace = ReadJson<EditorWorkspace>(workspacePath);
        VerifyFile(workspace.Editor.Path, workspace.Editor); var log = Path.Combine(root, "editor-export.log"); var exitCode = new EditorWorkspaceGenerator().RunExport(workspace, log);
        if (exitCode != 0 || !File.Exists(workspace.ExportT3d)) throw new InvalidOperationException($"Editor export failed with exit code {exitCode}. See {log}");
        var mapCheck = new EditorLogAnalyzer().AnalyzeMapCheck(log); var mapCheckPath = Path.Combine(root, "editor-map-check.json"); ReconJson.Write(mapCheckPath, mapCheck);
        ReconJson.Write(workspacePath, workspace with { Status = "exported-for-diff" });
        Console.WriteLine($"Exported saved editor map: {workspace.ExportT3d}"); Console.WriteLine($"Map Check: {mapCheck.Errors} error(s), {mapCheck.Warnings} warning(s)");
        Console.WriteLine($"Map Check report: {mapCheckPath}"); Console.WriteLine($"Log: {log}"); return mapCheck.Errors == 0 && mapCheck.Warnings == 0 ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int ExportEditorEdits(string[] args)
{
    if (args.Length < 3) return Fail("export-editor-edits requires a recovery project and editor workspace.");
    try
    {
        var state = ResolveState(args[1]); var workspaceRoot = Path.GetFullPath(args[2]); var workspace = ReadJson<EditorWorkspace>(Path.Combine(workspaceRoot, "editor-workspace.json"));
        if (!File.Exists(workspace.ExportT3d)) throw new FileNotFoundException("The editor workspace has not been exported.", workspace.ExportT3d);
        var result = new EditorDiffExporter().Export(state, workspace); var manifestPath = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json"));
        ReconJson.Write(manifestPath, result.Manifest); var reportPath = Path.Combine(state, "reports", "editor-diff.json"); ReconJson.Write(reportPath, result.Report);
        Console.WriteLine($"Matched reconstruction IDs: {result.Report.MatchedTaggedObjects}/{result.Report.ExpectedTaggedObjects}");
        Console.WriteLine($"Changed properties: {result.Report.ChangedProperties}"); Console.WriteLine($"Manifest: {manifestPath}"); Console.WriteLine($"Report: {reportPath}"); return result.Report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int ExtractAsset(string[] args)
{
    if (args.Length < 2) return Fail("extract-asset requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json")); var objectId = RequiredOption(args, "--object");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Directory.GetParent(state)!.FullName, "AssetBundle-" + objectId[..Math.Min(12, objectId.Length)]));
        var manifest = new AssetInterchangeService().Extract(state, ir, objectId, output);
        Console.WriteLine($"Extracted {manifest.Kind}: {manifest.RootObjectPath}"); Console.WriteLine($"Closure objects: {manifest.Closure.Count}; included dependencies: {manifest.Dependencies.Count(x => x.Included)}; external dependencies: {manifest.Dependencies.Count(x => !x.Included)}");
        Console.WriteLine($"Bundle: {output}"); return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int ListExtractable(string[] args)
{
    if (args.Length < 2) return Fail("list-extractable requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json"));
        var pattern = GetOption(args, "--match"); var patcher = new PropertyPatcher(); var results = new List<object>();
        foreach (var package in ir.Packages)
        {
            try { EnsureDirectWritablePackage(package); }
            catch (NotSupportedException) { continue; }
            var baseline = BaselinePath(state, package.InternalPath); UAssetAPI.UAsset? asset = null;
            foreach (var item in package.Objects)
            {
                if (pattern is not null && !item.ObjectPath.Contains(pattern, StringComparison.OrdinalIgnoreCase) && !(item.ClassPath?.Contains(pattern, StringComparison.OrdinalIgnoreCase) ?? false)) continue;
                AssetReplacementKind? kind = item.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) == true ? AssetReplacementKind.StaticMeshCollision
                    : item.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) == true ? AssetReplacementKind.BspModelClosure : null;
                IReadOnlyList<string> properties = [];
                if (kind is null)
                {
                    try
                    {
                        asset ??= new UAssetAPI.UAsset(baseline, UAssetAPI.UnrealTypes.EngineVersion.VER_UE4_15);
                        properties = patcher.ReadEditableProperties(asset, item.ExportIndex).Select(x => x.PropertyPath).ToArray();
                        if (properties.Count != 0) kind = AssetReplacementKind.PropertySet;
                    }
                    catch (NotSupportedException) { }
                }
                if (kind is not null) results.Add(new { package.PackagePath, item.Id, item.ObjectPath, item.ClassPath, Kind = kind, EditableProperties = properties });
            }
        }
        var document = new { schemaVersion = 1, profile = ir.Profile, count = results.Count, objects = results };
        var output = GetOption(args, "--output");
        if (output is null) Console.WriteLine(JsonSerializer.Serialize(document, ReconJson.Options));
        else { output = Path.GetFullPath(output); ReconJson.Write(output, document); Console.WriteLine($"Extractable objects: {results.Count}"); Console.WriteLine($"Inventory: {output}"); }
        return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int ValidateDonor(string[] args)
{
    if (args.Length < 3) return Fail("validate-donor requires an asset bundle and cooked donor package.");
    try
    {
        var bundle = Path.GetFullPath(args[1]); var donor = Path.GetFullPath(args[2]); var extraction = ReadJson<AssetExtractionManifest>(Path.Combine(bundle, "extraction-manifest.json")); var mode = ParseDonorMode(GetOption(args, "--mode"), extraction.Kind);
        var report = new AssetInterchangeService().ValidateDonor(bundle, donor, mode);
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(bundle, "donor-validation.json")); ReconJson.Write(output, report);
        Console.WriteLine($"Donor valid: {report.Passed}; changed closure exports: {report.ChangedExportIndices.Count}"); Console.WriteLine($"Report: {output}"); return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int InjectAsset(string[] args)
{
    if (args.Length < 3) return Fail("inject requires a recovery project and asset bundle.");
    try
    {
        var state = ResolveState(args[1]); var bundle = Path.GetFullPath(args[2]); var donorPath = Path.GetFullPath(RequiredOption(args, "--donor"));
        var extractionPath = Path.Combine(bundle, "extraction-manifest.json"); var extraction = ReadJson<AssetExtractionManifest>(extractionPath); var mode = ParseDonorMode(GetOption(args, "--mode"), extraction.Kind);
        var contractPath = Path.Combine(bundle, extraction.ReplacementContract.RelativePath); VerifyFile(contractPath, extraction.ReplacementContract.File); var contract = ReadJson<ReplacementContract>(contractPath);
        var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var irIdentity = Identity.File(irPath, "reconstruction-ir.json");
        if (extraction.Profile != ir.Profile || extraction.InputPak.Sha256 != ir.InputPak.Sha256 || extraction.ReconstructionIr.Sha256 != irIdentity.Sha256)
            throw new InvalidDataException("Asset bundle belongs to a different recovery project or reconstruction IR.");
        var package = ir.Packages.Single(x => x.PackagePath == extraction.PackagePath); EnsureDirectWritablePackage(package);
        var root = package.Objects.Single(x => x.Id == extraction.RootObjectId); if (root.PayloadHash != contract.OriginalRootPayloadHash) throw new InvalidDataException("Replacement contract root hash does not match the project baseline.");
        VerifyFile(BaselinePath(state, package.InternalPath), extraction.BaselineFiles.Single(x => x.Role == "baseline-package").File);

        var validation = new AssetInterchangeService().ValidateDonor(bundle, donorPath, mode); var validationPath = Path.Combine(bundle, "donor-validation.json"); ReconJson.Write(validationPath, validation);
        if (!validation.Passed) throw new InvalidDataException("Cooked donor failed replacement-contract validation: " + string.Join("; ", validation.Diagnostics));
        var donor = Identity.File(donorPath); var operations = new List<EditOperation>();
        if (extraction.Kind == AssetReplacementKind.PropertySet)
        {
            foreach (var change in new AssetInterchangeService().PropertyChanges(bundle, donorPath))
                operations.Add(new EditOperation("set-property", package.PackagePath, root.Id, root.ObjectPath, root.PayloadHash, change.Before.PropertyPath,
                    change.Before.Value, change.After.Value, "asset-bundle", change.Before.SupportRule));
        }
        else if (extraction.Kind == AssetReplacementKind.StaticMeshCollision)
        {
            var before = JsonSerializer.SerializeToElement(new { bodySetupPayloadHash = root.PayloadHash }, ReconJson.Options); var after = JsonSerializer.SerializeToElement(new { donor }, ReconJson.Options);
            operations.Add(new EditOperation("replace-collision", package.PackagePath, root.Id, root.ObjectPath, root.PayloadHash, "$collisionClosure", before, after, "asset-bundle", "cooked-bodysetup-navcollision-graft-v1"));
        }
        else
        {
            var supportRule = mode == DonorMode.FreshShell ? "fresh-cooked-bsp-model-closure-graft-v1" : "cooked-bsp-model-closure-graft-v1";
            var before = JsonSerializer.SerializeToElement(new { modelPayloadHash = root.PayloadHash }, ReconJson.Options); var after = JsonSerializer.SerializeToElement(new { donor, freshShell = mode == DonorMode.FreshShell }, ReconJson.Options);
            operations.Add(new EditOperation("replace-bsp", package.PackagePath, root.Id, root.ObjectPath, root.PayloadHash, "$bspClosure", before, after, "asset-bundle", supportRule));
        }
        if (operations.Count == 0) throw new InvalidDataException("Validated donor produced no injection operations.");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != irIdentity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (operations.Any(operation => existing.Operations.Any(x => x.PackagePath == operation.PackagePath && x.ObjectId == operation.ObjectId && x.PropertyPath == operation.PropertyPath))) throw new InvalidDataException("Existing manifest already contains one of the injected object/property operations.");
            manifest = existing with { Operations = [.. existing.Operations, .. operations] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, irIdentity, operations);
        ReconJson.Write(output, manifest);
        var reportPath = Path.GetFullPath(GetOption(args, "--report") ?? output + ".injection.json");
        var injection = new InjectionReport(FormatVersions.InjectionReport, contract.ContractId, Identity.File(extractionPath), Identity.File(validationPath), package.PackagePath, root.Id,
            operations.Select(x => x.Operation).ToArray(), operations.Select(x => x.SupportRule).Distinct(StringComparer.Ordinal).ToArray(), output, true); ReconJson.Write(reportPath, injection);
        Console.WriteLine($"Created {operations.Count} validated operation(s) for {root.ObjectPath}"); Console.WriteLine($"Manifest: {output}"); Console.WriteLine($"Report: {reportPath}"); return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int DeleteActor(string[] args)
{
    if (args.Length < 2) return Fail("delete-actor requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var objectId = RequiredOption(args, "--object");
        var matches = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item))).Where(x => x.Object.Id == objectId).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Object ID resolved to {matches.Length} exports instead of one.");
        var match = matches[0]; EnsureDirectWritablePackage(match.Package); var baseline = BaselinePath(state, match.Package.InternalPath); VerifyFile(baseline, match.Package.File);
        if (!new PropertyPatcher().IsPersistentLevelActor(baseline, match.Object.ExportIndex)) throw new InvalidOperationException("Only persistent-level actors can be deleted.");
        if (match.Object.Support is SupportLevel.Unsupported or SupportLevel.ProxyOnly) throw new NotSupportedException("This actor's support level does not permit deletion.");
        var before = JsonSerializer.SerializeToElement(true); var after = JsonSerializer.SerializeToElement(false);
        var operation = new EditOperation("delete-actor", match.Package.PackagePath, objectId, match.Object.ObjectPath, match.Object.PayloadHash, "$levelActorReference", before, after, "cli", "persistent-level-actor-delete-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var irIdentity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != irIdentity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.Operation == "delete-actor" && x.ObjectId == objectId)) throw new InvalidDataException("Existing manifest already deletes this actor."); manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, irIdentity, [operation]);
        ReconJson.Write(output, manifest); Console.WriteLine($"Created actor deletion: {match.Object.ObjectPath}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CloneActor(string[] args)
{
    if (args.Length < 2) return Fail("clone-actor requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath); var templateId = RequiredOption(args, "--template");
        var name = RequiredOption(args, "--name"); using var locationDocument = JsonDocument.Parse(RequiredOption(args, "--location")); var location = locationDocument.RootElement.Clone();
        var match = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item))).Single(x => x.Object.Id == templateId);
        var isStaticMesh = match.Object.ClassPath == "/Script/Engine.StaticMeshActor"; var isBlockingVolume = match.Object.ClassPath == "/Script/Engine.BlockingVolume";
        if (!isStaticMesh && !isBlockingVolume) throw new NotSupportedException("Clone addition supports StaticMeshActor and BlockingVolume templates only.");
        JsonElement? rotation = null; var rotationText = GetOption(args, "--rotation");
        if (rotationText is not null) { using var rotationDocument = JsonDocument.Parse(rotationText); rotation = rotationDocument.RootElement.Clone(); }
        if (isBlockingVolume && rotation is null) throw new ArgumentException("BlockingVolume cloning requires --rotation <json>.");
        var after = JsonSerializer.SerializeToElement(new { name, location, rotation }, ReconJson.Options); var operation = new EditOperation("clone-actor", match.Package.PackagePath, templateId, match.Object.ObjectPath, match.Object.PayloadHash,
            "$actorClone", JsonSerializer.SerializeToElement<object?>(null), after, "cli", isBlockingVolume ? "blocking-volume-closure-clone-v1" : "simple-static-mesh-actor-clone-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var identity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != identity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.Operation == "clone-actor" && x.After.ValueKind == JsonValueKind.Object && x.After.TryGetProperty("name", out var existingName) && existingName.GetString() == name)) throw new InvalidDataException("Existing manifest already adds an actor with this name."); manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, identity, [operation]); ReconJson.Write(output, manifest);
        Console.WriteLine($"Created {match.Object.ClassPath!.Split('.').Last()} clone from {match.Object.ObjectPath} as {name}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CertifyCollisionBoxTemplate(string[] args)
{
    if (args.Length < 2) return Fail("certify-collision-box-template requires a cooked template map.");
    try
    {
        var source = Path.GetFullPath(args[1]); var output = Path.GetFullPath(RequiredOption(args, "--output"));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Template output directory is not empty.");
        Directory.CreateDirectory(output); var package = Path.Combine(output, "CollisionBoxTemplate.umap"); File.Copy(source, package);
        const string profile = "ut4-4.15-windows-no-editor-v1", internalPath = "UnrealTournament/Content/UT4Recon/Templates/CollisionBoxTemplate.umap";
        var manifest = new CollisionBoxTemplateService().Certify(package, internalPath, profile); var manifestPath = Path.Combine(output, "collision-box-template.json"); ReconJson.Write(manifestPath, manifest);
        new CollisionBoxTemplateService().ValidateBundle(manifestPath, profile);
        Console.WriteLine($"Certified {manifest.Dimensions.X:g} x {manifest.Dimensions.Y:g} x {manifest.Dimensions.Z:g} collision box template.");
        Console.WriteLine($"Closure exports: {string.Join(", ", manifest.Closure.Select(x => x.Role + "#" + x.ExportIndex))}"); Console.WriteLine($"Manifest: {manifestPath}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int AddCollisionBox(string[] args)
{
    if (args.Length < 2) return Fail("add-collision-box requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath);
        var map = ir.Packages.Single(x => x.ContainsMap); var level = map.Objects.Single(x => x.ClassPath == "/Script/Engine.Level");
        var templatePath = Path.GetFullPath(RequiredOption(args, "--template")); var template = new CollisionBoxTemplateService().ValidateBundle(templatePath, ir.Profile);
        var name = RequiredOption(args, "--name"); var locationText = RequiredOption(args, "--location"); var rotationText = RequiredOption(args, "--rotation");
        using var locationDocument = JsonDocument.Parse(locationText); using var rotationDocument = JsonDocument.Parse(rotationText);
        JsonElement? scale = null; var scaleText = GetOption(args, "--scale"); if (scaleText is not null) { using var scaleDocument = JsonDocument.Parse(scaleText); scale = scaleDocument.RootElement.Clone(); }
        var afterValues = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(name), ["location"] = locationDocument.RootElement.Clone(), ["rotation"] = rotationDocument.RootElement.Clone(),
            ["template"] = JsonSerializer.SerializeToElement(Identity.File(templatePath), ReconJson.Options)
        };
        if (scale is JsonElement scaleValue) afterValues["scale"] = scaleValue;
        var operation = new EditOperation("clone-actor", map.PackagePath, level.Id, level.ObjectPath, level.PayloadHash, "$actorTemplateClone",
            JsonSerializer.SerializeToElement<object?>(null), JsonSerializer.SerializeToElement(afterValues, ReconJson.Options), "collision-box-template", "certified-blocking-volume-box-template-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var identity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != identity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.Operation == "clone-actor" && x.After.TryGetProperty("name", out var existingName) && existingName.GetString() == name)) throw new InvalidDataException("Existing manifest already adds an actor with this name.");
            manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, identity, [operation]);
        ReconJson.Write(output, manifest); Console.WriteLine($"Added certified collision box operation '{name}' from template {template.GeometryFingerprint[..12]}."); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int CreateCustomCollisionWorkspace(string[] args)
{
    try
    {
        var output = Path.GetFullPath(RequiredOption(args, "--output")); var editor = ResolveEditorRoot(args);
        var source = GetOption(args, "--source") ?? FindBundledFile(Path.Combine("fixtures", "collision-box-template", "source", "CollisionBoxTemplate.t3d"));
        var workspace = new CustomCollisionWorkspaceService().Create(editor, output, source);
        Console.WriteLine($"Created isolated custom collision workspace: {output}"); Console.WriteLine($"Map: {workspace.MapPackagePath}"); Console.WriteLine("Run run-custom-collision-import once, then open-custom-collision-workspace."); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int RunCustomCollisionImport(string[] args)
{
    if (args.Length < 2) return Fail("run-custom-collision-import requires a custom collision workspace.");
    try { new CustomCollisionWorkspaceService().Import(args[1]); Console.WriteLine("Starter BlockingVolume imported. The workspace is ready for authoring."); return 0; }
    catch (Exception error) { return Fail(error.Message); }
}

static int OpenCustomCollisionWorkspace(string[] args)
{
    if (args.Length < 2) return Fail("open-custom-collision-workspace requires a custom collision workspace.");
    try { var process = new CustomCollisionWorkspaceService().Open(args[1]); Console.WriteLine($"Opened custom collision editor in process {process}."); return 0; }
    catch (Exception error) { return Fail(error.Message); }
}

static int CookCustomCollisionDonor(string[] args)
{
    if (args.Length < 2) return Fail("cook-custom-collision-donor requires a custom collision workspace.");
    try
    {
        var manifest = new CustomCollisionWorkspaceService().CookAndCertify(args[1]);
        Console.WriteLine($"Cooked and certified custom collision: {manifest.PolygonCount} polygons, {manifest.VertexCount} unique vertices.");
        Console.WriteLine($"Manifest: {Path.Combine(Path.GetFullPath(args[1]), "custom-collision-donor.json")}"); return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static int AddCustomCollision(string[] args)
{
    if (args.Length < 2) return Fail("add-custom-collision requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath);
        var map = ir.Packages.Single(x => x.ContainsMap); var level = map.Objects.Single(x => x.ClassPath == "/Script/Engine.Level");
        var donorPath = Path.GetFullPath(RequiredOption(args, "--donor")); var donor = new CollisionBoxTemplateService().ValidateCustomBundle(donorPath, ir.Profile);
        var name = RequiredOption(args, "--name"); using var locationDocument = JsonDocument.Parse(RequiredOption(args, "--location")); using var rotationDocument = JsonDocument.Parse(RequiredOption(args, "--rotation"));
        JsonElement? scale = null; var scaleText = GetOption(args, "--scale"); if (scaleText is not null) { using var scaleDocument = JsonDocument.Parse(scaleText); scale = scaleDocument.RootElement.Clone(); }
        var afterValues = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(name), ["location"] = locationDocument.RootElement.Clone(), ["rotation"] = rotationDocument.RootElement.Clone(),
            ["donor"] = JsonSerializer.SerializeToElement(Identity.File(donorPath), ReconJson.Options)
        };
        if (scale is JsonElement scaleValue) afterValues["scale"] = scaleValue;
        var operation = new EditOperation("clone-actor", map.PackagePath, level.Id, level.ObjectPath, level.PayloadHash, "$actorCustomDonor", JsonSerializer.SerializeToElement<object?>(null),
            JsonSerializer.SerializeToElement(afterValues, ReconJson.Options), "custom-collision-donor", "custom-blocking-volume-donor-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var identity = Identity.File(irPath, "reconstruction-ir.json"); EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output); if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != identity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.Operation == "clone-actor" && x.After.TryGetProperty("name", out var existingName) && existingName.GetString() == name)) throw new InvalidDataException("Existing manifest already adds an actor with this name.");
            manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, identity, [operation]);
        ReconJson.Write(output, manifest); Console.WriteLine($"Added custom collision operation '{name}' from certified donor {donor.GeometryFingerprint[..12]}."); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int NewEdit(string[] args)
{
    if (args.Length < 2) return Fail("new-edit requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath);
        var objectId = RequiredOption(args, "--object"); var propertyPath = RequiredOption(args, "--property"); var afterText = RequiredOption(args, "--after");
        var rule = EditRules.RuleFor(propertyPath) ?? throw new InvalidOperationException($"Property is not allowlisted: {propertyPath}");
        var matches = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item))).Where(x => x.Object.Id.Equals(objectId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Object ID resolved to {matches.Length} exports instead of one.");
        var match = matches[0]; EnsureDirectWritablePackage(match.Package);
        if (rule == "ut-level-summary-title-v1" && match.Object.ClassPath != "/Script/UnrealTournament.UTLevelSummary")
            throw new InvalidOperationException("The Title editing rule is limited to UUTLevelSummary exports.");
        var baseline = BaselinePath(state, match.Package.InternalPath); VerifyFile(baseline, match.Package.File);
        foreach (var sidecar in match.Package.Sidecars) VerifyFile(BaselinePath(state, sidecar.Path), sidecar);
        var current = new PackageInspector().Inspect(baseline, match.Package.InternalPath).Objects.Single(x => x.Id == objectId);
        if (current.PayloadHash != match.Object.PayloadHash) throw new InvalidDataException("Baseline export payload no longer matches the reconstruction IR.");
        var patcher = new PropertyPatcher(); var before = patcher.ReadValue(baseline, match.Object.ExportIndex, propertyPath);
        using var afterDocument = JsonDocument.Parse(afterText); var after = afterDocument.RootElement.Clone();
        if (PropertyPatcher.JsonEquivalent(before, after)) throw new InvalidOperationException("The requested edit does not change the property.");
        var operation = new EditOperation("set-property", match.Package.PackagePath, objectId, match.Object.ObjectPath, match.Object.PayloadHash, propertyPath, before, after, "cli", rule);
        var irIdentity = Identity.File(irPath, "reconstruction-ir.json"); var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json"));
        EditManifest manifest;
        if (HasOption(args, "--append") && File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output);
            if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != irIdentity.Sha256) throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            if (existing.Operations.Any(x => x.ObjectId == objectId && x.PropertyPath == propertyPath)) throw new InvalidDataException("Existing manifest already edits this object property.");
            manifest = existing with { Operations = [.. existing.Operations, operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, irIdentity, [operation]);
        ReconJson.Write(output, manifest);
        Console.WriteLine($"Created edit for {match.Object.ObjectPath}.{propertyPath}"); Console.WriteLine($"Before: {before.GetRawText()}"); Console.WriteLine($"After:  {after.GetRawText()}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int SetMapTitle(string[] args)
{
    if (args.Length < 2) return Fail("set-map-title requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]); var title = RequiredOption(args, "--title");
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Map title must not be empty.");
        var irPath = Path.Combine(state, "reconstruction-ir.json"); var ir = ReadJson<ReconstructionIr>(irPath);
        var match = ir.Packages.SelectMany(package => package.Objects.Select(item => (Package: package, Object: item)))
            .Single(x => x.Object.ClassPath == "/Script/UnrealTournament.UTLevelSummary");
        EnsureDirectWritablePackage(match.Package); var baseline = BaselinePath(state, match.Package.InternalPath); VerifyFile(baseline, match.Package.File);
        var before = new PropertyPatcher().ReadValue(baseline, match.Object.ExportIndex, "Title");
        var after = JsonSerializer.SerializeToElement(title, ReconJson.Options);
        if (PropertyPatcher.JsonEquivalent(before, after)) throw new InvalidOperationException("The distinct map title is identical to the cooked baseline title.");
        var operation = new EditOperation("set-property", match.Package.PackagePath, match.Object.Id, match.Object.ObjectPath, match.Object.PayloadHash,
            "Title", before, after, "editor-panel", "ut-level-summary-title-v1");
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "edit-manifest.json")); var irIdentity = Identity.File(irPath, "reconstruction-ir.json");
        EditManifest manifest;
        if (File.Exists(output))
        {
            var existing = ReadJson<EditManifest>(output);
            if (existing.Profile != ir.Profile || existing.InputPak.Sha256 != ir.InputPak.Sha256 || existing.ReconstructionIr.Sha256 != irIdentity.Sha256)
                throw new InvalidDataException("Existing edit manifest belongs to different project state.");
            manifest = existing with { Operations = [.. existing.Operations.Where(x => x.SupportRule != "ut-level-summary-title-v1"), operation] };
        }
        else manifest = new EditManifest(FormatVersions.EditManifest, ir.Profile, ir.InputPak, irIdentity, [operation]);
        ReconJson.Write(output, manifest); Console.WriteLine($"Set distinct map title to: {title}"); Console.WriteLine($"Manifest: {output}"); return 0;
    }
    catch (Exception error) { return Fail(error.Message); }
}

static int BuildProject(string[] args)
{
    if (args.Length < 2) return Fail("build requires a recovery project.");
    string? staging = null;
    try
    {
        var projectRoot = Path.GetFullPath(args[1]); var state = ResolveState(projectRoot); var irPath = Path.Combine(state, "reconstruction-ir.json");
        var inputManifest = ReadJson<InputManifest>(Path.Combine(state, "input-manifest.json")); var ir = ReadJson<ReconstructionIr>(irPath);
        var editPath = Path.GetFullPath(GetOption(args, "--manifest") ?? Path.Combine(state, "edit-manifest.json")); var edits = ReadJson<EditManifest>(editPath);
        if (edits.SchemaVersion != FormatVersions.EditManifest || edits.Profile != ir.Profile || inputManifest.Profile != ir.Profile) throw new InvalidDataException("Manifest schema or profile mismatch.");
        VerifyFile(irPath, edits.ReconstructionIr); VerifyFile(inputManifest.Pak.Source.Path, edits.InputPak);
        if (edits.InputPak.Sha256 != ir.InputPak.Sha256 || edits.Operations.Count == 0) throw new InvalidDataException("Edit manifest input identity is invalid or contains no operations.");
        if (edits.Operations.Where(x => x.Operation != "clone-actor").GroupBy(x => (x.ObjectId, x.PropertyPath)).Any(x => x.Count() != 1)) throw new InvalidDataException("The manifest edits the same object property more than once.");
        if (edits.Operations.Where(x => x.Operation == "clone-actor").Select(x => x.After.GetProperty("name").GetString()).GroupBy(x => x, StringComparer.Ordinal).Any(x => x.Count() != 1)) throw new InvalidDataException("The manifest adds more than one actor with the same name.");

        var baselineRoot = Path.GetFullPath(Path.Combine(state, "baseline")); var behaviorPath = Path.Combine(state, "behavior-baseline.json"); var behaviorAnalyzer = new BehaviorAnalyzer();
        var expectedBehavior = behaviorAnalyzer.InspectProject(baselineRoot, ir); if (File.Exists(behaviorPath)) { var recordedBehavior = ReadJson<BehaviorSnapshot>(behaviorPath); var baselineBehaviorCheck = new BehaviorValidator().Compare(recordedBehavior, expectedBehavior); if (!baselineBehaviorCheck.Passed) throw new InvalidDataException("Recorded behavior baseline does not match the immutable cooked baseline: " + string.Join("; ", baselineBehaviorCheck.Differences)); }
        var buildRoot = Path.GetFullPath(Path.Combine(state, "build")); Directory.CreateDirectory(buildRoot); staging = Path.GetFullPath(Path.Combine(buildRoot, "staging-" + Guid.NewGuid().ToString("N")));
        if (!staging.StartsWith(buildRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe build staging path.");
        Directory.CreateDirectory(staging);
        foreach (var entry in inputManifest.Pak.Entries)
        {
            var source = BaselinePath(state, entry.Path); var expected = new FileIdentity(entry.Path, entry.Size, entry.Sha256); VerifyFile(source, expected);
            var destination = Path.GetFullPath(Path.Combine(staging, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pak entry escapes staging.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination); File.SetAttributes(destination, FileAttributes.Normal);
        }

        var inspector = new PackageInspector(); var patcher = new PropertyPatcher(); var changeApprovals = new List<(string PackagePath, string ObjectId, string Property)>(); var addedApprovals = new List<(string PackagePath, int ExportIndex)>();
        var allowedBehaviorDependencyAdditions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var operation in edits.Operations)
        {
            var package = ir.Packages.SingleOrDefault(x => x.PackagePath == operation.PackagePath) ?? throw new KeyNotFoundException($"Package is absent from IR: {operation.PackagePath}");
            EnsureDirectWritablePackage(package);
            var item = package.Objects.SingleOrDefault(x => x.Id == operation.ObjectId) ?? throw new KeyNotFoundException($"Object is absent from IR: {operation.ObjectId}");
            if (item.ObjectPath != operation.ObjectPath || item.PayloadHash != operation.OriginalPayloadHash) throw new InvalidDataException("Edit operation object identity or original payload hash is stale.");
            var packagePath = Path.GetFullPath(Path.Combine(staging, package.InternalPath.Replace('/', Path.DirectorySeparatorChar)));
            var temporary = Path.Combine(Path.GetDirectoryName(packagePath)!, $".{Path.GetFileNameWithoutExtension(packagePath)}.patching-{Guid.NewGuid():N}{Path.GetExtension(packagePath)}");
            if (operation.Operation == "set-property")
            {
                var expectedRule = EditRules.RuleFor(operation.PropertyPath) ?? throw new InvalidOperationException($"Property is not allowlisted: {operation.PropertyPath}");
                if (expectedRule != operation.SupportRule) throw new InvalidDataException("Edit operation support rule is invalid.");
                if (expectedRule == "ut-level-summary-title-v1" && item.ClassPath != "/Script/UnrealTournament.UTLevelSummary")
                    throw new InvalidDataException("The Title editing rule is limited to UUTLevelSummary exports.");
                patcher.Apply(packagePath, temporary, item.ExportIndex, operation.PropertyPath, operation.Before, operation.After);
                changeApprovals.Add((package.PackagePath, item.Id, operation.PropertyPath));
            }
            else if (operation.Operation == "delete-actor")
            {
                if (operation.SupportRule != "persistent-level-actor-delete-v1" || operation.PropertyPath != "$levelActorReference" || operation.Before.ValueKind != JsonValueKind.True || operation.After.ValueKind != JsonValueKind.False)
                    throw new InvalidDataException("Actor deletion operation is invalid.");
                var deletion = patcher.DeleteActor(packagePath, temporary, item.ExportIndex); var levelObject = package.Objects.Single(x => x.ExportIndex == deletion.LevelExportIndex);
                changeApprovals.Add((package.PackagePath, levelObject.Id, "delete-actor:" + item.ObjectPath));
            }
            else if (operation.Operation == "clone-actor")
            {
                if (operation.SupportRule is not ("simple-static-mesh-actor-clone-v1" or "blocking-volume-closure-clone-v1" or "certified-blocking-volume-box-template-v1" or "custom-blocking-volume-donor-v1") ||
                    operation.PropertyPath is not ("$actorClone" or "$actorTemplateClone" or "$actorCustomDonor") || operation.After.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Actor clone operation is invalid.");
                var expectedCloneProperty = operation.SupportRule switch { "certified-blocking-volume-box-template-v1" => "$actorTemplateClone", "custom-blocking-volume-donor-v1" => "$actorCustomDonor", _ => "$actorClone" };
                if (operation.PropertyPath != expectedCloneProperty) throw new InvalidDataException("Template, donor, and source-actor clone operations must use their matching operation property.");
                var name = operation.After.GetProperty("name").GetString() ?? throw new InvalidDataException("Clone name is missing."); var location = operation.After.GetProperty("location");
                ActorCloneResult clone;
                if (operation.SupportRule == "certified-blocking-volume-box-template-v1")
                {
                    if (item.ClassPath != "/Script/Engine.Level" || operation.PropertyPath != "$actorTemplateClone") throw new InvalidDataException("Certified collision box operation must be anchored to the map Level export.");
                    var templateIdentity = operation.After.GetProperty("template").Deserialize<FileIdentity>(ReconJson.Options) ?? throw new InvalidDataException("Collision box template identity is missing."); VerifyFile(templateIdentity.Path, templateIdentity);
                    var templateService = new CollisionBoxTemplateService();
                    var template = templateService.ValidateBundle(templateIdentity.Path, ir.Profile);
                    if (!allowedBehaviorDependencyAdditions.TryGetValue(package.InternalPath, out var allowedImports))
                        allowedBehaviorDependencyAdditions[package.InternalPath] = allowedImports = new HashSet<string>(StringComparer.Ordinal);
                    allowedImports.UnionWith(template.RequiredImportPackages);
                    clone = templateService.AddToMap(packagePath, temporary, templateIdentity.Path, ir.Profile, name, location, operation.After.GetProperty("rotation"), operation.After.TryGetProperty("scale", out var scale) ? scale : null);
                }
                else if (operation.SupportRule == "custom-blocking-volume-donor-v1")
                {
                    if (item.ClassPath != "/Script/Engine.Level") throw new InvalidDataException("Custom collision operation must be anchored to the map Level export.");
                    var donorIdentity = operation.After.GetProperty("donor").Deserialize<FileIdentity>(ReconJson.Options) ?? throw new InvalidDataException("Custom collision donor identity is missing."); VerifyFile(donorIdentity.Path, donorIdentity);
                    var donorService = new CollisionBoxTemplateService(); var donor = donorService.ValidateCustomBundle(donorIdentity.Path, ir.Profile);
                    if (!allowedBehaviorDependencyAdditions.TryGetValue(package.InternalPath, out var allowedImports)) allowedBehaviorDependencyAdditions[package.InternalPath] = allowedImports = new HashSet<string>(StringComparer.Ordinal);
                    allowedImports.UnionWith(donor.RequiredImportPackages);
                    clone = donorService.AddCustomToMap(packagePath, temporary, donorIdentity.Path, ir.Profile, name, location, operation.After.GetProperty("rotation"), operation.After.TryGetProperty("scale", out var scale) ? scale : null);
                }
                else clone = operation.SupportRule == "blocking-volume-closure-clone-v1"
                    ? patcher.CloneBlockingVolume(packagePath, temporary, item.ExportIndex, name, location, operation.After.GetProperty("rotation"), operation.After.TryGetProperty("scale", out var scale) ? scale : null)
                    : patcher.CloneStaticMeshActor(packagePath, temporary, item.ExportIndex, name, location);
                var levelObject = package.Objects.Single(x => x.ExportIndex == clone.LevelExportIndex);
                changeApprovals.Add((package.PackagePath, levelObject.Id, "clone-actor:" + name)); foreach (var index in clone.AddedExportIndexes) addedApprovals.Add((package.PackagePath, index));
            }
            else if (operation.Operation == "replace-collision")
            {
                if (operation.SupportRule != "cooked-bodysetup-navcollision-graft-v1" || operation.PropertyPath != "$collisionClosure" || operation.After.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Collision replacement operation is invalid.");
                if (item.ClassPath?.EndsWith(".BodySetup", StringComparison.Ordinal) != true) throw new InvalidDataException("Collision replacement does not target BodySetup.");
                var donorElement = operation.After.GetProperty("donor"); var donorIdentity = donorElement.Deserialize<FileIdentity>(ReconJson.Options) ?? throw new InvalidDataException("Collision donor identity is missing."); VerifyFile(donorIdentity.Path, donorIdentity);
                var graft = new CollisionGraftEngine().Graft(packagePath, donorIdentity.Path, temporary, package.InternalPath); if (!graft.Passed) throw new InvalidDataException("Collision graft failed: " + string.Join("; ", graft.Diagnostics));
                changeApprovals.Add((package.PackagePath, item.Id, "replace-collision:BodySetup"));
                var donorInventory = inspector.Inspect(donorIdentity.Path, package.InternalPath);
                foreach (var nav in package.Objects.Where(x => x.ClassPath?.EndsWith(".NavCollision", StringComparison.Ordinal) == true))
                    if (donorInventory.Objects.Single(x => x.ExportIndex == nav.ExportIndex).PayloadHash != nav.PayloadHash) changeApprovals.Add((package.PackagePath, nav.Id, "replace-collision:NavCollision"));
            }
            else if (operation.Operation == "replace-bsp")
            {
                if (operation.SupportRule is not ("cooked-bsp-model-closure-graft-v1" or "fresh-cooked-bsp-model-closure-graft-v1") || operation.PropertyPath != "$bspClosure" || operation.After.ValueKind != JsonValueKind.Object) throw new InvalidDataException("BSP replacement operation is invalid.");
                if (item.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) != true) throw new InvalidDataException("BSP replacement does not target a UModel export.");
                var donorElement = operation.After.GetProperty("donor"); var donorIdentity = donorElement.Deserialize<FileIdentity>(ReconJson.Options) ?? throw new InvalidDataException("BSP donor identity is missing."); VerifyFile(donorIdentity.Path, donorIdentity);
                var freshShell = operation.SupportRule == "fresh-cooked-bsp-model-closure-graft-v1"; var graft = freshShell ? new FreshBspGraftEngine().Graft(packagePath, donorIdentity.Path, temporary, package.InternalPath) : new BspGraftEngine().Graft(packagePath, donorIdentity.Path, temporary, package.InternalPath); if (!graft.Passed) throw new InvalidDataException("BSP graft failed: " + string.Join("; ", graft.Diagnostics));
                foreach (var index in graft.ChangedClosureExportIndices) { var changedObject = package.Objects.Single(x => x.ExportIndex == index); changeApprovals.Add((package.PackagePath, changedObject.Id, freshShell ? "replace-bsp:fresh-model-closure" : "replace-bsp:model-closure")); }
                foreach (var index in graft.AddedClosureExportIndices) addedApprovals.Add((package.PackagePath, index));
            }
            else throw new NotSupportedException($"Unsupported operation: {operation.Operation}");
            File.Delete(packagePath); File.Move(temporary, packagePath);
            if (package.Sidecars.Count == 1)
            {
                var targetSidecar = Path.ChangeExtension(packagePath, ".uexp"); var temporarySidecar = Path.ChangeExtension(temporary, ".uexp");
                if (!File.Exists(temporarySidecar)) throw new InvalidDataException("The split package writer did not produce the expected .uexp sidecar.");
                File.Delete(targetSidecar); File.Move(temporarySidecar, targetSidecar);
            }
        }

        var titleOperations = edits.Operations.Where(x => x.SupportRule == "ut-level-summary-title-v1").ToArray();
        if (titleOperations.Length > 1) throw new InvalidDataException("Only one UUTLevelSummary title edit is supported per build.");
        if (titleOperations.Length == 1)
        {
            var title = titleOperations[0];
            if (title.Before.ValueKind != JsonValueKind.String || title.After.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("UUTLevelSummary titles must be strings.");
            SynchronizeAssetRegistryTitle(staging, inputManifest, title.PackagePath.Split('/')[^1], title.Before.GetString()!, title.After.GetString()!);
        }

        var changedExports = new List<ChangedExport>(); var untouched = 0;
        foreach (var package in ir.Packages)
        {
            var stagedPackage = Path.Combine(staging, package.InternalPath.Replace('/', Path.DirectorySeparatorChar));
            var after = inspector.Inspect(stagedPackage, package.InternalPath); if (after.Diagnostics.Count != 0) throw new InvalidDataException($"Modified package failed inspection: {string.Join("; ", after.Diagnostics)}");
            var afterById = after.Objects.ToDictionary(x => x.Id, StringComparer.Ordinal); var approved = changeApprovals.Where(x => x.PackagePath == package.PackagePath).GroupBy(x => x.ObjectId).ToDictionary(x => x.Key, x => x.Select(y => y.Property).OrderBy(y => y, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
            foreach (var beforeObject in package.Objects)
            {
                if (!afterById.TryGetValue(beforeObject.Id, out var afterObject)) throw new InvalidDataException($"Export disappeared after write: {beforeObject.ObjectPath}");
                if (beforeObject.PayloadHash == afterObject.PayloadHash) { untouched++; continue; }
                if (!approved.TryGetValue(beforeObject.Id, out var properties)) throw new InvalidDataException($"Unapproved export changed: {beforeObject.ObjectPath}");
                changedExports.Add(new ChangedExport(package.PackagePath, beforeObject.Id, beforeObject.ObjectPath, beforeObject.PayloadHash, afterObject.PayloadHash, properties));
            }
            var beforeIds = package.Objects.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var added in after.Objects.Where(x => !beforeIds.Contains(x.Id)))
            {
                if (!addedApprovals.Contains((package.PackagePath, added.ExportIndex))) throw new InvalidDataException($"Unapproved export was added: {added.ObjectPath}");
                changedExports.Add(new ChangedExport(package.PackagePath, added.Id, added.ObjectPath, new string('0', 64), added.PayloadHash, ["add-export"]));
            }
        }
        var approvedObjects = changeApprovals.Select(x => x.ObjectId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var actualObjects = changedExports.Select(x => x.ObjectId).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (approvedObjects.Except(actualObjects, StringComparer.Ordinal).Any()) throw new InvalidDataException("One or more approved edits produced no isolated export change.");
        if (addedApprovals.Count != changedExports.Count(x => x.BeforePayloadHash == new string('0', 64))) throw new InvalidDataException("One or more approved added exports were not produced.");

        var reports = Path.Combine(state, "reports"); Directory.CreateDirectory(reports); var candidateBehavior = behaviorAnalyzer.InspectProject(staging, ir);
        var behaviorAllowlist = allowedBehaviorDependencyAdditions.ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Value, StringComparer.Ordinal);
        var behaviorValidation = new BehaviorValidator().Compare(expectedBehavior, candidateBehavior, behaviorAllowlist);
        var behaviorValidationPath = Path.Combine(reports, "behavior-validation.json"); ReconJson.Write(behaviorValidationPath, behaviorValidation); if (!behaviorValidation.Passed) throw new InvalidDataException("Compiled behavior preservation failed: " + string.Join("; ", behaviorValidation.Differences));
        IReadOnlyDictionary<string, string> pathRedirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var buildDiagnostics = new List<string>();
        var renamedMap = GetOption(args, "--rename-map");
        if (renamedMap is not null)
        {
            if (titleOperations.Length != 1) throw new InvalidDataException("A full map identity rename requires exactly one UUTLevelSummary title edit.");
            pathRedirects = RenameMapIdentity(staging, inputManifest, ir, titleOperations[0].PackagePath, renamedMap);
            buildDiagnostics.Add($"Map package, BuiltData, AssetRegistry, version filename, and map URL renamed to {renamedMap}; cooked compatibility version preserved.");
        }
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(Directory.GetParent(state)!.FullName, "Map-Repaired.pak"));
        if (output.Equals(Path.GetFullPath(inputManifest.Pak.Source.Path), StringComparison.OrdinalIgnoreCase) || output.StartsWith(baselineRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Output pak must not replace the input pak or any immutable baseline file.");
        var outputIdentity = new PakBuilder().Build(inputManifest.Pak, staging, output, Path.Combine(reports, "unrealpak-build.log"), pathRedirects);
        var validation = new ValidationReport(FormatVersions.ValidationReport, ir.Profile, ir.InputPak, Identity.File(editPath), outputIdentity, changedExports, untouched, buildDiagnostics, true);
        var validationPath = Path.Combine(reports, "build-validation.json"); ReconJson.Write(validationPath, validation);
        Console.WriteLine($"Built and integrity-tested: {output}"); Console.WriteLine($"Changed exports: {changedExports.Count}; untouched exports verified: {untouched}"); Console.WriteLine($"Validation: {validationPath}"); return 0;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
    finally
    {
        if (staging is not null && Directory.Exists(staging) && Environment.GetEnvironmentVariable("UT4RECON_KEEP_STAGING") != "1") Directory.Delete(staging, true);
    }
}

static int CertifyRuntimeLoad(string[] args)
{
    if (args.Length < 2) return Fail("certify-runtime-load requires a recovery project.");
    try
    {
        var state = ResolveState(args[1]);
        var ir = ReadJson<ReconstructionIr>(Path.Combine(state, "reconstruction-ir.json"));
        var pak = Path.GetFullPath(RequiredOption(args, "--pak"));
        var log = Path.GetFullPath(RequiredOption(args, "--log"));
        var map = RequiredOption(args, "--map");
        var report = new RuntimeLogCertifier().CertifyPackageLoad(ir.Profile, pak, log, map, GetOption(args, "--game-mode"));
        var output = Path.GetFullPath(GetOption(args, "--output") ?? Path.Combine(state, "reports", "runtime-certification.json"));
        ReconJson.Write(output, report);
        Console.WriteLine($"Runtime package-load certification: {(report.Passed ? "passed" : "failed")}");
        foreach (var check in report.Checks) Console.WriteLine($"  {(check.Passed ? "PASS" : "FAIL")} {check.Name}: {check.Detail}");
        Console.WriteLine("Not certified: live actor/component state or player interaction.");
        Console.WriteLine($"Report: {output}");
        return report.Passed ? 0 : 2;
    }
    catch (Exception error) { return Fail(Environment.GetEnvironmentVariable("UT4RECON_STACKTRACE") == "1" ? error.ToString() : error.Message); }
}

static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException($"Unable to read JSON: {path}");
static string FindBundledFile(string relativePath)
{
    var starts = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var start in starts)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath); if (File.Exists(candidate)) return candidate;
        }
    }
    throw new FileNotFoundException("Bundled support file was not found. Supply it explicitly with --source.", relativePath);
}
static string ResolveState(string project)
{
    var path = Path.GetFullPath(project); var nested = Path.Combine(path, ".ut4recon");
    if (File.Exists(Path.Combine(nested, "project.json"))) return nested;
    if (File.Exists(Path.Combine(path, "project.json"))) return path;
    throw new DirectoryNotFoundException($"Recovery project state was not found under: {path}");
}
static string BaselinePath(string state, string internalPath)
{
    var root = Path.GetFullPath(Path.Combine(state, "baseline")); var path = Path.GetFullPath(Path.Combine(root, internalPath.Replace('/', Path.DirectorySeparatorChar)));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Baseline path escapes project state.");
    return path;
}
static void VerifyFile(string path, FileIdentity expected)
{
    if (!File.Exists(path)) throw new FileNotFoundException($"Required file is missing: {path}", path);
    var actual = Identity.File(path); if (actual.Size != expected.Size || actual.Sha256 != expected.Sha256) throw new InvalidDataException($"File identity mismatch: {path}");
}

static void EnsureDirectWritablePackage(PackageInventory package)
{
    if (package.Sidecars.Count == 0) return;
    if (package.Sidecars.Count == 1 && Path.GetExtension(package.Sidecars[0].Path).Equals(".uexp", StringComparison.OrdinalIgnoreCase)) return;
    throw new NotSupportedException("The direct writer supports monolithic packages and packages with exactly one .uexp sidecar; bulk payload sidecars remain preserve-only.");
}

static void SynchronizeAssetRegistryTitle(string staging, InputManifest input, string mapName, string before, string after)
{
    if (before == after || before.Any(x => x > 0x7f) || after.Any(x => x > 0x7f))
        throw new InvalidDataException("The initial AssetRegistry title synchronizer supports changed ANSI titles only.");
    var suffix = "/" + mapName + "-AssetRegistry.bin";
    var matches = input.Pak.Entries.Where(x => x.Path.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matches.Length != 1) throw new InvalidDataException($"Matching map AssetRegistry resolved to {matches.Length} entries.");
    var path = Path.GetFullPath(Path.Combine(staging, matches[0].Path.Replace('/', Path.DirectorySeparatorChar)));
    if (!path.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("AssetRegistry path escapes staging.");
    var data = File.ReadAllBytes(path); var oldSequence = RegistryString("Title").Concat(RegistryString(before)).ToArray();
    var newSequence = RegistryString("Title").Concat(RegistryString(after)).ToArray(); var positions = FindAll(data, oldSequence);
    if (positions.Count != 1) throw new InvalidDataException($"AssetRegistry Title tag resolved to {positions.Count} matching records.");
    var at = positions[0]; var output = new byte[data.Length - oldSequence.Length + newSequence.Length];
    Buffer.BlockCopy(data, 0, output, 0, at); Buffer.BlockCopy(newSequence, 0, output, at, newSequence.Length);
    Buffer.BlockCopy(data, at + oldSequence.Length, output, at + newSequence.Length, data.Length - at - oldSequence.Length);
    if (FindAll(output, newSequence).Count != 1 || FindAll(output, oldSequence).Count != 0) throw new InvalidDataException("AssetRegistry Title tag failed replacement verification.");
    File.WriteAllBytes(path, output);
}

static IReadOnlyDictionary<string, string> RenameMapIdentity(string staging, InputManifest input, ReconstructionIr ir, string oldPackagePath, string newMapName)
{
    if (newMapName.Length is < 1 or > 128 || newMapName.Any(x => !char.IsLetterOrDigit(x) && x is not ('_' or '-')))
        throw new InvalidDataException("The renamed map must contain only letters, digits, underscores, and hyphens.");
    var oldMapName = oldPackagePath.Split('/')[^1];
    if (oldMapName.Equals(newMapName, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The renamed map identity must differ from the original.");
    var parent = oldPackagePath[..^(oldMapName.Length)]; var newPackagePath = parent + newMapName;
    var oldBuiltPackagePath = oldPackagePath + "_BuiltData"; var newBuiltPackagePath = newPackagePath + "_BuiltData";

    var mapPackage = ir.Packages.Single(x => x.PackagePath == oldPackagePath);
    var builtPackage = ir.Packages.Single(x => x.PackagePath == oldBuiltPackagePath);
    var mapEntry = input.Pak.Entries.Single(x => x.Path.Equals(mapPackage.InternalPath, StringComparison.OrdinalIgnoreCase));
    var builtEntry = input.Pak.Entries.Single(x => x.Path.Equals(builtPackage.InternalPath, StringComparison.OrdinalIgnoreCase));
    var registryEntry = input.Pak.Entries.Single(x => x.Path.Replace('\\', '/').EndsWith("/" + oldMapName + "-AssetRegistry.bin", StringComparison.OrdinalIgnoreCase));
    var versionEntry = input.Pak.Entries.Single(x => x.Path.Replace('\\', '/').EndsWith("/" + oldMapName + "-version.txt", StringComparison.OrdinalIgnoreCase));

    static string RenameLeaf(string path, string before, string after) => path[..^Path.GetFileName(path).Length] + Path.GetFileName(path).Replace(before, after, StringComparison.Ordinal);
    var redirects = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [mapEntry.Path] = RenameLeaf(mapEntry.Path, oldMapName, newMapName),
        [builtEntry.Path] = RenameLeaf(builtEntry.Path, oldMapName, newMapName),
        [registryEntry.Path] = RenameLeaf(registryEntry.Path, oldMapName, newMapName),
        [versionEntry.Path] = RenameLeaf(versionEntry.Path, oldMapName, newMapName)
    };
    if (redirects.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != redirects.Count) throw new InvalidDataException("Map identity rename produced duplicate pak paths.");
    if (input.Pak.Entries.Any(x => !redirects.ContainsKey(x.Path) && redirects.Values.Contains(x.Path, StringComparer.OrdinalIgnoreCase))) throw new InvalidDataException("Map identity rename collides with an existing pak entry.");

    var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [oldPackagePath] = newPackagePath,
        [oldBuiltPackagePath] = newBuiltPackagePath,
        [oldMapName] = newMapName,
        [oldMapName + "_BuiltData"] = newMapName + "_BuiltData"
    };
    var renamer = new PackageIdentityRenamer();
    RenamePackageFile(mapEntry.Path, redirects[mapEntry.Path], mapPackage, replacements, renamer);
    RenamePackageFile(builtEntry.Path, redirects[builtEntry.Path], builtPackage, replacements, renamer);

    var registrySource = StagePath(registryEntry.Path); var registryTarget = StagePath(redirects[registryEntry.Path]);
    var registryReplacements = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [$"{oldPackagePath}.{oldMapName}"] = $"{newPackagePath}.{newMapName}",
        [oldPackagePath] = newPackagePath,
        [oldMapName] = newMapName,
        [$"{oldBuiltPackagePath}.{oldMapName}_BuiltData"] = $"{newBuiltPackagePath}.{newMapName}_BuiltData",
        [oldBuiltPackagePath] = newBuiltPackagePath,
        [oldMapName + "_BuiltData"] = newMapName + "_BuiltData"
    };
    var registryData = File.ReadAllBytes(registrySource); var patchedRegistry = ReplaceRegistryStrings(registryData, registryReplacements);
    Directory.CreateDirectory(Path.GetDirectoryName(registryTarget)!); File.WriteAllBytes(registryTarget, patchedRegistry); File.Delete(registrySource);

    var versionSource = StagePath(versionEntry.Path); var versionTarget = StagePath(redirects[versionEntry.Path]);
    var versionData = File.ReadAllBytes(versionSource);
    Directory.CreateDirectory(Path.GetDirectoryName(versionTarget)!); File.WriteAllBytes(versionTarget, versionData); File.Delete(versionSource);
    return redirects;

    string StagePath(string relative)
    {
        var result = Path.GetFullPath(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(Path.GetFullPath(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Renamed pak path escapes staging.");
        return result;
    }

    void RenamePackageFile(string sourceRelative, string targetRelative, PackageInventory original, IReadOnlyDictionary<string, string> names, PackageIdentityRenamer packageRenamer)
    {
        var source = StagePath(sourceRelative); var target = StagePath(targetRelative); var before = new PackageInspector().Inspect(source, original.InternalPath);
        packageRenamer.Rename(source, target, names); var after = new PackageInspector().Inspect(target, targetRelative);
        if (after.Diagnostics.Count != 0 || after.ExportCount != before.ExportCount) throw new InvalidDataException("Renamed package failed structural inspection.");
        var beforePayloads = before.Objects.OrderBy(x => x.ExportIndex).Select(x => x.PayloadHash).ToArray(); var afterPayloads = after.Objects.OrderBy(x => x.ExportIndex).Select(x => x.PayloadHash).ToArray();
        if (!beforePayloads.SequenceEqual(afterPayloads, StringComparer.Ordinal)) throw new InvalidDataException("Package identity rename changed serialized export payloads.");
        if (after.PackagePath != (original.PackagePath == oldPackagePath ? newPackagePath : newBuiltPackagePath)) throw new InvalidDataException("Renamed package path failed verification.");
        File.Delete(source);
    }
}

static byte[] ReplaceRegistryStrings(byte[] input, IReadOnlyDictionary<string, string> replacements)
{
    if (replacements.Any(x => x.Key.Any(c => c > 0x7f) || x.Value.Any(c => c > 0x7f))) throw new InvalidDataException("AssetRegistry identity rename supports ANSI values only.");
    if (replacements.Keys.Intersect(replacements.Values, StringComparer.Ordinal).Any()) throw new InvalidDataException("AssetRegistry rename chains are unsupported.");
    var existingTargetCounts = replacements.Values.Distinct(StringComparer.Ordinal).ToDictionary(value => value, value => FindAll(input, RegistryString(value)).Count, StringComparer.Ordinal);
    var frames = new List<(int At, int Length, byte[] Replacement)>();
    for (var at = 0; at <= input.Length - 5; at++)
    {
        var length = BitConverter.ToInt32(input, at);
        if (length < 1 || length > 4096 || at + 4 + length > input.Length || input[at + 3 + length] != 0) continue;
        var value = System.Text.Encoding.ASCII.GetString(input, at + 4, length - 1);
        if (replacements.TryGetValue(value, out var replacement)) frames.Add((at, 4 + length, RegistryString(replacement)));
    }
    foreach (var key in replacements.Keys)
        if (frames.Count(x => System.Text.Encoding.ASCII.GetString(input, x.At + 4, x.Length - 5) == key) != 1)
            throw new InvalidDataException($"AssetRegistry identity '{key}' did not resolve exactly once.");
    using var output = new MemoryStream(); var cursor = 0;
    foreach (var frame in frames.OrderBy(x => x.At)) { output.Write(input, cursor, frame.At - cursor); output.Write(frame.Replacement); cursor = frame.At + frame.Length; }
    output.Write(input, cursor, input.Length - cursor); var result = output.ToArray();
    foreach (var value in replacements.Values.Distinct(StringComparer.Ordinal))
    {
        var expectedCount = existingTargetCounts[value] + replacements.Count(x => x.Value == value);
        if (FindAll(result, RegistryString(value)).Count != expectedCount) throw new InvalidDataException($"AssetRegistry renamed identity failed verification: {value}");
    }
    foreach (var value in replacements.Keys)
        if (FindAll(result, RegistryString(value)).Count != 0) throw new InvalidDataException($"AssetRegistry retained old identity: {value}");
    return result;
}

static byte[] RegistryString(string value)
{
    var text = System.Text.Encoding.ASCII.GetBytes(value + "\0"); var result = new byte[4 + text.Length];
    BitConverter.GetBytes(text.Length).CopyTo(result, 0); text.CopyTo(result, 4); return result;
}

static List<int> FindAll(byte[] data, byte[] pattern)
{
    var result = new List<int>();
    for (var i = 0; i <= data.Length - pattern.Length; i++)
        if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern)) result.Add(i);
    return result;
}

static string? GetOption(string[] args, string name) { var index = Array.FindIndex(args, x => x.Equals(name, StringComparison.OrdinalIgnoreCase)); if (index < 0) return null; if (++index == args.Length) throw new ArgumentException($"Missing value for {name}"); return args[index]; }
static bool HasOption(string[] args, string name) => args.Any(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
static string RequiredOption(string[] args, string name) => GetOption(args, name) ?? throw new ArgumentException($"Missing required option: {name}");
static DonorMode ParseDonorMode(string? value, AssetReplacementKind kind) => value?.ToLowerInvariant() switch { null when kind == AssetReplacementKind.PropertySet => DonorMode.ExternalDocument, null or "compatible" or "compatible-shell" => DonorMode.CompatibleShell, "external" or "document" => DonorMode.ExternalDocument, "fresh" or "fresh-shell" => DonorMode.FreshShell, _ => throw new ArgumentException("--mode must be external, compatible, or fresh.") };
static string ResolveEditorRoot(string[] args) => GetOption(args, "--editor") ?? Environment.GetEnvironmentVariable("UT4_EDITOR_ROOT") ?? throw new ArgumentException("Specify --editor or set UT4_EDITOR_ROOT.");
static string ResolveUnrealPak(string[] args)
{
    var direct = GetOption(args, "--unreal-pak"); if (direct is not null) return direct;
    var editor = GetOption(args, "--editor") ?? Environment.GetEnvironmentVariable("UT4_EDITOR_ROOT");
    if (editor is null) throw new ArgumentException("Specify --editor or --unreal-pak (or set UT4_EDITOR_ROOT).");
    return Path.Combine(editor, "Engine", "Binaries", "Win64", "UnrealPak.exe");
}
static int Fail(string message) { Console.Error.WriteLine("error: " + message); return 1; }
static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  ut4recon inspect <input.pak> [--output <directory>] [--editor <root> | --unreal-pak <path>]");
    Console.WriteLine("  ut4recon create-project <input.pak> --output <directory> [--editor <root> | --unreal-pak <path>]");
    Console.WriteLine("  ut4recon inspect-object <project> --object <id>");
    Console.WriteLine("  ut4recon inspect-objects <project> --match <substring>");
    Console.WriteLine("  ut4recon extract-entry <input.pak> <internal-path> --output <file> [--editor <root> | --unreal-pak <path>]");
    Console.WriteLine("  ut4recon new-edit <project> --object <id> --property <path> --after <json> [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon build <project> [--manifest <manifest>] [--output <repaired.pak>] [--rename-map <new-map-name>]");
    Console.WriteLine("  ut4recon certify-runtime-load <project> --pak <tested.pak> --log <server.log> --map </Game/path> [--game-mode <name>] [--output <report>]");
    Console.WriteLine("  ut4recon create-editor-project <project> [--output <directory>] [--editor <root>] [--include-runtime-proxies]");
    Console.WriteLine("  ut4recon run-editor-import <editor-workspace>");
    Console.WriteLine("  ut4recon open-editor-workspace <editor-workspace>");
    Console.WriteLine("  ut4recon run-editor-export <editor-workspace>");
    Console.WriteLine("  ut4recon export-editor-edits <project> <editor-workspace> [--output <manifest>]");
    Console.WriteLine("  ut4recon list-extractable <project> [--match <substring>] [--output <inventory.json>]");
    Console.WriteLine("  ut4recon extract-asset <project> --object <extractable-object-id> --output <bundle>");
    Console.WriteLine("  ut4recon validate-donor <bundle> <donor> [--mode external|compatible|fresh] [--output <report>]");
    Console.WriteLine("  ut4recon inject <project> <bundle> --donor <donor> [--mode external|compatible|fresh] [--output <manifest>] [--append] [--report <report>]");
    Console.WriteLine("  ut4recon delete-actor <project> --object <id> [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon clone-actor <project> --template <actor-id> --name <name> --location <json> [--rotation <json>] [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon certify-collision-box-template <cooked.umap> --output <bundle>");
    Console.WriteLine("  ut4recon add-collision-box <project> --template <collision-box-template.json> --name <name> --location <json> --rotation <json> [--scale <json>] [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon set-map-title <project> --title <distinct-title> [--output <manifest>]");
    Console.WriteLine("  ut4recon create-custom-collision-workspace --output <directory> [--editor <root>] [--source <starter.t3d>]");
    Console.WriteLine("  ut4recon run-custom-collision-import <workspace>");
    Console.WriteLine("  ut4recon open-custom-collision-workspace <workspace>");
    Console.WriteLine("  ut4recon cook-custom-collision-donor <workspace>");
    Console.WriteLine("  ut4recon add-custom-collision <project> --donor <custom-collision-donor.json> --name <name> --location <json> --rotation <json> [--scale <json>] [--append]");
    Console.WriteLine("  ut4recon inspect-mesh <provider-root> <virtual.uasset> [--output <mesh-ir>] [--obj <render.obj>] [--editor-obj <render-and-UCX.obj>] [--collision-obj <collision.obj>]");
    Console.WriteLine("  ut4recon create-mesh-workspace <provider-root> <virtual.uasset> <output-directory> [--destination </Game/path>]");
    Console.WriteLine("  ut4recon compare-mesh <expected.mesh-ir.json> <actual.mesh-ir.json> [--output <report>]");
    Console.WriteLine("  ut4recon graft-collision <baseline.uasset> <cooked-donor.uasset> <output.uasset> [--internal-path <path>] [--report <report>]");
    Console.WriteLine("  ut4recon replace-collision <project> --object <bodysetup-id> --donor <cooked.uasset> [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon inspect-bsp <cooked.umap> [--internal-path <path>] [--output <bsp-ir>]");
    Console.WriteLine("  ut4recon create-bsp-workspace <cooked.umap> <output-directory> [--internal-path <path>] [--destination </Game/path>]");
    Console.WriteLine("  ut4recon compare-bsp <expected.bsp-ir.json> <actual.bsp-ir.json> [--output <report>]");
    Console.WriteLine("  ut4recon graft-bsp <baseline.umap> <cooked-donor.umap> <output.umap> [--internal-path <path>] [--report <report>]");
    Console.WriteLine("  ut4recon graft-bsp-fresh <baseline.umap> <fresh-donor.umap> <output.umap> [--internal-path <path>] [--report <report>]");
    Console.WriteLine("  ut4recon replace-bsp <project> --object <model-id> --donor <cooked.umap> [--fresh-shell] [--output <manifest>] [--append]");
    Console.WriteLine("  ut4recon inspect-behavior <cooked.uasset|umap> [--internal-path <path>] [--output <snapshot>]");
    Console.WriteLine("  ut4recon compare-behavior <expected.behavior.json> <candidate.behavior.json> [--output <report>]");
}
