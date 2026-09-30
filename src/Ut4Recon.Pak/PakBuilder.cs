using System.Diagnostics;
using System.Text;
using Ut4Recon.Core;

namespace Ut4Recon.Pak;

public sealed class PakBuilder
{
    public FileIdentity Build(PakInventory inventory, string stagingRoot, string outputPak, string logPath, IReadOnlyDictionary<string, string>? pathRedirects = null)
    {
        var unrealPak = inventory.UnrealPak.Path;
        if (!File.Exists(unrealPak) || Identity.Sha256File(unrealPak) != inventory.UnrealPak.Sha256) throw new InvalidDataException("The selected UnrealPak binary no longer matches the input manifest.");
        if (inventory.Entries.Any(x => x.Path.Contains('"'))) throw new InvalidDataException("Pak paths containing quotes are unsupported.");

        var output = Path.GetFullPath(outputPak); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var response = Path.Combine(stagingRoot, "pak-response.txt");
        pathRedirects ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = inventory.Entries.OrderBy(x => x.Sequence).Select(entry =>
        {
            var stagedPath = pathRedirects.GetValueOrDefault(entry.Path, entry.Path);
            var source = Path.GetFullPath(Path.Combine(stagingRoot, stagedPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(source)) throw new FileNotFoundException("Staged pak entry is missing", source);
            return $"\"{source.Replace('\\', '/')}\" \"../../../{stagedPath}\"";
        });
        File.WriteAllLines(response, lines, Encoding.ASCII);
        if (File.Exists(output)) File.Delete(output);

        var log = new StringBuilder();
        Run(unrealPak, [output, $"-create={response}", "-compress"], log);
        Run(unrealPak, [output, "-test"], log);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!); File.WriteAllText(logPath, log.ToString(), new UTF8Encoding(false));
        return Identity.File(output);
    }

    private static void Run(string executable, IReadOnlyList<string> arguments, StringBuilder log)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start UnrealPak.");
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); process.WaitForExit(); Task.WaitAll(stdout, stderr);
        log.AppendLine($"> {Path.GetFileName(executable)} {string.Join(' ', arguments)}"); log.Append(stdout.Result); log.Append(stderr.Result);
        if (process.ExitCode != 0) throw new InvalidOperationException($"UnrealPak failed with exit code {process.ExitCode}: {stderr.Result}{stdout.Result}");
    }
}
