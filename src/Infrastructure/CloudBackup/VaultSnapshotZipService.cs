using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class VaultSnapshotZipService : IVaultSnapshotZipService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly CloudBackupHashService _hashes;
    private readonly IAppVersion _version;
    private readonly ILogger<VaultSnapshotZipService> _logger;

    public VaultSnapshotZipService(
        IDataRootResolver dataRoot,
        CloudBackupHashService hashes,
        IAppVersion version,
        ILogger<VaultSnapshotZipService> logger)
    {
        _dataRoot = dataRoot;
        _hashes = hashes;
        _version = version;
        _logger = logger;
    }

    public async Task<VaultSnapshotZipResult> CreateAsync(
        string vaultSnapshotPath,
        string stagingRoot,
        string outputDirectory,
        string runId,
        string backupSetId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var dataRoot = _dataRoot.ResolveDataRoot();
        if (!CloudBackupPaths.IsUnderStaging(dataRoot, vaultSnapshotPath) ||
            !CloudBackupPaths.IsUnderStaging(dataRoot, stagingRoot))
        {
            return Fail("Vault snapshot path must be under cloud-backup-staging.");
        }

        if (!Directory.Exists(vaultSnapshotPath))
            return Fail("Vault snapshot missing.");

        Directory.CreateDirectory(outputDirectory);
        var zipPath = Path.Combine(outputDirectory, fileName);
        var tmpPath = zipPath + ".partial";

        try
        {
            if (File.Exists(tmpPath)) File.Delete(tmpPath);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            var createdUtc = DateTimeOffset.UtcNow;
            var entryCount = 0;
            var noteCount = 0;
            long uncompressed = 0;

            await Task.Run(() =>
            {
                using (var zip = ZipFile.Open(tmpPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.EnumerateFiles(vaultSnapshotPath, "*", SearchOption.AllDirectories))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var full = Path.GetFullPath(file);
                        EnsureUnder(vaultSnapshotPath, full);
                        if (IsReparse(full))
                            throw new InvalidOperationException("Reparse point rejected in vault snapshot.");

                        var rel = Path.GetRelativePath(vaultSnapshotPath, full).Replace('\\', '/');
                        if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                            rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
                            continue;

                        zip.CreateEntryFromFile(full, "vault/" + rel, CompressionLevel.Optimal);
                        entryCount++;
                        uncompressed += new FileInfo(full).Length;
                        if (rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                            noteCount++;
                    }

                    var manifest = new
                    {
                        schemaVersion = 1,
                        kind = "jotdex-vault-only-backup",
                        backupSetId,
                        runId,
                        createdUtc,
                        jotdexVersion = _version.Version,
                        encrypted = false,
                        vaultRootEntry = "vault/",
                        noteCount,
                        fileCount = entryCount,
                        uncompressedBytes = uncompressed,
                        warning = "This archive is not encrypted and contains only the Jotdex vault."
                    };
                    WriteText(zip, "VAULT-MANIFEST.json", JsonSerializer.Serialize(manifest, JsonOpts));
                    WriteText(zip, "README-VAULT-BACKUP.txt", Readme);
                    entryCount += 2;
                }

                ValidateZipRoots(tmpPath);
            }, cancellationToken).ConfigureAwait(false);

            File.Move(tmpPath, zipPath, overwrite: true);
            var sha = _hashes.Sha256FileHex(zipPath);
            var size = new FileInfo(zipPath).Length;

            return new VaultSnapshotZipResult
            {
                Success = true,
                ZipPath = zipPath,
                SizeBytes = size,
                Sha256 = sha,
                EntryCount = entryCount,
                NoteCount = noteCount,
                CreatedUtc = createdUtc
            };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { /* ignore */ }
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
            _logger.LogWarning(ex, "Vault snapshot ZIP failed");
            return Fail(ex.Message);
        }
    }

    private static void ValidateZipRoots(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (string.IsNullOrEmpty(name) || name.EndsWith('/'))
                continue;
            if (name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(name))
                throw new InvalidDataException("ZIP contains unsafe path: " + name);

            var root = name.Split('/')[0];
            if (root is "vault" or "VAULT-MANIFEST.json" or "README-VAULT-BACKUP.txt")
                continue;
            if (name is "VAULT-MANIFEST.json" or "README-VAULT-BACKUP.txt")
                continue;
            throw new InvalidDataException("ZIP has disallowed root entry: " + name);
        }

        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains("VAULT-MANIFEST.json"))
            throw new InvalidDataException("Missing VAULT-MANIFEST.json");
        if (!names.Contains("README-VAULT-BACKUP.txt"))
            throw new InvalidDataException("Missing README-VAULT-BACKUP.txt");
    }

    private static void WriteText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        w.Write(content);
    }

    private static void EnsureUnder(string root, string candidate)
    {
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var c = Path.GetFullPath(candidate);
        if (!c.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(c, r, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes vault snapshot.");
    }

    private static bool IsReparse(string path)
    {
        try { return File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint); }
        catch { return false; }
    }

    private static VaultSnapshotZipResult Fail(string error) =>
        new() { Success = false, Error = error, CreatedUtc = DateTimeOffset.UtcNow };

    private const string Readme =
        """
        Jotdex readable vault backup
        ============================

        This ZIP contains a plain, unencrypted copy of your Jotdex vault.

        Your Markdown notes are inside the vault folder. Attachments and images are stored beside their related notes in .assets folders.

        You do not need Jotdex to read these files. Extract the ZIP and open the .md files with any Markdown editor or text editor.

        This archive does not contain your Jotdex password, Jotdex settings, note-history snapshots, notification credentials, cloud credentials, or application files.

        For a complete Jotdex recovery, use the matching encrypted .jotdexkit from the same backup generation.
        """;
}
