using System.Globalization;
using System.Text.RegularExpressions;
using Ut4Recon.Core;

namespace Ut4Recon.EditorProtocol;

public sealed partial class RuntimeLogCertifier
{
    public RuntimeCertificationReport CertifyPackageLoad(string profile, string pakPath, string logPath, string mapPackagePath, string? expectedGameMode = null)
    {
        pakPath = Path.GetFullPath(pakPath); logPath = Path.GetFullPath(logPath);
        if (!File.Exists(pakPath)) throw new FileNotFoundException("Runtime-tested pak was not found.", pakPath);
        if (!File.Exists(logPath)) throw new FileNotFoundException("Runtime log was not found.", logPath);
        if (!mapPackagePath.StartsWith("/Game/", StringComparison.Ordinal)) throw new ArgumentException("Map package path must start with /Game/.", nameof(mapPackagePath));

        var text = File.ReadAllText(logPath);
        var lines = text.Split('\n');
        var pakName = Path.GetFileName(pakPath);
        var mountIndex = Array.FindIndex(lines, line => line.Contains("Mounted", StringComparison.OrdinalIgnoreCase) && line.Contains(pakName, StringComparison.OrdinalIgnoreCase));
        var requestIndex = mountIndex < 0 ? -1 : Array.FindIndex(lines, mountIndex + 1, line => line.Contains("LoadMap:", StringComparison.Ordinal) && line.Contains(mapPackagePath, StringComparison.OrdinalIgnoreCase));
        var completionIndex = -1; Match? completion = null;
        for (var index = requestIndex + 1; requestIndex >= 0 && index < lines.Length; index++)
        {
            var match = LoadCompleteRegex().Match(lines[index]);
            if (!match.Success || !match.Groups["map"].Value.Equals(mapPackagePath, StringComparison.OrdinalIgnoreCase)) continue;
            completionIndex = index; completion = match; break;
        }
        var mounted = mountIndex >= 0; var requested = requestIndex >= 0; var completed = completionIndex >= 0;
        double? loadSeconds = completed ? double.Parse(completion!.Groups["seconds"].Value, CultureInfo.InvariantCulture) : null;
        string? observedGameMode = null;
        if (requestIndex >= 0)
        {
            var last = completionIndex >= 0 ? completionIndex : lines.Length - 1;
            for (var index = requestIndex; index <= last; index++)
            {
                var match = GameModeRegex().Match(lines[index]); if (match.Success) observedGameMode = match.Groups["mode"].Value;
            }
        }
        var gameModePassed = expectedGameMode is null || string.Equals(expectedGameMode, observedGameMode, StringComparison.OrdinalIgnoreCase);
        var checks = new List<RuntimeCertificationCheck>
        {
            new("pak-mounted", mounted, mounted ? $"Runtime mounted {pakName}." : $"No mount record for {pakName}."),
            new("map-requested", requested, requested ? $"Runtime requested {mapPackagePath}." : $"No LoadMap request for {mapPackagePath}."),
            new("map-load-completed", completed, completed ? $"LoadMap completed in {loadSeconds:0.000000} seconds." : $"No completed LoadMap record for {mapPackagePath}.")
        };
        if (expectedGameMode is not null)
            checks.Add(new("game-mode", gameModePassed, gameModePassed ? $"Runtime selected {observedGameMode}." : $"Expected {expectedGameMode}; observed {observedGameMode ?? "no game mode"}."));
        var passed = checks.All(check => check.Passed);
        return new RuntimeCertificationReport(FormatVersions.RuntimeCertificationReport, profile, DateTimeOffset.UtcNow, "package-load",
            Identity.File(pakPath), Identity.File(logPath), mapPackagePath, observedGameMode, loadSeconds, checks,
            ["The runtime log identifies the mounted pak by filename, not by content hash; the report hashes the supplied pak for traceability.", "Live actor and component state was not queried.", "Player movement, collision, pickup, and other interaction behavior was not tested."], passed);
    }

    [GeneratedRegex(@"Took\s+(?<seconds>[0-9]+(?:\.[0-9]+)?)\s+seconds\s+to\s+LoadMap\((?<map>[^)]+)\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LoadCompleteRegex();

    [GeneratedRegex(@"Game class is\s+'(?<mode>[^']+)'", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex GameModeRegex();
}
