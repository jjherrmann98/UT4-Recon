using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Pak.Objects;
using CUE4Parse.UE4.Versions;
using CUE4Parse.UE4.VirtualFileSystem;
using Ut4Recon.Core;

namespace Ut4Recon.Pak;

public sealed class PakExtractor : IDisposable
{
    public const string SupportedUnrealPakSha256 = "f3c6ee2a3703925dae89a561da6fd798c5dfb7f5ddfa2e0db683a32efc516c3d";
    private readonly DefaultFileProvider provider; private readonly IAesVfsReader archive;
    private readonly string unrealPakPath; private readonly FileIdentity unrealPakIdentity; private readonly FileIdentity? editorIdentity;
    public PakExtractor(string pakPath, string unrealPakPath)
    {
        PakPath = Path.GetFullPath(pakPath); if (!File.Exists(PakPath)) throw new FileNotFoundException("Pak not found", PakPath);
        this.unrealPakPath = Path.GetFullPath(unrealPakPath); if (!File.Exists(this.unrealPakPath)) throw new FileNotFoundException("UnrealPak not found", this.unrealPakPath);
        unrealPakIdentity = Identity.File(this.unrealPakPath);
        if (!unrealPakIdentity.Sha256.Equals(SupportedUnrealPakSha256, StringComparison.Ordinal)) throw new NotSupportedException($"UnrealPak does not match the supported UT4 build: {unrealPakIdentity.Sha256}");
        var editorPath = Path.Combine(Path.GetDirectoryName(this.unrealPakPath)!, "UE4Editor.exe"); editorIdentity = File.Exists(editorPath) ? Identity.File(editorPath) : null;
        provider = new DefaultFileProvider(Path.GetDirectoryName(PakPath)!, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE4_15), StringComparer.OrdinalIgnoreCase);
        provider.Initialize(); provider.SubmitKey(new FGuid(), new FAesKey(new byte[32]));
        archive = provider.GetArchive(Path.GetFileName(PakPath), StringComparison.OrdinalIgnoreCase);
        if (archive.IsEncrypted) throw new NotSupportedException("Encrypted paks are unsupported by this milestone.");
    }
    public string PakPath { get; }
    public string MountPoint => archive.MountPoint.Replace('\\', '/');
    public FileIdentity ExtractEntryTo(string internalPath, string outputPath)
    {
        internalPath = NormalizeAndValidate(internalPath);
        if (!provider.TryGetGameFile(internalPath, out var file)) throw new FileNotFoundException($"Pak entry was not found: {internalPath}");
        var bytes = file.Read(); outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!); File.WriteAllBytes(outputPath, bytes);
        return Identity.File(outputPath, internalPath);
    }
    public PakInventory ExtractTo(string baselineDirectory)
    {
        var root = Path.GetFullPath(baselineDirectory); Directory.CreateDirectory(root); var entries = new List<PakEntry>();
        var stateRoot = Path.GetFullPath(Path.GetDirectoryName(root)!); var temporary = Path.GetFullPath(Path.Combine(stateRoot, ".extracting-" + Guid.NewGuid().ToString("N")));
        if (!temporary.StartsWith(stateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe extraction workspace");
        Directory.CreateDirectory(temporary);
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo(unrealPakPath) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.ArgumentList.Add(PakPath); start.ArgumentList.Add("-Extract"); start.ArgumentList.Add(temporary);
            using (var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Unable to start UnrealPak"))
            {
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); process.WaitForExit(); Task.WaitAll(stdout, stderr);
                if (process.ExitCode != 0) throw new InvalidOperationException($"UnrealPak extraction failed ({process.ExitCode}): {stderr.Result}{stdout.Result}");
            }
            var orderedEntries = archive.Files.Select(pair => (Path: pair.Key, Entry: (FPakEntry)pair.Value)).OrderBy(x => x.Entry.Offset).ToArray();
            for (var sequence = 0; sequence < orderedEntries.Length; sequence++)
            {
                var pair = orderedEntries[sequence]; var internalPath = NormalizeAndValidate(pair.Path); var output = Path.GetFullPath(Path.Combine(root, internalPath.Replace('/', Path.DirectorySeparatorChar)));
                if (!output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Pak entry escapes baseline: {pair.Path}");
                var relative = internalPath.StartsWith(MountPoint, StringComparison.OrdinalIgnoreCase) ? internalPath[MountPoint.Length..] : internalPath;
                var extracted = Path.GetFullPath(Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!extracted.StartsWith(temporary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(extracted)) throw new InvalidDataException($"UnrealPak did not extract expected entry: {internalPath}");
                var bytes = File.ReadAllBytes(extracted); Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                if (File.Exists(output) && Identity.Sha256File(output) != Identity.Sha256Bytes(bytes)) throw new InvalidDataException($"Immutable baseline differs from extracted entry: {internalPath}");
                if (!File.Exists(output)) File.WriteAllBytes(output, bytes);
                entries.Add(new PakEntry(sequence, internalPath, pair.Entry.Offset, bytes.LongLength, pair.Entry.CompressedSize, pair.Entry.CompressionMethod.ToString(), pair.Entry.IsEncrypted, Identity.Sha256Bytes(bytes)));
            }
            return new PakInventory(PakPath, MountPoint, false, Identity.File(PakPath), unrealPakIdentity, editorIdentity, entries);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                if (!temporary.StartsWith(stateRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing unsafe cleanup");
                Directory.Delete(temporary, true);
            }
        }
    }
    private static string NormalizeAndValidate(string path) { var value = path.Replace('\\', '/').TrimStart('/'); if (value.Length == 0 || value.Split('/').Any(x => x is "" or "." or "..")) throw new InvalidDataException($"Unsafe pak path: {path}"); return value; }
    public void Dispose() => provider.Dispose();
}
