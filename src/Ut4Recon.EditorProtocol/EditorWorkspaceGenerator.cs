using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Ut4Recon.Core;

namespace Ut4Recon.EditorProtocol;

public sealed class EditorWorkspaceGenerator
{
    public EditorWorkspace Create(string stateDirectory, ReconstructionIr ir, string editorRoot, string outputDirectory, bool visualOnly = true,
        string? backendExecutable = null, IReadOnlyList<string>? backendArguments = null)
    {
        var editorProject = Path.GetFullPath(Path.Combine(editorRoot, "UnrealTournament", "UnrealTournament.uproject"));
        var editorExe = Path.GetFullPath(Path.Combine(editorRoot, "Engine", "Binaries", "Win64", "UE4Editor.exe"));
        if (!File.Exists(editorProject) || !File.Exists(editorExe)) throw new DirectoryNotFoundException("The UT4 editor project or executable is missing.");
        var input = ReadJson<InputManifest>(Path.Combine(stateDirectory, "input-manifest.json"));
        if (input.Pak.Editor is null || Identity.Sha256File(editorExe) != input.Pak.Editor.Sha256) throw new InvalidDataException("Editor binary does not match the editor recorded for this recovery project.");

        var workspaceRoot = Path.GetFullPath(outputDirectory); var projectRoot = Path.Combine(workspaceRoot, "project");
        if (Directory.Exists(workspaceRoot) && Directory.EnumerateFileSystemEntries(workspaceRoot).Any()) throw new IOException("Editor workspace output directory is not empty.");
        Directory.CreateDirectory(projectRoot); var projectFile = Path.Combine(projectRoot, "UnrealTournament.uproject"); File.Copy(editorProject, projectFile);
        CopyDirectory(Path.Combine(editorRoot, "UnrealTournament", "Config"), Path.Combine(projectRoot, "Config"));
        CreateJunction(Path.Combine(projectRoot, "Binaries"), Path.Combine(editorRoot, "UnrealTournament", "Binaries"));
        CreateJunction(Path.Combine(projectRoot, "Plugins"), Path.Combine(editorRoot, "UnrealTournament", "Plugins"));
        CreateJunction(Path.Combine(projectRoot, "Releases"), Path.Combine(editorRoot, "UnrealTournament", "Releases"));
        var content = Path.Combine(projectRoot, "Content"); Directory.CreateDirectory(content);
        foreach (var name in new[] { "RestrictedAssets", "StarterContent" }) CreateJunction(Path.Combine(content, name), Path.Combine(editorRoot, "UnrealTournament", "Content", name));

        var map = ir.Packages.Single(x => x.ContainsMap && x.InternalPath.EndsWith(".umap", StringComparison.OrdinalIgnoreCase)); var sourceName = map.PackagePath.Split('/')[^1]; var recoveredName = "Recovered_" + Sanitize(sourceName);
        var editorMapPackage = "/Game/UT4Recon/" + recoveredName; var mapDirectory = Path.Combine(content, "UT4Recon"); Directory.CreateDirectory(mapDirectory);
        ConfigureProject(projectFile, Path.Combine(projectRoot, "Config", "DefaultEngine.ini"), editorMapPackage,
            File.Exists(Path.Combine(editorRoot, "Engine", "Plugins", "Marketplace", "Ut4ReconEditor", "Ut4ReconEditor.uplugin")));
        var mapFile = Path.Combine(mapDirectory, recoveredName + ".umap"); File.Copy(Path.Combine(editorRoot, "Engine", "Content", "Maps", "Entry.umap"), mapFile);
        var scene = new ProxySceneBuilder().Build(stateDirectory, ir, editorMapPackage, visualOnly); var scenePath = Path.Combine(workspaceRoot, "proxy-scene.json");
        var collisionT3d = Path.Combine(workspaceRoot, "collision-overlays.t3d"); var collisionIndexPath = Path.Combine(workspaceRoot, "collision-overlays.json");
        var collisionIndex = new CollisionOverlayWriter().Write(stateDirectory, ir, scene, collisionT3d, collisionIndexPath);
        var visualizationT3d = Path.Combine(workspaceRoot, "workspace-visualization.t3d"); var visualizationIndexPath = Path.Combine(workspaceRoot, "workspace-visualization.json");
        var visualization = new WorkspaceVisualizationWriter().Write(stateDirectory, ir, scene, visualizationT3d, visualizationIndexPath);
        var overlayIds = collisionIndex.Entries.Select(x => x.SourceComponentId).ToHashSet(StringComparer.Ordinal);
        scene = scene with { Objects = scene.Objects.Select(item => overlayIds.Contains(item.ReconstructionId) && item.Collision is not null
            ? item with { Collision = item.Collision with { Visualization = "exact-overlay-t3d", Limitation = "The overlay is exact cooked collision geometry. Canonical location/rotation changes and compatible duplicates are exportable; arbitrary brush-vertex edits are not." },
                Capabilities = item.Capabilities.Concat([
                    new ProxyCapability("edit-collision-overlay-transform", null, CapabilityState.Available, "editor-protocol-v2", "native-component-transform-v1", "Moving or rotating the canonical overlay updates the original BrushComponent transform."),
                    new ProxyCapability("duplicate-collision-overlay", null, CapabilityState.Available, "editor-protocol-v2", "blocking-volume-closure-clone-v1", "A compatible duplicated overlay becomes a complete BlockingVolume closure."),
                    new ProxyCapability("delete-collision-overlay", null, CapabilityState.Available, "editor-protocol-v2", "persistent-level-actor-delete-v1", "Deleting the canonical overlay removes the original cooked BlockingVolume from the persistent level."),
                    new ProxyCapability("edit-collision-brush-vertices", null, CapabilityState.Blocked, "editor-protocol-v2", null, "Arbitrary brush-vertex editing has no validated cooked writer.")]).ToArray(),
                Limitations = item.Limitations.Where(x => !x.Contains("does not yet import its selectable overlay", StringComparison.Ordinal)).Append("Arbitrary collision brush-vertex edits cannot be exported.").ToArray() }
            : item).ToArray() };
        ReconJson.Write(scenePath, scene);
        var t3dPath = Path.Combine(workspaceRoot, "proxy-import.t3d"); new T3dProxyWriter().Write(stateDirectory, ir, scene, t3dPath);
        MergeActors(t3dPath, collisionT3d); MergeActors(t3dPath, visualizationT3d);
        var exportPath = Path.Combine(workspaceRoot, "editor-export.t3d");
        var importCommands = Path.Combine(workspaceRoot, "import-commands.txt"); var exportCommands = Path.Combine(workspaceRoot, "export-commands.txt");
        WriteCommands(importCommands,
            "ACTOR SELECT ALL", "DELETE", $"MAP IMPORTADD FILE={Slash(t3dPath)}", "MAP REBUILD",
            $"OBJ SAVEPACKAGE PACKAGE={editorMapPackage} FILE={Slash(mapFile)} SILENT=true",
            $"OBJ EXPORT TYPE=World NAME={recoveredName} FILE={Slash(exportPath)}", "QUIT");
        WriteCommands(exportCommands, "MAP CHECK", $"OBJ SAVEPACKAGE PACKAGE={editorMapPackage} FILE={Slash(mapFile)} SILENT=true",
            $"OBJ EXPORT TYPE=World NAME={recoveredName} FILE={Slash(exportPath)}", "QUIT");
        var recoveryProject = Directory.GetParent(Path.GetFullPath(stateDirectory))?.FullName ?? throw new DirectoryNotFoundException("Recovery project root is unavailable.");
        backendExecutable = Path.GetFullPath(backendExecutable ?? Environment.ProcessPath ?? throw new InvalidOperationException("Backend executable is unavailable."));
        backendArguments ??= [];
        var repairedName = Sanitize(sourceName) + "-Repaired";
        var defaultOutputPak = Path.Combine(recoveryProject, "Build", repairedName + "-WindowsNoEditor.pak");
        var workspace = new EditorWorkspace(FormatVersions.EditorWorkspace, ir.Profile, scene.ReconstructionIr, Identity.File(editorExe), projectFile, editorMapPackage, scenePath, t3dPath,
            collisionT3d, collisionIndexPath, importCommands, exportCommands, exportPath, "generated", recoveryProject, backendExecutable,
            backendArguments.ToArray(), defaultOutputPak, repairedName, visualizationT3d, visualizationIndexPath, null,
            visualOnly ? "safe-visual" : "experimental-runtime-proxies");
        ReconJson.Write(Path.Combine(workspaceRoot, "editor-workspace.json"), workspace);
        new EditorWorkspaceReportWriter().Write(scene, collisionIndex, Path.Combine(workspaceRoot, "workspace-support-report.json"), Path.Combine(workspaceRoot, "README.md"), visualization);
        return workspace;
    }

