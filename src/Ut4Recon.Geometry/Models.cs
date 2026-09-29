namespace Ut4Recon.Geometry;

public sealed record MeshVector(double X, double Y, double Z);
public sealed record MeshUv(double U, double V);
public sealed record MeshBounds(MeshVector Origin, MeshVector Extent, double SphereRadius);
public sealed record MeshSection(int MaterialIndex, int FirstIndex, int TriangleCount, int MinimumVertexIndex, int MaximumVertexIndex, bool CollisionEnabled, bool CastShadow);
public sealed record MeshLod(
    int Index,
    IReadOnlyList<MeshVector> Positions,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<MeshVector> TangentX,
    IReadOnlyList<MeshVector> TangentZ,
    IReadOnlyList<IReadOnlyList<MeshUv>> UvChannels,
    IReadOnlyList<MeshSection> Sections,
    string PositionHash,
    string IndexHash,
    string AttributeHash);
public sealed record ConvexHull(int Index, IReadOnlyList<MeshVector> Vertices, MeshVector BoundsMinimum, MeshVector BoundsMaximum, string VertexHash);
public sealed record CookedCollisionPayload(string Format, int ElementCount, int SizeOnDisk, long OffsetInFile, string PayloadHash, bool IsInline);
public sealed record CollisionIr(
    int BodySetupExportIndex,
    string BodySetupObjectName,
    IReadOnlyList<ConvexHull> ConvexHulls,
    IReadOnlyList<CookedCollisionPayload> CookedPayloads,
    string TaggedGeometryHash,
    string OpaqueNativeHash,
    int OfflineQueryCount,
    string OfflineQueryFingerprint,
    bool HasConsistentSimpleCollision);
public sealed record StaticMeshIr(
    int SchemaVersion,
    string Profile,
    string VirtualPath,
    string SourceFileSha256,
    string ObjectName,
    int StaticMeshExportIndex,
    MeshBounds Bounds,
    IReadOnlyList<MeshLod> Lods,
    CollisionIr? Collision,
    IReadOnlyList<string> Diagnostics);
public sealed record MeshComparison(
    int SchemaVersion,
    string Profile,
    bool RenderEquivalent,
    bool CollisionEquivalent,
    IReadOnlyList<string> Diagnostics);
public sealed record MeshEditorWorkspace(
    int SchemaVersion,
    string Profile,
    string MeshIr,
    string EditorObj,
    string RenderObj,
    string CollisionObj,
    string ImportSettings,
    string DestinationPath,
    string Status);
public sealed record MeshWorkspaceEntry(string PackagePath, string InternalPath, string? WorkspacePath, string Status, IReadOnlyList<string> Diagnostics);
public sealed record MeshWorkspaceIndex(int SchemaVersion, string Profile, IReadOnlyList<MeshWorkspaceEntry> Entries);
public sealed record MeshPreviewImportEntry(string PackagePath, string AssetName, string DestinationPath, string SourceObj, string Fidelity, IReadOnlyList<string> Limitations);
public sealed record MeshPreviewImportManifest(int SchemaVersion, string Profile, IReadOnlyList<MeshPreviewImportEntry> Entries, IReadOnlyList<string> Limitations);
public sealed record CollisionGraftReport(
    int SchemaVersion,
    string Profile,
    string BaselineSha256,
    string DonorSha256,
    string OutputSha256,
    string StaticMeshPayloadHashBefore,
    string StaticMeshPayloadHashAfter,
    string BodySetupPayloadHashBefore,
    string BodySetupPayloadHashDonor,
    string BodySetupPayloadHashAfter,
    bool RenderPreserved,
    bool CollisionMatchesDonor,
    int NavigationCollisionExportCount,
    bool NavigationCollisionMatchesDonor,
    IReadOnlyList<string> Diagnostics,
    bool Passed);

public sealed record BspNode(
    int Index, double PlaneX, double PlaneY, double PlaneZ, double PlaneW,
    int VertexPoolStart, int SurfaceIndex, int VertexIndex, ushort ComponentIndex, ushort ComponentNodeIndex, int ComponentElementIndex,
    int BackNode, int FrontNode, int PlaneNode, int CollisionBound,
    byte Zone0, byte Zone1, byte VertexCount, byte Flags, int Leaf0, int Leaf1);
