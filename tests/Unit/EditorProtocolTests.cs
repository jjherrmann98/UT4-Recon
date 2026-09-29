using System.Text.RegularExpressions;
using Ut4Recon.Core;
using Ut4Recon.EditorProtocol;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class EditorProtocolTests
{
    [Fact]
    public void EmitsExactTaggedCollisionOverlaysFromCookedUModels()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var state = Path.Combine(temporary, ".ut4recon");
        try
        {
            const string internalPath = "UnrealTournament/Content/Maps/Example_Map/Example_Map.umap";
            var source = Path.Combine(root, "research", "pairs", "Example_Map", "cooked", "Example_Map.umap"); var baseline = Path.Combine(state, "baseline", internalPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(baseline)!); File.Copy(source, baseline);
            var package = new PackageInspector().Inspect(baseline, internalPath); var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", Identity.File(source), [package]);
            ReconJson.Write(Path.Combine(state, "reconstruction-ir.json"), ir); var scene = new ProxySceneBuilder().Build(state, ir, "/Game/UT4Recon/Recovered_Example_Map");
            var t3d = Path.Combine(temporary, "collision.t3d"); var indexPath = Path.Combine(temporary, "collision.json"); var index = new CollisionOverlayWriter().Write(state, ir, scene, t3d, indexPath);

            Assert.NotEmpty(index.Entries); Assert.Empty(index.Diagnostics); Assert.All(index.Entries, x => { Assert.Equal("exact-runtime-arrays", x.GeometryFidelity); Assert.True(x.PolygonCount > 0); });
            var text = File.ReadAllText(t3d); Assert.Equal(index.Entries.Count * 2, Regex.Matches(text, "UT4RECON_COLLISION_OVERLAY:").Count);
            Assert.Contains("bActorEnableCollision=False", text); Assert.Contains("Begin Actor Class=/Script/Engine.BlockingVolume", text);

            var visualizationT3d = Path.Combine(temporary, "visualization.t3d"); var visualizationIndex = Path.Combine(temporary, "visualization.json");
            var visualization = new WorkspaceVisualizationWriter().Write(state, ir, scene, visualizationT3d, visualizationIndex);
            Assert.Equal(987, visualization.BspPolygonCount); Assert.Equal(1, visualization.BspActorCount); Assert.Equal(2, visualization.LightingActorCount);
            var visualizationText = File.ReadAllText(visualizationT3d);
            Assert.Equal(987, Regex.Matches(visualizationText, "Begin Polygon").Count);
            Assert.Contains("UT4RECON_VISUALIZATION:BSP_CONTEXT", visualizationText);
            Assert.Equal(2, Regex.Matches(visualizationText, "UT4RECON_VISUALIZATION:LIGHTING").Count);
            Assert.Contains("bActorEnableCollision=False", visualizationText); Assert.Contains("CastShadows=False", visualizationText);

            var reportPath = Path.Combine(temporary, "workspace-support-report.json"); var guidePath = Path.Combine(temporary, "README.md");
            var report = new EditorWorkspaceReportWriter().Write(scene, index, reportPath, guidePath, visualization);
            Assert.Equal(FormatVersions.EditorWorkspaceReport, report.SchemaVersion); Assert.Equal(scene.Objects.Count, report.ObjectCount);
            Assert.Equal(index.Entries.Count, report.ExactCollisionOverlayCount); Assert.False(report.RequiresNativePlugin);
            Assert.Equal(987, report.ReconstructedBspContextPolygons); Assert.Equal(2, report.WorkspaceLightingActorCount);
            Assert.Equal(scene.Objects.Count, report.Objects.Count); Assert.True(File.Exists(reportPath));
            var guide = File.ReadAllText(guidePath); Assert.Contains("run-editor-export", guide); Assert.Contains("Preserve only", guide); Assert.Contains("Do not distribute", guide); Assert.Contains("Reconstructed BSP context", guide);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    [Fact]
    public void BuildsDeterministicGlassProxySceneAndTaggedT3d()
    {
        var root = RepositoryRoot(); var temporary = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var state = Path.Combine(temporary, ".ut4recon");
        try
        {
            var sources = new[]
            {
                ("UnrealTournament/Content/Maps/Glass/Glass.umap", Path.Combine(root, "research/reconstruction/input/Glass/Glass.umap")),
                ("UnrealTournament/Content/Maps/Glass/Blueprints/Block.uasset", Path.Combine(root, "research/reconstruction/input/Glass/Blueprints/Block.uasset")),
                ("UnrealTournament/Content/Maps/Glass/Materials/GlassMaterial.uasset", Path.Combine(root, "research/reconstruction/input/Glass/Materials/GlassMaterial.uasset"))
            };
            var inspector = new PackageInspector(); var packages = new List<PackageInventory>();
            foreach (var source in sources)
            {
                var destination = Path.Combine(state, "baseline", source.Item1.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source.Item2, destination);
                packages.Add(inspector.Inspect(destination, source.Item1));
            }
            var pak = Identity.File(Path.Combine(root, "research/reconstruction/input/Glass-input.pak"));
            var ir = new ReconstructionIr(1, "ut4-4.15-windows-no-editor-v1", pak, packages); ReconJson.Write(Path.Combine(state, "reconstruction-ir.json"), ir);
            var builder = new ProxySceneBuilder(); var first = builder.Build(state, ir, "/Game/UT4Recon/Recovered_Glass"); var second = builder.Build(state, ir, "/Game/UT4Recon/Recovered_Glass");
            Assert.Equal(FormatVersions.ProxyScene, first.SchemaVersion);
            Assert.Equal(44, first.Objects.Count); Assert.Equal(18, first.Objects.Count(x => x.Kind == "actor")); Assert.Equal(25, first.Objects.Count(x => x.Kind == "component"));
            Assert.Equal(first.Objects.Count, first.FidelityCounts.Sum(x => x.Count));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(first, ReconJson.Options), System.Text.Json.JsonSerializer.Serialize(second, ReconJson.Options));
            var floor = first.Objects.Single(x => x.ObjectPath.EndsWith("Floor.StaticMeshComponent0", StringComparison.Ordinal));
            Assert.Equal(FidelityCategory.ExactEditable, floor.Fidelity);
            Assert.Contains(floor.EditableProperties, x => x.PropertyPath == "RelativeLocation");
            Assert.Contains(floor.Capabilities, x => x.Operation == "set-property" && x.PropertyPath == "RelativeLocation" && x.State == CapabilityState.Available && x.Interface == "editor-protocol-v2");
            Assert.Contains(first.Objects.SelectMany(x => x.Capabilities), x => x.PropertyPath == "OverrideMaterials" && x.Interface == "cli-manifest" && x.Reason.Contains("does not capture", StringComparison.Ordinal));
            Assert.Equal("referenced-static-mesh-body-setup", floor.Collision!.SourceKind);
            Assert.Contains(floor.Limitations, x => x.Contains("BodySetup", StringComparison.Ordinal));
            var behaviorProxies = first.Objects.Where(x => x.Kind == "actor" && x.Behavior is not null).ToArray(); Assert.Equal(3, behaviorProxies.Length); Assert.All(behaviorProxies, behaviorProxy => { Assert.Equal(FidelityCategory.BehavioralProxy, behaviorProxy.Fidelity); Assert.Equal("preserve-compiled-class-v1", behaviorProxy.Behavior!.Policy); Assert.Equal("/Game/Maps/Glass/Blueprints/Block", behaviorProxy.Behavior.PackagePath); Assert.Contains(behaviorProxy.Limitations, x => x.Contains("Blueprint graphs", StringComparison.Ordinal)); });

            var t3d1 = Path.Combine(temporary, "one.t3d"); var t3d2 = Path.Combine(temporary, "two.t3d"); var writer = new T3dProxyWriter(); writer.Write(state, ir, first, t3d1); writer.Write(state, ir, first, t3d2);
            Assert.Equal(File.ReadAllBytes(t3d1), File.ReadAllBytes(t3d2)); var text = File.ReadAllText(t3d1);
            Assert.Equal(39, Regex.Matches(text, "UT4RECON:[0-9a-f]{64}").Count); Assert.Contains("StaticMesh=Object'/Engine/BasicShapes/Cube.Cube'", text); Assert.Contains("WorldGridMaterial.WorldGridMaterial", text);
            Assert.Equal(3, Regex.Matches(text, "UT4RECON_BEHAVIOR_PRESERVED").Count); Assert.Contains("[Behavior preserved; proxy] Block2", text);
            Assert.Equal(39, Regex.Matches(text, "UT4RECON_FIDELITY:").Count); Assert.Contains("UT4RECON_FIDELITY:BEHAVIORALPROXY", text);

            var safeScene = builder.Build(state, ir, "/Game/UT4Recon/Recovered_Glass_Safe", visualOnly: true);
            var playerStart = safeScene.Objects.Single(x => x.Kind == "actor" && x.ClassPath == "/Script/Engine.PlayerStart");
            var playerStartCapsule = safeScene.Objects.Single(x => x.OwnerReconstructionId == playerStart.ReconstructionId);
            Assert.Equal(FidelityCategory.Reconstructed, playerStart.Fidelity);
            Assert.DoesNotContain(playerStart.Capabilities, x => x.Operation == "delete-actor" && x.State == CapabilityState.Available);
            Assert.All(playerStartCapsule.EditableProperties, x => Assert.Contains(x.PropertyPath, new[] { "RelativeLocation", "RelativeRotation" }));
            Assert.Contains(playerStartCapsule.Capabilities, x => x.Operation == "set-property" && x.PropertyPath == "RelativeLocation" && x.State == CapabilityState.Available);
            var safeT3d = Path.Combine(temporary, "safe.t3d"); writer.Write(state, ir, safeScene, safeT3d); var safeText = File.ReadAllText(safeT3d);
            Assert.Contains("UT4RECON_PLAYER_START_MARKER", safeText);
            Assert.Contains("[Player start marker; move/rotate only]", safeText);
            Assert.Contains("Class=/Script/Engine.StaticMeshActor Name=\"PlayerStart\"", safeText);
            Assert.DoesNotContain("Class=/Script/Engine.PlayerStart Name=\"PlayerStart\"", safeText);
            var pickupScene = safeScene with { Objects = safeScene.Objects.Select(x => x.ReconstructionId == playerStart.ReconstructionId
                ? x with { ClassPath = "/Game/RestrictedAssets/Pickups/Health/Health_Small.Health_Small_C" } : x).ToArray() };
            var pickupT3d = Path.Combine(temporary, "pickup.t3d"); writer.Write(state, ir, pickupScene, pickupT3d); var pickupText = File.ReadAllText(pickupT3d);
            Assert.Contains("UT4RECON_PICKUP_MARKER", pickupText);
            Assert.Contains("[Pickup marker; move/rotate only]", pickupText);
            Assert.Contains("/Engine/BasicShapes/Sphere.Sphere", pickupText);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}
