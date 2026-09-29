using System.Globalization;
using Ut4Recon.Core;

namespace Ut4Recon.Geometry;

public sealed class BspInterchangeWriter
{
    public BspEditorWorkspace CreateWorkspace(BspMapIr map, string outputDirectory, string destinationPath = "/Game/UT4Recon/BspDonors")
    {
        outputDirectory = Path.GetFullPath(outputDirectory); Directory.CreateDirectory(outputDirectory); var principal = Principal(map);
        var ir = Path.Combine(outputDirectory, "bsp-ir.json"); var t3d = Path.Combine(outputDirectory, "RecoveredBSP.t3d"); var obj = Path.Combine(outputDirectory, "RecoveredBSP.obj");
        ReconJson.Write(ir, map); WriteT3d(principal, t3d, destinationPath); WriteObj(principal, obj);
        var workspace = new BspEditorWorkspace(GeometryFormatVersions.BspEditorWorkspace, GeometryFormatVersions.BspProfile, ir, t3d, obj, destinationPath,
            "awaiting-editor-import-edit-and-cooked-donor", ["original additive/subtractive brush history", "brush names and grouping", "construction intent"]);
        ReconJson.Write(Path.Combine(outputDirectory, "bsp-editor-workspace.json"), workspace);
        File.WriteAllText(Path.Combine(outputDirectory, "README.txt"), "Import RecoveredBSP.t3d into a fresh matching UT4 editor map. The brush represents surviving compiled surfaces, not the original CSG brush stack. Edit and rebuild geometry, then cook a donor for validated model-closure grafting.\r\n"); return workspace;
    }

    public void WriteT3d(BspModel model, string path, string mapPath = "/Game/UT4Recon/BspDonors")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); var c = CultureInfo.InvariantCulture; var lines = new List<string> {
            $"Begin Map Name={mapPath}/RecoveredBSP", "Begin Level NAME=PersistentLevel", "Begin Actor Class=/Script/Engine.Brush Name=\"RecoveredBSP\"",
            "Begin Object Class=/Script/Engine.BrushComponent Name=\"BrushComponent0\"", "End Object", "Begin Object Name=\"BrushComponent0\"", "Brush=Model'RecoveredModel'", "End Object",
            "BrushType=Brush_Add", "Begin Brush Name=RecoveredModel", "Begin PolyList" };
        foreach (var polygon in model.Polygons)
        {
            var texture = string.IsNullOrEmpty(polygon.Material) ? "" : " Texture=" + polygon.Material; lines.Add($"Begin Polygon{texture} Flags={polygon.Flags} LightMapScale={polygon.LightMapScale.ToString("R", c)}");
            AddVector(lines, "Origin", polygon.Origin); AddVector(lines, "Normal", polygon.Normal); AddVector(lines, "TextureU", polygon.TextureU); AddVector(lines, "TextureV", polygon.TextureV);
            foreach (var vertex in polygon.Positions) AddVector(lines, "Vertex", vertex); lines.Add("End Polygon");
        }
        lines.AddRange(["End PolyList", "End Brush", "Brush=Model'RecoveredModel'", "BrushComponent=BrushComponent'BrushComponent0'", "RootComponent=BrushComponent'BrushComponent0'", "End Actor", "End Level", "Begin Surface", "End Surface", "End Map"]);
        File.WriteAllLines(path, lines);
    }

    public void WriteObj(BspModel model, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); using var writer = new StreamWriter(path); writer.WriteLine("o RecoveredBSP"); var offset = 1;
        foreach (var polygon in model.Polygons) { foreach (var p in polygon.Positions) writer.WriteLine(FormattableString.Invariant($"v {p.X:R} {p.Y:R} {p.Z:R}")); writer.WriteLine("f " + string.Join(' ', Enumerable.Range(offset, polygon.Positions.Count))); offset += polygon.Positions.Count; }
    }

    private static BspModel Principal(BspMapIr map) => map.Models.OrderByDescending(x => x.Polygons.Count).FirstOrDefault() ?? throw new InvalidDataException("BSP IR has no decoded models.");
    private static void AddVector(List<string> lines, string label, MeshVector v) => lines.Add(FormattableString.Invariant($"{label} {v.X:+0.000000000;-0.000000000;+0.000000000},{v.Y:+0.000000000;-0.000000000;+0.000000000},{v.Z:+0.000000000;-0.000000000;+0.000000000}"));
}