    public int RunImport(EditorWorkspace workspace, string logPath)
    {
        if (File.Exists(workspace.ExportT3d)) File.Delete(workspace.ExportT3d);
        return RunCommands(workspace, workspace.ImportCommands, logPath);
    }

    public int RunExport(EditorWorkspace workspace, string logPath)
    {
        if (File.Exists(workspace.ExportT3d)) File.Delete(workspace.ExportT3d);
        return RunCommands(workspace, workspace.ExportCommands, logPath);
    }

    public int Open(EditorWorkspace workspace)
    {
        var mapFile = Path.Combine(Path.GetDirectoryName(workspace.ProjectFile)!, "Content", workspace.MapPackagePath[6..].Replace('/', Path.DirectorySeparatorChar) + ".umap");
        if (!File.Exists(mapFile)) throw new FileNotFoundException("The editor map has not been materialized. Run run-editor-import first.", mapFile);
        var start = new ProcessStartInfo(workspace.Editor.Path) { UseShellExecute = true };
        start.ArgumentList.Add(workspace.ProjectFile); start.ArgumentList.Add(workspace.MapPackagePath); start.ArgumentList.Add("-ddc=noshared"); start.ArgumentList.Add("-UT4ReconOpen");
        return (Process.Start(start) ?? throw new InvalidOperationException("Could not start the UT4 editor.")).Id;
    }