public sealed record BspSurface(
    int Index, string? Material, uint Flags, int BasePointIndex, int NormalVectorIndex,
    int TextureUVectorIndex, int TextureVVectorIndex, int BrushPolygonIndex, string? Actor,
    double PlaneX, double PlaneY, double PlaneZ, double PlaneW, double LightMapScale, int LightmassIndex);
public sealed record BspVertexPoolEntry(int Index, int PointIndex, int SideIndex, double? ShadowU, double? ShadowV, double? BackfaceShadowU, double? BackfaceShadowV);
public sealed record BspPolygon(
    int NodeIndex, int SurfaceIndex, IReadOnlyList<MeshVector> Positions, string? Material, uint Flags,
    MeshVector Origin, MeshVector Normal, MeshVector TextureU, MeshVector TextureV, double LightMapScale, double Area);
public sealed record BspMetrics(
    MeshVector BoundsMinimum, MeshVector BoundsMaximum, double SurfaceArea, int PlaneGroupCount,
    string PlaneCoverageFingerprint, int OfflineQueryCount, string OfflineQueryFingerprint);
public sealed record BspModel(
    string ObjectPath, int ExportIndex, IReadOnlyList<MeshVector> Vectors, IReadOnlyList<MeshVector> Points,
    IReadOnlyList<BspNode> Nodes, IReadOnlyList<BspSurface> Surfaces, IReadOnlyList<BspVertexPoolEntry> VertexPool,
    IReadOnlyList<BspPolygon> Polygons, int ParsedGeometryBytes, int RemainingNativeBytes,
    string HeaderHash, string RemainingNativeHash, BspMetrics Metrics);
public sealed record BspMapIr(
    int SchemaVersion, string Profile, string VirtualPath, string SourceFileSha256,
    IReadOnlyList<BspModel> Models, IReadOnlyList<string> Diagnostics);
public sealed record BspComparison(
    int SchemaVersion, string Profile, bool WithinTolerance, double SurfaceAreaDifferenceRatio,
    double MaximumBoundsDisplacement, bool OfflineQueriesEquivalent, IReadOnlyList<string> Diagnostics);
public sealed record BspEditorWorkspace(
    int SchemaVersion, string Profile, string BspIr, string T3d, string Obj,
    string DestinationPath, string Status, IReadOnlyList<string> InventedEditorMetadata);
public sealed record BspGraftReport(
    int SchemaVersion, string Profile, string BaselineSha256, string DonorSha256, string OutputSha256,
    IReadOnlyList<int> ClosureExportIndices, IReadOnlyList<int> ChangedClosureExportIndices, IReadOnlyList<int> AddedClosureExportIndices,
    int PreservedLevelExportCount, bool LevelExportsPreserved, int UnrelatedExportsVerified,
    bool GeometryMatchesDonor, bool CollisionClosureMatchesDonor, IReadOnlyList<string> Diagnostics, bool Passed);
public sealed record BspWorkspaceEntry(string PackagePath, string InternalPath, string? WorkspacePath, string Status, IReadOnlyList<string> Diagnostics);
public sealed record BspWorkspaceIndex(int SchemaVersion, string Profile, IReadOnlyList<BspWorkspaceEntry> Entries);

public static class GeometryFormatVersions
{
    public const int StaticMeshIr = 1;
    public const int MeshComparison = 1;
    public const int CollisionGraftReport = 1;
    public const int MeshEditorWorkspace = 1;
    public const int MeshWorkspaceIndex = 1;
    public const int BspMapIr = 1;
    public const int BspComparison = 1;
    public const int BspEditorWorkspace = 1;
    public const int BspGraftReport = 1;
    public const int BspWorkspaceIndex = 1;
    public const string Profile = "ut4-4.15-static-mesh-windows-v1";
    public const string BspProfile = "ut4-4.15-bsp-windows-v1";
}
