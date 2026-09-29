using Ut4Recon.EditorProtocol;

namespace Ut4Recon.UnitTests;

public class EditorProtocolPublicTests
{
    [Fact]
    public void GeneratesSafeDeterministicNamesForDuplicatedCollisionActors()
    {
        const string sourceId = "f48ba56231e079ce7591d3a5a47d2e9a9373899e2cc408f76811bd00f95254ae";
        const string invalidEditorName = "ExactcollisionoverlayCTF-Switchback-PRO2PersistentLevelBlockingVolume_1BrushComponent1";

        var first = EditorDiffExporter.CollisionCloneActorName(sourceId, invalidEditorName);
        var repeated = EditorDiffExporter.CollisionCloneActorName(sourceId, invalidEditorName);
        var secondClone = EditorDiffExporter.CollisionCloneActorName(sourceId, invalidEditorName + "_2");

        Assert.Matches("^[A-Za-z0-9_]+$", first);
        Assert.Equal(first, repeated);
        Assert.NotEqual(first, secondClone);
        Assert.StartsWith("UT4Recon_Blocker_f48ba56231e0_", first, StringComparison.Ordinal);
    }

    [Fact]
    public void CertifiesOnlyPackageLoadEvidenceFromRuntimeLog()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var pak = Path.Combine(directory, "Repaired-WindowsNoEditor.pak"); var log = Path.Combine(directory, "server.log");
        try
        {
            File.WriteAllText(pak, "pak fixture");
            File.WriteAllLines(log, [
                "UT4UU: Mounted '../../../UnrealTournament/Content/Paks/Repaired-WindowsNoEditor.pak'",
                "LogLoad: LoadMap: /Game/Maps/Repaired?Game=/Script/UnrealTournament.UTDMGameMode",
                "LogLoad: Game class is 'UTDMGameMode'",
                "LogLoad: Took 0.125 seconds to LoadMap(/Game/Maps/Repaired)"]);
            var report = new RuntimeLogCertifier().CertifyPackageLoad("profile", pak, log, "/Game/Maps/Repaired", "UTDMGameMode");
            Assert.True(report.Passed); Assert.Equal("package-load", report.EvidenceLevel); Assert.Equal(0.125, report.LoadSeconds);
            Assert.All(report.Checks, check => Assert.True(check.Passed));
            Assert.Contains(report.Limitations, limitation => limitation.Contains("interaction", StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void RejectsPackageLoadClaimWhenExactPakWasNotMounted()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ut4recon-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var pak = Path.Combine(directory, "Expected.pak"); var log = Path.Combine(directory, "server.log");
        try
        {
            File.WriteAllText(pak, "pak fixture");
            File.WriteAllLines(log, ["UT4UU: Mounted 'Different.pak'", "LogLoad: LoadMap: /Game/Maps/Repaired", "LogLoad: Took 1.0 seconds to LoadMap(/Game/Maps/Repaired)"]);
            var report = new RuntimeLogCertifier().CertifyPackageLoad("profile", pak, log, "/Game/Maps/Repaired");
            Assert.False(report.Passed); Assert.False(report.Checks.Single(check => check.Name == "pak-mounted").Passed);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void ReportsCompletedEditorMapCheckWarnings()
    {
        var temporary = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(temporary, [
                "[time]MapCheck:Warning: Warning MissingMesh Static mesh actor has NULL StaticMesh property",
                "[time]MapCheck: Info Map check complete: 0 Error(s), 126 Warning(s), took 1ms to complete."]);
            var report = new EditorLogAnalyzer().AnalyzeMapCheck(temporary);
            Assert.Equal(0, report.Errors); Assert.Equal(126, report.Warnings); Assert.Single(report.Messages); Assert.True(report.Passed);
        }
        finally { File.Delete(temporary); }
    }
}
