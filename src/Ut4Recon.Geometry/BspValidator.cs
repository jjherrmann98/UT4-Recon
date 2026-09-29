namespace Ut4Recon.Geometry;

public sealed class BspValidator
{
    public BspComparison Compare(BspMapIr expected, BspMapIr actual, double areaTolerance = 0.0001, double boundsTolerance = 1.0, bool requireOfflineQueries = false)
    {
        var diagnostics = new List<string>(); var a = Principal(expected); var b = Principal(actual);
        var areaRatio = a.Metrics.SurfaceArea == 0 ? (b.Metrics.SurfaceArea == 0 ? 0 : 1) : Math.Abs(a.Metrics.SurfaceArea - b.Metrics.SurfaceArea) / a.Metrics.SurfaceArea;
        var displacement = new[] { Math.Abs(a.Metrics.BoundsMinimum.X-b.Metrics.BoundsMinimum.X), Math.Abs(a.Metrics.BoundsMinimum.Y-b.Metrics.BoundsMinimum.Y), Math.Abs(a.Metrics.BoundsMinimum.Z-b.Metrics.BoundsMinimum.Z), Math.Abs(a.Metrics.BoundsMaximum.X-b.Metrics.BoundsMaximum.X), Math.Abs(a.Metrics.BoundsMaximum.Y-b.Metrics.BoundsMaximum.Y), Math.Abs(a.Metrics.BoundsMaximum.Z-b.Metrics.BoundsMaximum.Z) }.Max();
        var queries = a.Metrics.OfflineQueryFingerprint == b.Metrics.OfflineQueryFingerprint;
        if (areaRatio > areaTolerance) diagnostics.Add($"Surface-area difference ratio {areaRatio:R} exceeds {areaTolerance:R}.");
        if (displacement > boundsTolerance) diagnostics.Add($"Maximum bounds displacement {displacement:R} exceeds {boundsTolerance:R} Unreal units.");
        if (a.Metrics.PlaneCoverageFingerprint != b.Metrics.PlaneCoverageFingerprint) diagnostics.Add("Quantized material/plane coverage differs.");
        if (!queries) diagnostics.Add("Offline ray-query fingerprint differs.");
        var within = areaRatio <= areaTolerance && displacement <= boundsTolerance && (!requireOfflineQueries || queries);
        return new BspComparison(GeometryFormatVersions.BspComparison, GeometryFormatVersions.BspProfile, within, areaRatio, displacement, queries, diagnostics);
    }
    private static BspModel Principal(BspMapIr map) => map.Models.OrderByDescending(x => x.Polygons.Count).FirstOrDefault() ?? throw new InvalidDataException("BSP IR has no decoded models.");
}
