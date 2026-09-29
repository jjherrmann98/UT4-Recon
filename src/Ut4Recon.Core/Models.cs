using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ut4Recon.Core;

public static class FormatVersions { public const int InputManifest = 1, ReconstructionIr = 1, SupportReport = 1, EditManifest = 1, ValidationReport = 1, BehaviorSnapshot = 1, BehaviorValidationReport = 1, ProxyScene = 2, EditorWorkspace = 3, EditorWorkspaceReport = 1, EditorMapCheckReport = 1, RuntimeCertificationReport = 1, AssetExtractionManifest = 1, ReplacementContract = 1, DonorValidationReport = 1, InjectionReport = 1, ExternalPropertyDocument = 1, CollisionBoxTemplate = 1, CustomCollisionWorkspace = 1, CustomCollisionDonor = 1; }

[JsonConverter(typeof(JsonStringEnumConverter<SupportLevel>))]
public enum SupportLevel { Preserve, PropertyEditable, Reconstructable, ProxyOnly, Unsupported }

[JsonConverter(typeof(JsonStringEnumConverter<FidelityCategory>))]
public enum FidelityCategory { ExactEditable, Reconstructed, BehavioralProxy, PreserveOnly }

[JsonConverter(typeof(JsonStringEnumConverter<CapabilityState>))]
public enum CapabilityState { Available, RequiresDonor, Blocked }

[JsonConverter(typeof(JsonStringEnumConverter<AssetReplacementKind>))]
public enum AssetReplacementKind { PropertySet, StaticMeshCollision, BspModelClosure }

[JsonConverter(typeof(JsonStringEnumConverter<DonorMode>))]
public enum DonorMode { ExternalDocument, CompatibleShell, FreshShell }

public sealed record ToolIdentity(string Name, string Version);
public sealed record FileIdentity(string Path, long Size, string Sha256);
public sealed record PakEntry(int Sequence, string Path, long Offset, long Size, long StoredSize, string CompressionMethod, bool IsEncrypted, string Sha256);
public sealed record PakInventory(string SourcePath, string MountPoint, bool IsEncrypted, FileIdentity Source, FileIdentity UnrealPak, FileIdentity? Editor, IReadOnlyList<PakEntry> Entries);
public sealed record CustomVersion(string Key, string Name, int Version);
public sealed record PackageObject(string Id, string ObjectPath, string? ClassPath, int ExportIndex, long SerialSize, string PayloadHash, int TaggedPropertyCount, int OpaqueNativeBytes, SupportLevel Support, string SupportReason);
public sealed record PackageInventory(string InternalPath, string PackagePath, FileIdentity File, IReadOnlyList<FileIdentity> Sidecars, string ObjectVersion, string RecordedEngineVersion, string PackageFlags, bool IsFilterEditorOnly, bool ContainsMap, int NameCount, int ImportCount, int ExportCount, IReadOnlyList<CustomVersion> CustomVersions, IReadOnlyList<PackageObject> Objects, IReadOnlyList<string> ImportPackages, IReadOnlyList<string> UnavailableAuthoringFields, IReadOnlyList<string> Diagnostics);
public sealed record InputManifest(int SchemaVersion, ToolIdentity Tool, DateTimeOffset CreatedUtc, string Profile, PakInventory Pak, IReadOnlyList<FileIdentity> ExtractedFiles);
public sealed record ReconstructionIr(int SchemaVersion, string Profile, FileIdentity InputPak, IReadOnlyList<PackageInventory> Packages);
public sealed record SupportCount(SupportLevel Level, int Count);
public sealed record SupportReport(int SchemaVersion, string Profile, IReadOnlyList<SupportCount> Counts, IReadOnlyList<string> Diagnostics, bool CanCreateEditableProject);
public sealed record EditOperation(string Operation, string PackagePath, string ObjectId, string ObjectPath, string OriginalPayloadHash, string PropertyPath, JsonElement Before, JsonElement After, string Source, string SupportRule);
public sealed record EditManifest(int SchemaVersion, string Profile, FileIdentity InputPak, FileIdentity ReconstructionIr, IReadOnlyList<EditOperation> Operations);
public sealed record ChangedExport(string PackagePath, string ObjectId, string ObjectPath, string BeforePayloadHash, string AfterPayloadHash, IReadOnlyList<string> ApprovedProperties);
public sealed record ValidationReport(int SchemaVersion, string Profile, FileIdentity InputPak, FileIdentity EditManifest, FileIdentity OutputPak, IReadOnlyList<ChangedExport> ChangedExports, int UntouchedExportsVerified, IReadOnlyList<string> Diagnostics, bool Passed);
public sealed record BehaviorOwnedObject(string ObjectPath, string? ClassPath, int ExportIndex, string PayloadHash);
public sealed record BehaviorFunction(string ObjectPath, int ExportIndex, int ScriptBytecodeSize, string BytecodeFingerprint, string SerializedFunctionHash, IReadOnlyDictionary<string, int> OpcodeCounts, IReadOnlyList<string> Calls);
public sealed record BehaviorClass(string ObjectPath, int ExportIndex, string? SuperPath, string? DefaultObjectPath, string MetadataFingerprint, string? DefaultObjectPayloadHash, string DelegateFingerprint, IReadOnlyList<string> FunctionPaths, IReadOnlyList<BehaviorOwnedObject> OwnedObjects);
public sealed record BehaviorPackage(string InternalPath, string PackagePath, string SourceSha256, IReadOnlyList<string> ImportPackages, string DependencyFingerprint, IReadOnlyList<BehaviorClass> Classes, IReadOnlyList<BehaviorFunction> Functions);
public sealed record BehaviorSnapshot(int SchemaVersion, string Profile, IReadOnlyList<BehaviorPackage> Packages, IReadOnlyList<string> Diagnostics);
public sealed record BehaviorValidationReport(int SchemaVersion, string Profile, string ExpectedFingerprint, string CandidateFingerprint, int PackagesVerified, int ClassesVerified, int FunctionsVerified, IReadOnlyList<string> Differences, bool Passed);

