using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class MeshPreviewImportWriter
{
    public MeshPreviewImportManifest Write(MeshWorkspaceIndex index, string outputDirectory, string manifestPath)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var entries = new List<MeshPreviewImportEntry>();
        foreach (var item in index.Entries.Where(x => x.Status != "unsupported" && x.WorkspacePath is not null))
        {
            var workspacePath = Path.Combine(item.WorkspacePath!, "mesh-editor-workspace.json");
            var workspace = ReadJson<MeshEditorWorkspace>(workspacePath);
            var assetName = item.PackagePath.Split('/')[^1];
            var destinationPath = item.PackagePath[..^(assetName.Length + 1)];
            var stagingDirectory = Path.Combine(outputDirectory, Sanitize(item.PackagePath));
            Directory.CreateDirectory(stagingDirectory);
            var stagedObj = Path.Combine(stagingDirectory, assetName + ".obj");
            var mesh = ReadJson<StaticMeshIr>(workspace.MeshIr);
            new MeshInterchangeWriter().WriteUnrealPreviewObj(mesh, stagedObj);
            entries.Add(new MeshPreviewImportEntry(item.PackagePath, assetName, destinationPath, stagedObj, "recovered-render-geometry",
                ["Preview mesh coordinates compensate for the pinned UT4 OBJ importer's Y-axis reflection.", "Preview mesh materials are not recovered by OBJ interchange.", "The imported source asset is editor context and is excluded from cooked-baseline output."]));
        }
        var manifest = new MeshPreviewImportManifest(1, index.Profile, entries,
            ["Existing source packages are never overwritten.", "Preview assets restore recovered LOD0 geometry under the original package path; material graphs, authored LODs, sockets, and original import metadata are not reconstructed."]);
        ReconJson.Write(manifestPath, manifest);
        return manifest;
    }

    private static T ReadJson<T>(string path) => System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(path), ReconJson.Options) ?? throw new InvalidDataException(path);
    private static string Sanitize(string value) => new(value.Trim('/').Select(x => char.IsLetterOrDigit(x) || x is '_' or '-' ? x : '_').ToArray());
}
