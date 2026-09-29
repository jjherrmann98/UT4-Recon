using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.EditorProtocol;

public sealed class CustomCollisionWorkspaceService
{
    public const string Profile = "ut4-4.15-windows-no-editor-v1";
    public const string MapPackage = "/Game/UT4ReconDonor/CustomCollisionDonor";

    public CustomCollisionWorkspace Create(string editorRoot, string outputDirectory, string sourceT3d)
    {
        editorRoot = Path.GetFullPath(editorRoot); outputDirectory = Path.GetFullPath(outputDirectory); sourceT3d = Path.GetFullPath(sourceT3d);
        if (!File.Exists(sourceT3d)) throw new FileNotFoundException("Initial collision brush T3D is missing.", sourceT3d);
        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any()) throw new IOException("Custom collision workspace output directory is not empty.");
        ValidateEditor(editorRoot);
        var installedProject = Path.Combine(editorRoot, "UnrealTournament", "UnrealTournament.uproject");
        var editor = Path.Combine(editorRoot, "Engine", "Binaries", "Win64", "UE4Editor.exe");
        var projectRoot = Path.Combine(outputDirectory, "project"); Directory.CreateDirectory(projectRoot);
        var projectFile = Path.Combine(projectRoot, "UnrealTournament.uproject"); File.Copy(installedProject, projectFile);
        EditorWorkspaceGenerator.CopyDirectory(Path.Combine(editorRoot, "UnrealTournament", "Config"), Path.Combine(projectRoot, "Config"));
        EditorWorkspaceGenerator.CreateJunction(Path.Combine(projectRoot, "Binaries"), Path.Combine(editorRoot, "UnrealTournament", "Binaries"));
        EditorWorkspaceGenerator.CreateJunction(Path.Combine(projectRoot, "Plugins"), Path.Combine(editorRoot, "UnrealTournament", "Plugins"));
        EditorWorkspaceGenerator.CreateJunction(Path.Combine(projectRoot, "Releases"), Path.Combine(editorRoot, "UnrealTournament", "Releases"));
        var content = Path.Combine(projectRoot, "Content"); Directory.CreateDirectory(content);
        foreach (var name in new[] { "RestrictedAssets", "StarterContent" }) EditorWorkspaceGenerator.CreateJunction(Path.Combine(content, name), Path.Combine(editorRoot, "UnrealTournament", "Content", name));
        var mapDirectory = Path.Combine(content, "UT4ReconDonor"); Directory.CreateDirectory(mapDirectory);
        var mapFile = Path.Combine(mapDirectory, "CustomCollisionDonor.umap"); File.Copy(Path.Combine(editorRoot, "Engine", "Content", "Maps", "Entry.umap"), mapFile);
        EditorWorkspaceGenerator.ConfigureProject(projectFile, Path.Combine(projectRoot, "Config", "DefaultEngine.ini"), MapPackage, false);
        var copiedT3d = Path.Combine(outputDirectory, "CustomCollisionSource.t3d");
        var sourceText = File.ReadAllText(sourceT3d).Replace("/Game/Reconstruction/CollisionBoxTemplate", MapPackage, StringComparison.Ordinal);
        File.WriteAllText(copiedT3d, sourceText, Encoding.ASCII);
        var commands = Path.Combine(outputDirectory, "import-commands.txt");
        EditorWorkspaceGenerator.WriteCommands(commands, "ACTOR SELECT ALL", "DELETE", $"MAP IMPORTADD FILE=\"{EditorWorkspaceGenerator.Slash(copiedT3d)}\"", "MAP REBUILD",
            $"OBJ SAVEPACKAGE PACKAGE=/Game/UT4ReconDonor/CustomCollisionDonor FILE=\"{EditorWorkspaceGenerator.Slash(mapFile)}\" SILENT=true", "QUIT");
        var cooked = Path.Combine(projectRoot, "Saved", "Cooked", "WindowsNoEditor", "UnrealTournament", "Content", "UT4ReconDonor", "CustomCollisionDonor.umap");
        var workspace = new CustomCollisionWorkspace(FormatVersions.CustomCollisionWorkspace, Profile, Identity.File(editor), projectFile, MapPackage, copiedT3d, commands, cooked, "generated",
            ["Author exactly one BlockingVolume and keep it in the persistent level.", "Use brush geometry only; gameplay actors and external custom assets are rejected.", "The cooked donor supplies geometry and collision, not behavior."]);
        ReconJson.Write(Path.Combine(outputDirectory, "custom-collision-workspace.json"), workspace);
        File.WriteAllText(Path.Combine(outputDirectory, "README.md"), Guide(), new UTF8Encoding(false)); return workspace;
    }

    public int Import(string directory)
    {
        var path = Path.Combine(Path.GetFullPath(directory), "custom-collision-workspace.json"); var workspace = Read(path); VerifyEditor(workspace);
        var mapFile = Path.Combine(Path.GetDirectoryName(workspace.ProjectFile)!, "Content", "UT4ReconDonor", "CustomCollisionDonor.umap");
        var result = RunEditor(workspace, [workspace.ProjectFile, workspace.MapPackagePath, "-unattended", "-nosplash", "-NullRHI", "-ddc=noshared", "-FORCELOGFLUSH",
            "-ExecCmds=EXECFILE " + EditorWorkspaceGenerator.Slash(workspace.ImportCommands)], Path.Combine(Path.GetFullPath(directory), "import.log"), TimeSpan.FromMinutes(4), mapFile);
        if (result != 0) throw new InvalidOperationException($"Custom collision import failed with exit code {result}.");
        ReconJson.Write(path, workspace with { Status = "ready-for-authoring" }); return result;
    }

    public int Open(string directory)
    {
        var workspace = Read(Path.Combine(Path.GetFullPath(directory), "custom-collision-workspace.json")); VerifyEditor(workspace);
        var process = new ProcessStartInfo(workspace.Editor.Path) { UseShellExecute = true };
        process.ArgumentList.Add(workspace.ProjectFile); process.ArgumentList.Add(workspace.MapPackagePath); process.ArgumentList.Add("-ddc=noshared");
        return (Process.Start(process) ?? throw new InvalidOperationException("Could not open custom collision workspace.")).Id;
    }

    public CustomCollisionDonorManifest CookAndCertify(string directory)
    {
        directory = Path.GetFullPath(directory); var workspacePath = Path.Combine(directory, "custom-collision-workspace.json"); var workspace = Read(workspacePath); VerifyEditor(workspace);
        var editorCmd = Path.Combine(Path.GetDirectoryName(workspace.Editor.Path)!, "UE4Editor-Cmd.exe"); if (!File.Exists(editorCmd)) editorCmd = workspace.Editor.Path;
        if (File.Exists(workspace.CookedPackage)) File.Delete(workspace.CookedPackage);
        var log = Path.Combine(directory, "cook.log");
        var arguments = new[] { workspace.ProjectFile, "-run=Cook", "-TargetPlatform=WindowsNoEditor", "-MAP=" + workspace.MapPackagePath, "-cooksinglepackage", "-unattended", "-nosplash", "-NullRHI", "-ddc=noshared", "-FORCELOGFLUSH", "-stdout" };
        var exit = Run(editorCmd, arguments, Path.GetDirectoryName(workspace.ProjectFile)!, log, TimeSpan.FromMinutes(6), workspace.CookedPackage);
        if (exit != 0 || !File.Exists(workspace.CookedPackage)) throw new InvalidOperationException($"Custom collision cook failed with exit code {exit}. See {log}");
        var bundledPackage = Path.Combine(directory, "CustomCollisionDonor.umap"); File.Copy(workspace.CookedPackage, bundledPackage, true); File.SetAttributes(bundledPackage, FileAttributes.Normal);
        var manifest = new CollisionBoxTemplateService().CertifyCustom(bundledPackage, "UnrealTournament/Content/UT4ReconDonor/CustomCollisionDonor.umap", Profile);
        ReconJson.Write(Path.Combine(directory, "custom-collision-donor.json"), manifest); ReconJson.Write(workspacePath, workspace with { Status = "cooked-and-certified" }); return manifest;
    }

    private static int RunEditor(CustomCollisionWorkspace workspace, IReadOnlyList<string> arguments, string log, TimeSpan timeout, string? completionFile = null)
        => Run(workspace.Editor.Path, arguments.Append("-abslog=" + Path.GetFullPath(log)).ToArray(), Path.GetDirectoryName(workspace.ProjectFile)!, log, timeout, completionFile);
    private static int Run(string executable, IReadOnlyList<string> arguments, string workingDirectory, string log, TimeSpan timeout, string? completionFile = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the UT4 editor process.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); var deadline = DateTime.UtcNow + timeout;
        var originalWrite = completionFile is not null && File.Exists(completionFile) ? File.GetLastWriteTimeUtc(completionFile) : DateTime.MinValue; DateTime? completed = null; var accepted = false;
        while (!process.WaitForExit(1000))
        {
            if (completionFile is not null && File.Exists(completionFile) && File.GetLastWriteTimeUtc(completionFile) > originalWrite)
            {
                completed ??= DateTime.UtcNow;
                if (DateTime.UtcNow - completed > TimeSpan.FromSeconds(5)) { process.Kill(true); process.WaitForExit(); accepted = true; break; }
            }
            if (DateTime.UtcNow >= deadline) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("UT4 custom collision process timed out."); }
        }
        File.WriteAllText(log + ".stdout.txt", stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult(), new UTF8Encoding(false)); return accepted ? 0 : process.ExitCode;
    }
    private static void ValidateEditor(string root)
    {
        var modulesPath = Path.Combine(root, "Engine", "Binaries", "Win64", "UE4Editor.modules");
        using var document = JsonDocument.Parse(File.ReadAllText(modulesPath)); var rootElement = document.RootElement;
        if (rootElement.GetProperty("Changelist").GetInt32() != 3525360 || rootElement.GetProperty("CompatibleChangelist").GetInt32() != 3525109)
            throw new InvalidDataException("Custom collision donors require UT4 editor CL 3525360 / API 3525109.");
    }
    private static void VerifyEditor(CustomCollisionWorkspace workspace)
    {
        if (workspace.SchemaVersion != FormatVersions.CustomCollisionWorkspace || workspace.Profile != Profile) throw new InvalidDataException("Custom collision workspace profile is unsupported.");
        var actual = Identity.File(workspace.Editor.Path); if (actual.Size != workspace.Editor.Size || actual.Sha256 != workspace.Editor.Sha256) throw new InvalidDataException("Custom collision workspace editor identity changed.");
    }
    private static CustomCollisionWorkspace Read(string path) => JsonSerializer.Deserialize<CustomCollisionWorkspace>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException(path);
    private static string Guide() => """
# UT4 Recon custom collision donor

This isolated project contains one starter BlockingVolume. Run the import command once, open the workspace, and edit that brush into the desired local collision shape. Keep exactly one BlockingVolume and do not add gameplay actors or custom asset dependencies. Save and close the editor, then cook and certify the donor.

The resulting `custom-collision-donor.json` owns only the new BlockingVolume, BrushComponent, UModel, and BodySetup closure. It can be placed, rotated, and scaled when added to a recovery project. The original brush construction history is not relevant; the certified cooked geometry and PhysX collision are authoritative.
""" + Environment.NewLine;
}