public static class EditRules
{
    private static readonly IReadOnlyDictionary<string, string> Rules = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RelativeLocation"] = "native-component-transform-v1",
        ["RelativeRotation"] = "native-component-transform-v1",
        ["RelativeScale3D"] = "native-component-transform-v1",
        ["BoxExtent"] = "primitive-collision-shape-v1",
        ["CapsuleHalfHeight"] = "primitive-collision-shape-v1",
        ["CapsuleRadius"] = "primitive-collision-shape-v1",
        ["SphereRadius"] = "primitive-collision-shape-v1",
        ["OverrideMaterials"] = "material-override-existing-reference-v1",
        ["CollisionProfileName"] = "native-component-collision-v1",
        ["BodyInstance.CollisionProfileName"] = "native-component-collision-v1",
        ["BodyInstance.CollisionEnabled"] = "native-component-collision-v1",
        ["BodyInstance.ObjectType"] = "native-component-collision-v1",
        ["CollisionTraceFlag"] = "body-setup-collision-v1",
        ["bDoubleSidedGeometry"] = "body-setup-collision-v1",
        ["bGenerateMirroredCollision"] = "body-setup-collision-v1",
        ["Title"] = "ut-level-summary-title-v1"
    };

    private const string CollisionResponsePrefix = "BodyInstance.CollisionResponses.";

    public static string? RuleFor(string propertyPath)
    {
        if (Rules.TryGetValue(propertyPath, out var rule)) return rule;
        if (!propertyPath.StartsWith(CollisionResponsePrefix, StringComparison.Ordinal)) return null;
        var channel = propertyPath[CollisionResponsePrefix.Length..];
        return channel.Length > 0 && channel.All(character => char.IsLetterOrDigit(character) || character == '_')
            ? "native-component-collision-response-v1"
            : null;
    }

    public static bool IsAllowlisted(string propertyPath) => RuleFor(propertyPath) is not null;
    public static IReadOnlyList<string> KnownPaths => Rules.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
}

public sealed record ProxyProperty(string PropertyPath, JsonElement Value, string SupportRule);
public sealed record ProxyBehavior(string Policy, string ClassPath, string PackagePath, int CompiledFunctionCount, string MetadataFingerprint);
public sealed record ProxyCapability(string Operation, string? PropertyPath, CapabilityState State, string Interface, string? SupportRule, string Reason);
public sealed record ProxyCollision(string SourceKind, string GeometryFidelity, string? GeometryObjectId, string Visualization, IReadOnlyList<string> EditableProperties, string Limitation);
public sealed record FidelityCount(FidelityCategory Category, int Count);
public sealed record ProxyObject(string ReconstructionId, string ObjectPath, string? ClassPath, int ExportIndex, string? OwnerReconstructionId, string Kind, string EditorName, SupportLevel Support,
    FidelityCategory Fidelity, IReadOnlyList<string> Evidence, IReadOnlyList<string> InventedEditorMetadata, IReadOnlyList<string> Limitations,
    IReadOnlyList<ProxyCapability> Capabilities, ProxyCollision? Collision, ProxyBehavior? Behavior, IReadOnlyList<ProxyProperty> EditableProperties);
