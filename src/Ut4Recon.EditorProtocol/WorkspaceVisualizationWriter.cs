using System.Globalization;
using System.Text;
using System.Text.Json;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.EditorProtocol;

public sealed class WorkspaceVisualizationWriter
{
    public WorkspaceVisualizationIndex Write(string stateDirectory, ReconstructionIr ir, ProxyScene scene, string t3dPath, string indexPath)
    {
        var package = ir.Packages.Single(x => x.PackagePath == scene.MapPackagePath);
        var physicalPath = Path.Combine(stateDirectory, "baseline", package.InternalPath.Replace('/', Path.DirectorySeparatorChar));
        var decoded = new NativeBspDecoder().Decode(physicalPath, package.InternalPath);
        var principal = decoded.Models.OrderByDescending(x => x.Polygons.Count).FirstOrDefault()
            ?? throw new InvalidDataException("The cooked map has no decoded principal BSP model for workspace context.");
        var lines = new List<string> { $"Begin Map Name={scene.EditorMapPackagePath}_Visualization", "   Begin Level NAME=PersistentLevel" };
        WriteBspContext(lines, principal);
        WriteDirectionalLight(lines, "UT4ReconPreviewKey", "Key", 3.2, -38, -32, 244, 238, 224);
        WriteDirectionalLight(lines, "UT4ReconPreviewFill", "Fill", 1.15, 32, 148, 176, 204, 255);
        lines.AddRange(["   End Level", "Begin Surface", "End Surface", "End Map"]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(t3dPath))!);
        File.WriteAllText(t3dPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        var result = new WorkspaceVisualizationIndex(1, scene.MapPackagePath, Path.GetFullPath(t3dPath), principal.ObjectPath, principal.ExportIndex,
            principal.Polygons.Count, 1, 2,
            ["The BSP context reconstructs final cooked surfaces, not original additive/subtractive brush history.",
             "BSP context and preview lights are editor-only and are excluded from every edit manifest and output pak.",
             "Ordinary edits to the BSP context have no runtime effect; use the isolated BSP donor workflow for model replacement."]);
        ReconJson.Write(indexPath, result);
        return result;
    }

    private static void WriteBspContext(List<string> lines, BspModel model)
    {
        const string actor = "UT4ReconBspContext";
        const string brush = "UT4ReconBspContext_Model";
        lines.Add($"      Begin Actor Class=/Script/Engine.Brush Name={Quote(actor)}");
        lines.Add("         Begin Object Class=/Script/Engine.BrushComponent Name=BrushComponent0");
        lines.Add("         End Object");
        lines.Add("         Begin Object Name=BrushComponent0");
        lines.Add($"            Brush=Model'{brush}'");
        lines.Add("            BodyInstance=(CollisionEnabled=NoCollision)");
        lines.Add("            ComponentTags(0)=\"UT4RECON_VISUALIZATION:BSP_CONTEXT\"");
        lines.Add("         End Object");
        lines.Add("         bActorEnableCollision=False");
        lines.Add("         bLockLocation=True");
        lines.Add("         Tags(0)=\"UT4RECON_VISUALIZATION:BSP_CONTEXT\"");
        lines.Add($"         ActorLabel={Quote("[Reconstructed BSP context] " + model.ObjectPath)}");
        lines.Add("         BrushColor=(B=96,G=148,R=205,A=255)");
        lines.Add("         BrushType=Brush_Add");
        lines.Add($"         Begin Brush Name={brush}");
        lines.Add("            Begin PolyList");
        foreach (var polygon in model.Polygons)
        {
            lines.Add($"               Begin Polygon Flags={polygon.Flags} LightMapScale={polygon.LightMapScale.ToString("R", CultureInfo.InvariantCulture)}");
            AddVector(lines, "Origin", polygon.Origin); AddVector(lines, "Normal", polygon.Normal);
            AddVector(lines, "TextureU", polygon.TextureU); AddVector(lines, "TextureV", polygon.TextureV);
            foreach (var vertex in polygon.Positions) AddVector(lines, "Vertex", vertex);
            lines.Add("               End Polygon");
        }
        lines.Add("            End PolyList");
        lines.Add("         End Brush");
        lines.Add($"         Brush=Model'{brush}'");
        lines.Add("         BrushComponent=BrushComponent'BrushComponent0'");
        lines.Add("         RootComponent=BrushComponent'BrushComponent0'");
        lines.Add("      End Actor");
    }

    private static void WriteDirectionalLight(List<string> lines, string actor, string label, double intensity, double pitch, double yaw, byte red, byte green, byte blue)
    {
        lines.Add($"      Begin Actor Class=/Script/Engine.DirectionalLight Name={Quote(actor)}");
        lines.Add("         Begin Object Class=/Script/Engine.DirectionalLightComponent Name=LightComponent0");
        lines.Add("         End Object");
        lines.Add("         Begin Object Name=LightComponent0");
        lines.Add("            Mobility=Movable");
        lines.Add("            CastShadows=False");
        lines.Add($"            Intensity={intensity.ToString("R", CultureInfo.InvariantCulture)}");
        lines.Add($"            LightColor=(B={blue},G={green},R={red},A=255)");
        lines.Add(FormattableString.Invariant($"            RelativeRotation=(Pitch={pitch:R},Yaw={yaw:R},Roll=0)"));
        lines.Add("         End Object");
        lines.Add("         Tags(0)=\"UT4RECON_VISUALIZATION:LIGHTING\"");
        lines.Add($"         ActorLabel={Quote("[Workspace lighting] " + label)}");
        lines.Add("         LightComponent=Object'" + actor + ".LightComponent0'");
        lines.Add("         RootComponent=Object'" + actor + ".LightComponent0'");
        lines.Add("      End Actor");
    }

    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static void AddVector(List<string> lines, string label, MeshVector value) => lines.Add(FormattableString.Invariant(
        $"                  {label} {value.X:+0.000000000;-0.000000000;+0.000000000},{value.Y:+0.000000000;-0.000000000;+0.000000000},{value.Z:+0.000000000;-0.000000000;+0.000000000}"));
}
