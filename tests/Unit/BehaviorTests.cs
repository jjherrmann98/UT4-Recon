using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Ut4Recon.Core;
using Ut4Recon.Package;

namespace Ut4Recon.UnitTests;

public class BehaviorTests
{
    private const string InternalPath = "UnrealTournament/Content/InvestigationAssets/Blueprint_CeilingLight.uasset";

    [Fact]
    public void InventoriesCeilingLightCompiledBehavior()
    {
        var package = new BehaviorAnalyzer().InspectPackage(Fixture(), InternalPath); var generatedClass = Assert.Single(package.Classes); var function = Assert.Single(package.Functions);
        Assert.Equal("Blueprint_CeilingLight_C", generatedClass.ObjectPath); Assert.Equal("/Script/Engine.Actor", generatedClass.SuperPath); Assert.Equal("Default__Blueprint_CeilingLight_C", generatedClass.DefaultObjectPath); Assert.Equal(17, generatedClass.OwnedObjects.Count);
        Assert.Equal("Blueprint_CeilingLight_C.UserConstructionScript", function.ObjectPath); Assert.Equal(127, function.ScriptBytecodeSize); Assert.Equal(3, function.OpcodeCounts["EX_Context"]); Assert.Equal(3, function.OpcodeCounts["EX_FinalFunction"]);
        Assert.Equal(["/Script/Engine.LightComponent.SetIntensity", "/Script/Engine.LightComponent.SetLightColor", "/Script/Engine.PointLightComponent.SetSourceRadius"], function.Calls);
    }

    [Fact]
    public void DetectsChangedClassDefaultWhileConfirmingFunctionBytecodeIsUnchanged()
    {
        var analyzer = new BehaviorAnalyzer(); var expectedPackage = analyzer.InspectPackage(Fixture(), InternalPath); var expected = Snapshot(expectedPackage);
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); var candidatePath = Path.Combine(directory, "Blueprint_CeilingLight.uasset");
        try
        {
            Directory.CreateDirectory(directory); var asset = new UAsset(Fixture(), EngineVersion.VER_UE4_15); var cdo = (NormalExport)asset.Exports[8]; var brightness = cdo.Data.OfType<FloatPropertyData>().Single(x => x.Name.ToString() == "Brightness"); brightness.Value += 1; asset.Write(candidatePath);
            var candidatePackage = analyzer.InspectPackage(candidatePath, InternalPath); var report = new BehaviorValidator().Compare(expected, Snapshot(candidatePackage));
            Assert.False(report.Passed); Assert.Contains(report.Differences, x => x.Contains("Generated class", StringComparison.Ordinal));
            Assert.Equal(expectedPackage.Functions[0].BytecodeFingerprint, candidatePackage.Functions[0].BytecodeFingerprint); Assert.Equal(expectedPackage.Functions[0].SerializedFunctionHash, candidatePackage.Functions[0].SerializedFunctionHash);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CertifiedNativeDependencyAdditionDoesNotRelaxCompiledBehaviorChecks()
    {
        var analyzer = new BehaviorAnalyzer(); var package = analyzer.InspectPackage(Fixture(), InternalPath); var expected = Snapshot(package);
        var candidatePackage = package with
        {
            ImportPackages = [.. package.ImportPackages, "/Script/UnrealTournament"],
            DependencyFingerprint = new string('1', 64)
        };
        var allowed = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [InternalPath] = new HashSet<string>(["/Script/UnrealTournament"], StringComparer.Ordinal)
        };
        Assert.True(new BehaviorValidator().Compare(expected, Snapshot(candidatePackage), allowed).Passed);

        var changedFunction = candidatePackage.Functions[0] with { BytecodeFingerprint = new string('2', 64) };
        var changedBehavior = candidatePackage with { Functions = [changedFunction] };
        var rejected = new BehaviorValidator().Compare(expected, Snapshot(changedBehavior), allowed);
        Assert.False(rejected.Passed); Assert.Contains(rejected.Differences, x => x.Contains("Compiled function changed", StringComparison.Ordinal));
    }

    private static BehaviorSnapshot Snapshot(BehaviorPackage package) => new(FormatVersions.BehaviorSnapshot, "ut4-4.15-windows-no-editor-v1", [package], []);
    private static string Fixture() => Path.Combine(RepositoryRoot(), "research", "investigation", "cooked-fixtures", "Blueprint_CeilingLight.uasset");
    private static string RepositoryRoot() { var directory = new DirectoryInfo(AppContext.BaseDirectory); while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ut4Recon.sln"))) directory = directory.Parent; return directory?.FullName ?? throw new DirectoryNotFoundException(); }
}