public sealed record ProxyScene(int SchemaVersion, string Profile, FileIdentity InputPak, FileIdentity ReconstructionIr, string MapPackagePath, string EditorMapPackagePath,
    IReadOnlyList<FidelityCount> FidelityCounts, IReadOnlyList<ProxyObject> Objects, IReadOnlyList<string> Diagnostics);
public sealed record CollisionOverlayEntry(string SourceComponentId, string GeometryObjectId, string SourceObjectPath, string ActorName, int PolygonCount, string GeometryFidelity);
public sealed record CollisionOverlayIndex(int SchemaVersion, string MapPackagePath, string T3d, IReadOnlyList<CollisionOverlayEntry> Entries, IReadOnlyList<string> Diagnostics);
public sealed record WorkspaceVisualizationIndex(int SchemaVersion, string MapPackagePath, string T3d, string PrincipalModelObjectPath,
    int PrincipalModelExportIndex, int BspPolygonCount, int BspActorCount, int LightingActorCount, IReadOnlyList<string> Limitations);
public sealed record CapabilityCount(string Operation, CapabilityState State, int Count);
public sealed record EditorWorkspaceObjectSummary(string ReconstructionId, string ObjectPath, string? ClassPath, FidelityCategory Fidelity,
    IReadOnlyList<string> AvailableOperations, IReadOnlyList<string> Limitations);
public sealed record EditorWorkspaceReport(int SchemaVersion, string Profile, FileIdentity InputPak, string MapPackagePath, int ObjectCount,
    IReadOnlyList<FidelityCount> FidelityCounts, IReadOnlyList<CapabilityCount> CapabilityCounts, int ExactCollisionOverlayCount,
    int BehavioralProxyCount, int ObjectsWithLimitations, bool RequiresNativePlugin, IReadOnlyList<EditorWorkspaceObjectSummary> Objects,
    IReadOnlyList<string> Diagnostics, int ReconstructedBspContextPolygons = 0, int WorkspaceLightingActorCount = 0);
public sealed record EditorMapCheckReport(int SchemaVersion, FileIdentity Log, int Errors, int Warnings, IReadOnlyList<string> Messages, bool Passed);
public sealed record RuntimeCertificationCheck(string Name, bool Passed, string Detail);
public sealed record RuntimeCertificationReport(int SchemaVersion, string Profile, DateTimeOffset CreatedUtc, string EvidenceLevel,
    FileIdentity OutputPak, FileIdentity Log, string MapPackagePath, string? GameMode, double? LoadSeconds,
    IReadOnlyList<RuntimeCertificationCheck> Checks, IReadOnlyList<string> Limitations, bool Passed);
public sealed record AssetBundleFile(string Role, string RelativePath, FileIdentity File);
public sealed record AssetClosureObject(string Role, string ReconstructionId, string ObjectPath, string? ClassPath, int ExportIndex, string PayloadHash, int OpaqueNativeBytes);
public sealed record AssetDependency(string PackagePath, bool Included, IReadOnlyList<AssetBundleFile> Files);
public sealed record AssetExtractionManifest(int SchemaVersion, string Profile, DateTimeOffset CreatedUtc, FileIdentity InputPak, FileIdentity ReconstructionIr,
    AssetReplacementKind Kind, string PackagePath, string InternalPath, string RootObjectId, string RootObjectPath, string? RootClassPath,
    string ObjectVersion, string RecordedEngineVersion, string PackageFlags, IReadOnlyList<CustomVersion> CustomVersions,
    IReadOnlyList<AssetBundleFile> BaselineFiles, IReadOnlyList<AssetClosureObject> Closure, IReadOnlyList<AssetDependency> Dependencies,
    IReadOnlyList<AssetBundleFile> InterchangeFiles, AssetBundleFile ReplacementContract, IReadOnlyList<string> Limitations);
public sealed record ReplacementContract(int SchemaVersion, string ContractId, string Profile, AssetReplacementKind Kind, string PackagePath, string InternalPath,
    string RootObjectId, string RootObjectPath, string OriginalRootPayloadHash, string ClosureFingerprint, IReadOnlyList<DonorMode> AllowedDonorModes,
    IReadOnlyList<string> DonorRequirements, IReadOnlyList<string> UserOwnedScope, IReadOnlyList<string> PreservedScope, IReadOnlyList<string> RejectedChanges);
public sealed record DonorValidationCheck(string Name, bool Passed, string Detail);
public sealed record ExternalPropertyDocument(int SchemaVersion, string Profile, string PackagePath, string ObjectId, string ObjectPath,
    string OriginalPayloadHash, IReadOnlyList<ProxyProperty> Properties);
