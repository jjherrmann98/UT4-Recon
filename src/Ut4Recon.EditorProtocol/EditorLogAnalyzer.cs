using System.Text.RegularExpressions;
using Ut4Recon.Core;

namespace Ut4Recon.EditorProtocol;

public sealed partial class EditorLogAnalyzer
{
    public EditorMapCheckReport AnalyzeMapCheck(string logPath)
    {
        var lines = File.ReadAllLines(logPath); var summary = lines.Select(line => SummaryRegex().Match(line)).LastOrDefault(match => match.Success);
        if (summary is null || !summary.Success) throw new InvalidDataException("The editor log has no completed Map Check summary.");
        var messages = lines.Where(line => line.Contains("MapCheck:Warning:", StringComparison.Ordinal) || line.Contains("MapCheck:Error:", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("MapCheck:", StringComparison.Ordinal))..]).Distinct(StringComparer.Ordinal).ToArray();
        var errors = int.Parse(summary.Groups["errors"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var warnings = int.Parse(summary.Groups["warnings"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return new EditorMapCheckReport(FormatVersions.EditorMapCheckReport, Identity.File(logPath), errors, warnings, messages, errors == 0);
    }

    [GeneratedRegex(@"Map check complete:\s*(?<errors>\d+) Error\(s\),\s*(?<warnings>\d+) Warning\(s\)", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryRegex();
}