    private static int RunCommands(EditorWorkspace workspace, string commands, string logPath)
    {
        var start = new ProcessStartInfo(workspace.Editor.Path) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(workspace.ProjectFile); start.ArgumentList.Add(workspace.MapPackagePath); start.ArgumentList.Add("-unattended"); start.ArgumentList.Add("-nosplash");
        start.ArgumentList.Add("-NullRHI"); start.ArgumentList.Add("-ddc=noshared"); start.ArgumentList.Add("-FORCELOGFLUSH"); start.ArgumentList.Add("-abslog=" + Path.GetFullPath(logPath));
        start.ArgumentList.Add("-ExecCmds=EXECFILE " + Slash(commands));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the UT4 editor bridge process.");
        var deadline = DateTime.UtcNow.AddMinutes(4); DateTime? exportSeen = null;
        while (!process.WaitForExit(1000))
        {
            if (File.Exists(workspace.ExportT3d))
            {
                exportSeen ??= DateTime.UtcNow;
                if (DateTime.UtcNow - exportSeen > TimeSpan.FromSeconds(5)) { process.Kill(true); process.WaitForExit(); return 0; }
            }
            if (DateTime.UtcNow >= deadline) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("UT4 editor bridge timed out before producing its T3D export."); }
        }
        return process.ExitCode;
    }

    internal static void CreateJunction(string link, string target)
    {
        if (!Directory.Exists(target)) throw new DirectoryNotFoundException(target);
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEnd(); var stderr = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException($"Could not create project junction {link}: {stdout} {stderr}".Trim());
    }

    internal static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var directory in Directory.EnumerateDirectories(source)) CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    internal static void WriteCommands(string path, params string[] commands) => File.WriteAllText(path, string.Join(Environment.NewLine, commands) + Environment.NewLine, Encoding.ASCII);
    internal static void ConfigureProject(string projectFile, string engineConfig, string mapPackagePath, bool enableAdapter)
    {
        if (enableAdapter)
        {
            var root = JsonNode.Parse(File.ReadAllText(projectFile))?.AsObject() ?? throw new InvalidDataException(projectFile);
            var plugins = root["Plugins"]?.AsArray() ?? throw new InvalidDataException($"Project has no Plugins array: {projectFile}");
            if (!plugins.Any(x => string.Equals(x?["Name"]?.GetValue<string>(), "Ut4ReconEditor", StringComparison.Ordinal)))
                plugins.Add(new JsonObject { ["Name"] = "Ut4ReconEditor", ["Enabled"] = true });
            File.WriteAllText(projectFile, root.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine, new UTF8Encoding(false));
        }

        var lines = File.ReadAllLines(engineConfig).ToList();
        var index = lines.FindIndex(x => x.TrimStart().StartsWith("EditorStartupMap=", StringComparison.OrdinalIgnoreCase));
        var setting = "EditorStartupMap=" + mapPackagePath;
        if (index >= 0) lines[index] = setting;
        else { lines.Add(""); lines.Add("[/Script/UnrealEd.EditorLoadingSavingSettings]"); lines.Add(setting); }
        File.WriteAllLines(engineConfig, lines, new UTF8Encoding(false));
    }
    private static void MergeActors(string proxyT3d, string overlayT3d)
    {
        var proxy = File.ReadAllLines(proxyT3d).ToList(); var overlay = File.ReadAllLines(overlayT3d);
        var insertAt = proxy.FindIndex(x => x.Trim().Equals("End Level", StringComparison.Ordinal));
        var overlayStart = Array.FindIndex(overlay, x => x.Trim().StartsWith("Begin Level", StringComparison.Ordinal)) + 1;
        var overlayEnd = Array.FindIndex(overlay, overlayStart, x => x.Trim().Equals("End Level", StringComparison.Ordinal));
        if (insertAt < 0 || overlayStart == 0 || overlayEnd < overlayStart) throw new InvalidDataException("Merged T3D has an invalid map envelope.");
        proxy.InsertRange(insertAt, overlay[overlayStart..overlayEnd]); File.WriteAllLines(proxyT3d, proxy, new UTF8Encoding(false));
    }
    internal static string Slash(string path) => Path.GetFullPath(path).Replace('\\', '/');
    internal static string Sanitize(string value) => new(value.Select(x => char.IsLetterOrDigit(x) || x == '_' ? x : '_').ToArray());
    private static T ReadJson<T>(string path) => System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException(path);
}
