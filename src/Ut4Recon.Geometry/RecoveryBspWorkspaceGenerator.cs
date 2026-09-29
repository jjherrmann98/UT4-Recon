using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class RecoveryBspWorkspaceGenerator
{
    public BspWorkspaceIndex Create(string baselineRoot, ReconstructionIr ir, string outputRoot)
    {
        baselineRoot = Path.GetFullPath(baselineRoot); outputRoot = Path.GetFullPath(outputRoot); Directory.CreateDirectory(outputRoot); var entries = new List<BspWorkspaceEntry>();
        foreach (var package in ir.Packages.Where(x => x.ContainsMap && x.Objects.Any(y => y.ClassPath?.EndsWith(".Model", StringComparison.Ordinal) == true)))
        {
            var directory = Path.Combine(outputRoot, SafeName(package.PackagePath));
            try
            {
                var physical = Path.Combine(baselineRoot, package.InternalPath.Replace('/', Path.DirectorySeparatorChar)); var bsp = new NativeBspDecoder().Decode(physical, package.InternalPath); new BspInterchangeWriter().CreateWorkspace(bsp, directory);
                entries.Add(new BspWorkspaceEntry(package.PackagePath, package.InternalPath, directory, bsp.Diagnostics.Count == 0 ? "awaiting-editor-import-edit-and-cooked-donor" : "decoded-with-diagnostics", bsp.Diagnostics));
            }
            catch (Exception error) { entries.Add(new BspWorkspaceEntry(package.PackagePath, package.InternalPath, null, "unsupported", [error.Message])); }
        }
        var index = new BspWorkspaceIndex(GeometryFormatVersions.BspWorkspaceIndex, GeometryFormatVersions.BspProfile, entries); ReconJson.Write(Path.Combine(outputRoot, "bsp-workspace-index.json"), index); return index;
    }
    private static string SafeName(string packagePath) => packagePath.Trim('/').Replace('/', '_').Replace('\\', '_').Replace(':', '_');
}
