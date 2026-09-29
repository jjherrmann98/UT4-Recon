using System.Text.Json;
using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class MeshEditorWorkspaceGenerator
{
    public MeshEditorWorkspace Create(StaticMeshIr mesh, string outputDirectory, string destinationPath = "/Game/UT4Recon/MeshDonors")
    {
        outputDirectory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(outputDirectory); var writer = new MeshInterchangeWriter();
        var meshIr = Path.Combine(outputDirectory, mesh.ObjectName + ".mesh-ir.json"); var editorObj = Path.Combine(outputDirectory, mesh.ObjectName + ".editor.obj");
        var renderObj = Path.Combine(outputDirectory, mesh.ObjectName + ".render.obj"); var collisionObj = Path.Combine(outputDirectory, mesh.ObjectName + ".collision.obj");
        var importSettings = Path.Combine(outputDirectory, "import-settings.json"); ReconJson.Write(meshIr, mesh); writer.WriteEditorObj(mesh, editorObj); writer.WriteObj(mesh, renderObj); writer.WriteCollisionObj(mesh, collisionObj);
        var settings = new
        {
            ImportGroups = new[] { new
            {
                Filenames = new[] { editorObj.Replace('\\', '/') }, DestinationPath = destinationPath, FactoryName = "FbxFactory", bReplaceExisting = true,
                ImportSettings = new { bImportMaterials = false, bImportTextures = false, bImportAnimations = false, bImportAsSkeletal = false, MeshTypeToImport = "FBXIT_StaticMesh",
                    StaticMeshImportData = new { bAutoGenerateCollision = false, bOneConvexHullPerUCX = true, bGenerateLightmapUVs = false, NormalImportMethod = "FBXNIM_ImportNormals", bCombineMeshes = true } }
            } }
        };
        File.WriteAllText(importSettings, JsonSerializer.Serialize(settings, ReconJson.Options) + Environment.NewLine);
        var workspace = new MeshEditorWorkspace(GeometryFormatVersions.MeshEditorWorkspace, GeometryFormatVersions.Profile, meshIr, editorObj, renderObj, collisionObj, importSettings, destinationPath,
            "awaiting-editor-import-and-cooked-donor");
        ReconJson.Write(Path.Combine(outputDirectory, "mesh-editor-workspace.json"), workspace);
        File.WriteAllText(Path.Combine(outputDirectory, "README.txt"),
            "Import the .editor.obj into the recorded DestinationPath. It contains the render mesh and UCX convex collision objects.\r\n" +
            "After editing collision, cook the asset for WindowsNoEditor with the matching UT4 editor. Pass that cooked donor to ut4recon graft-collision.\r\n" +
            "The graft refuses mismatched package tables and verifies that the original StaticMesh render export remains byte-identical.\r\n");
        return workspace;
    }
}
