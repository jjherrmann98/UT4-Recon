using System.Globalization;
using System.Text;
using System.Text.Json;
using Ut4Recon.Core;
using Ut4Recon.Geometry;

namespace Ut4Recon.EditorProtocol;

public sealed class CollisionOverlayWriter
{
    public CollisionOverlayIndex Write(string stateDirectory, ReconstructionIr ir, ProxyScene scene, string t3dPath, string indexPath)
    {
        var mapPackage = ir.Packages.Single(x => x.PackagePath == scene.MapPackagePath);
        var mapPath = Path.Combine(stateDirectory, "baseline", mapPackage.InternalPath.Replace('/', Path.DirectorySeparatorChar));
        var decoded = new NativeBspDecoder().Decode(mapPath, mapPackage.InternalPath);
        var models = decoded.Models.ToDictionary(x => x.ExportIndex); var inventory = mapPackage.Objects.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var entries = new List<CollisionOverlayEntry>(); var diagnostics = new List<string>(decoded.Diagnostics); var lines = new List<string>
        {
            $"Begin Map Name={scene.EditorMapPackagePath}_CollisionOverlays", "   Begin Level NAME=PersistentLevel"
        };
        foreach (var component in scene.Objects.Where(x => x.Collision?.SourceKind == "cooked-u-model").OrderBy(x => x.ExportIndex))
        {
            var geometryId = component.Collision!.GeometryObjectId;
            if (geometryId is null || !inventory.TryGetValue(geometryId, out var geometry) || !models.TryGetValue(geometry.ExportIndex, out var model))
            {
                diagnostics.Add($"{component.ObjectPath}: decoded collision model could not be resolved."); continue;
            }
            var actorName = "UT4ReconCollision_" + component.ReconstructionId[..12]; var modelName = actorName + "_Model";
            WriteActor(lines, component, model, actorName, modelName);
            entries.Add(new CollisionOverlayEntry(component.ReconstructionId, geometryId, component.ObjectPath, actorName, model.Polygons.Count, "exact-runtime-arrays"));
        }
        lines.AddRange(["   End Level", "Begin Surface", "End Surface", "End Map"]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(t3dPath))!); File.WriteAllText(t3dPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, new UTF8Encoding(false));
        var result = new CollisionOverlayIndex(1, scene.MapPackagePath, Path.GetFullPath(t3dPath), entries, diagnostics);
        ReconJson.Write(indexPath, result); return result;
    }

    private static void WriteActor(List<string> lines, ProxyObject component, BspModel model, string actorName, string modelName)
    {
        lines.Add($"      Begin Actor Class=/Script/Engine.BlockingVolume Name={Quote(actorName)}");
        lines.Add("         Begin Object Class=/Script/Engine.BrushComponent Name=BrushComponent0"); lines.Add("         End Object");
        lines.Add("         Begin Object Name=BrushComponent0");
        foreach (var property in component.EditableProperties.Where(x => x.PropertyPath is "RelativeLocation" or "RelativeRotation" or "RelativeScale3D"))
            lines.Add($"            {property.PropertyPath}={Struct(property.Value)}");
        lines.Add($"            ComponentTags(0)={Quote("UT4RECON_COLLISION_OVERLAY:" + component.ReconstructionId)}");
        lines.Add("            BodyInstance=(CollisionEnabled=NoCollision)"); lines.Add("         End Object");
        lines.Add("         bActorEnableCollision=False"); lines.Add($"         Tags(0)={Quote("UT4RECON_COLLISION_OVERLAY:" + component.ReconstructionId)}");
        lines.Add($"         ActorLabel={Quote("[Exact collision overlay] " + component.ObjectPath)}"); lines.Add("         BrushType=Brush_Add");
        lines.Add($"         Begin Brush Name={modelName}"); lines.Add("            Begin PolyList");
        foreach (var polygon in model.Polygons)
        {
            lines.Add($"               Begin Polygon Flags={polygon.Flags} LightMapScale={polygon.LightMapScale.ToString("R", CultureInfo.InvariantCulture)}");
            AddVector(lines, "Origin", polygon.Origin); AddVector(lines, "Normal", polygon.Normal); AddVector(lines, "TextureU", polygon.TextureU); AddVector(lines, "TextureV", polygon.TextureV);
            foreach (var vertex in polygon.Positions) AddVector(lines, "Vertex", vertex); lines.Add("               End Polygon");
        }
        lines.Add("            End PolyList"); lines.Add("         End Brush");
        lines.Add($"         Brush=Model'{modelName}'"); lines.Add("         BrushComponent=BrushComponent'BrushComponent0'"); lines.Add("         RootComponent=BrushComponent'BrushComponent0'"); lines.Add("      End Actor");
    }

    private static string Struct(JsonElement value) => "(" + string.Join(',', value.EnumerateObject().Select(x => x.Name.ToUpperInvariant() + "=" + x.Value.GetDouble().ToString("R", CultureInfo.InvariantCulture))) + ")";
    private static string Quote(string value) => JsonSerializer.Serialize(value);
    private static void AddVector(List<string> lines, string label, MeshVector value) => lines.Add(FormattableString.Invariant($"                  {label} {value.X:+0.000000000;-0.000000000;+0.000000000},{value.Y:+0.000000000;-0.000000000;+0.000000000},{value.Z:+0.000000000;-0.000000000;+0.000000000}"));
}