public sealed record DonorValidationReport(int SchemaVersion, string ContractId, AssetReplacementKind Kind, DonorMode Mode, FileIdentity Baseline,
    FileIdentity Donor, IReadOnlyList<int> ChangedExportIndices, IReadOnlyList<string> UserOwnedObjects, IReadOnlyList<string> PreservedClaims,
    int UnrelatedExportsVerified, IReadOnlyList<DonorValidationCheck> Checks, IReadOnlyList<string> Diagnostics, bool Passed);
public sealed record InjectionReport(int SchemaVersion, string ContractId, FileIdentity ExtractionManifest, FileIdentity DonorValidation,
    string PackagePath, string RootObjectId, IReadOnlyList<string> Operations, IReadOnlyList<string> SupportRules, string EditManifest, bool Passed);
public sealed record EditorWorkspace(int SchemaVersion, string Profile, FileIdentity ReconstructionIr, FileIdentity Editor, string ProjectFile, string MapPackagePath, string ProxyScene,
    string ImportT3d, string CollisionOverlayT3d, string CollisionOverlayIndex, string ImportCommands, string ExportCommands, string ExportT3d, string Status,
    string RecoveryProject, string BackendExecutable, IReadOnlyList<string> BackendArguments, string DefaultOutputPak, string DefaultMapName,
    string? VisualizationT3d = null, string? VisualizationIndex = null, string? MeshPreviewImport = null, string WorkspaceMode = "safe-visual");
public sealed record EditorDiffReport(int SchemaVersion, string Profile, FileIdentity ReconstructionIr, int ExpectedTaggedObjects, int MatchedTaggedObjects, int ChangedProperties, IReadOnlyList<string> MissingReconstructionIds, IReadOnlyList<string> Diagnostics, bool Passed);
public sealed record CollisionBoxDimensions(double X, double Y, double Z);
public sealed record CollisionBoxTemplateManifest(int SchemaVersion, string Profile, int EditorChangelist, int RuntimeApiVersion,
    FileIdentity Package, string InternalPath, int ActorExportIndex, string ActorObjectPath, IReadOnlyList<AssetClosureObject> Closure,
    CollisionBoxDimensions Dimensions, string GeometryFingerprint, string CollisionFingerprint, IReadOnlyList<string> RequiredImportPackages,
    IReadOnlyList<string> SupportedTransforms);
public sealed record CustomCollisionWorkspace(int SchemaVersion, string Profile, FileIdentity Editor, string ProjectFile, string MapPackagePath,
    string SourceT3d, string ImportCommands, string CookedPackage, string Status, IReadOnlyList<string> Limitations);
public sealed record CustomCollisionDonorManifest(int SchemaVersion, string Profile, int EditorChangelist, int RuntimeApiVersion,
    FileIdentity Package, string InternalPath, int ActorExportIndex, string ActorObjectPath, IReadOnlyList<AssetClosureObject> Closure,
    int PolygonCount, int VertexCount, CollisionBoxDimensions BoundsMinimum, CollisionBoxDimensions BoundsMaximum, string GeometryFingerprint,
    string CollisionFingerprint, IReadOnlyList<string> RequiredImportPackages, IReadOnlyList<string> SupportedTransforms, IReadOnlyList<string> Limitations);

public static class ReconJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    public static void Write<T>(string path, T value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(value, Options) + Environment.NewLine, new UTF8Encoding(false)); }
}

public static class Identity
{
    public static string Sha256File(string path) { using var stream = System.IO.File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    public static string Sha256Bytes(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static FileIdentity File(string path, string? reportedPath = null) { var info = new FileInfo(path); return new FileIdentity(reportedPath ?? info.FullName, info.Length, Sha256File(info.FullName)); }
    public static string ObjectId(string packagePath, string objectPath, string? classPath) => Sha256Bytes(Encoding.UTF8.GetBytes(packagePath + "\n" + objectPath + "\n" + (classPath ?? "")));
}

public static class PackagePaths
{
    public static string FromInternalPath(string internalPath)
    {
        var path = internalPath.Replace('\\', '/'); var extension = Path.GetExtension(path);
        if (extension.Equals(".umap", StringComparison.OrdinalIgnoreCase) || extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase)) path = path[..^extension.Length];
        const string game = "UnrealTournament/Content/", engine = "Engine/Content/";
        var at = path.IndexOf(game, StringComparison.OrdinalIgnoreCase); if (at >= 0) return "/Game/" + path[(at + game.Length)..];
        at = path.IndexOf(engine, StringComparison.OrdinalIgnoreCase); if (at >= 0) return "/Engine/" + path[(at + engine.Length)..];
        return "/Unknown/" + path.TrimStart('/');
    }
}
