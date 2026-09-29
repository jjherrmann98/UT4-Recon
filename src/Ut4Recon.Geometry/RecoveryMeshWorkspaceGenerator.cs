using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class RecoveryMeshWorkspaceGenerator
{
    public MeshWorkspaceIndex Create(string baselineRoot, ReconstructionIr ir, string outputRoot)
    {
        baselineRoot = Path.GetFullPath(baselineRoot); outputRoot = Path.GetFullPath(outputRoot); Directory.CreateDirectory(outputRoot); var entries = new List<MeshWorkspaceEntry>();
        foreach (var package in ir.Packages.Where(x => x.Objects.Any(o => o.ClassPath?.EndsWith(".StaticMesh", StringComparison.Ordinal) == true)).OrderBy(x => x.InternalPath, StringComparer.Ordinal))
        {
            try
            {
                var mesh = new NativeMeshDecoder().Decode(baselineRoot, package.InternalPath); var name = string.Join('_', package.PackagePath.Trim('/').Split('/').Select(Sanitize)); var directory = Path.Combine(outputRoot, name);
                new MeshEditorWorkspaceGenerator().Create(mesh, directory); entries.Add(new MeshWorkspaceEntry(package.PackagePath, package.InternalPath, directory, "awaiting-editor-import-and-cooked-donor", mesh.Diagnostics));
            }
            catch (Exception error) { entries.Add(new MeshWorkspaceEntry(package.PackagePath, package.InternalPath, null, "unsupported", [error.GetType().Name + ": " + error.Message])); }
        }
        var index = new MeshWorkspaceIndex(GeometryFormatVersions.MeshWorkspaceIndex, GeometryFormatVersions.Profile, entries); ReconJson.Write(Path.Combine(outputRoot, "mesh-workspace-index.json"), index); return index;
    }

    private static string Sanitize(string value) => new(value.Select(x => char.IsLetterOrDigit(x) || x is '_' or '-' ? x : '_').ToArray());
}
