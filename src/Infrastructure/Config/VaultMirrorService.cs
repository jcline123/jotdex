using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Config;

public sealed class VaultMirrorSettings
{
    public bool Enabled { get; set; }
    public string DestinationPath { get; set; } = "";
    /// <summary>Minutes between automatic mirrors. Clamped 5–1440.</summary>
    public int IntervalMinutes { get; set; } = 15;
    /// <summary>Persisted last successful mirror (for stale alerts across restarts).</summary>
    public DateTimeOffset? LastSucceededUtc { get; set; }
    public string? LastError { get; set; }
    /// <summary>Once per day, place an encrypted (or plain) move-kit ZIP in the mirror folder.</summary>
    public bool IncludeDailyMoveKit { get; set; }
    public DateTimeOffset? LastMoveKitUtc { get; set; }
}

public sealed class VaultMirrorStatus
{
    public bool Enabled { get; init; }
    public string? DestinationPath { get; init; }
    public int IntervalMinutes { get; init; }
    public DateTimeOffset? LastStartedUtc { get; init; }
    public DateTimeOffset? LastSucceededUtc { get; init; }
    public string? LastError { get; init; }
    public bool Running { get; init; }
    public string? Hint { get; init; }
    public bool IncludeDailyMoveKit { get; init; }
    public DateTimeOffset? LastMoveKitUtc { get; init; }
    /// <summary>Stable zip name written into the destination folder.</summary>
    public string? ZipFileName { get; init; }
}

public interface IVaultMirrorService
{
    VaultMirrorSettings GetSettings();
    (bool Success, string? Error, VaultMirrorSettings? Settings) SaveSettings(VaultMirrorSettings incoming);
    VaultMirrorStatus GetStatus();
    Task<(bool Success, string? Error)> RunNowAsync(CancellationToken ct = default);
    void RecordMoveKitPublished(DateTimeOffset utc);
}

public sealed class VaultMirrorService : IVaultMirrorService
{
    /// <summary>Single archive name so sync clients (iCloud, etc.) only handle one large file.</summary>
    public const string MirrorZipFileName = "jotdex-vault-mirror.zip";
    public const string MoveKitsFolderName = "jotdex-move-kits";
    public const string MirrorReadmeFileName = "README-JOTDEX-MIRROR.txt";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<VaultMirrorService> _logger;
    private readonly object _gate = new();
    private VaultMirrorSettings _settings;
    private DateTimeOffset? _lastStarted;
    private DateTimeOffset? _lastSucceeded;
    private string? _lastError;
    private bool _running;

    public VaultMirrorService(IDataRootResolver dataRoot, IVaultPathGuard paths, ILogger<VaultMirrorService> logger)
    {
        _dataRoot = dataRoot;
        _paths = paths;
        _logger = logger;
        _settings = Load() ?? new VaultMirrorSettings();
        _lastSucceeded = _settings.LastSucceededUtc;
        _lastError = _settings.LastError;
    }

    public VaultMirrorSettings GetSettings()
    {
        lock (_gate) return Clone(_settings);
    }

    public VaultMirrorStatus GetStatus()
    {
        lock (_gate)
        {
            return new VaultMirrorStatus
            {
                Enabled = _settings.Enabled,
                DestinationPath = string.IsNullOrWhiteSpace(_settings.DestinationPath) ? null : _settings.DestinationPath,
                IntervalMinutes = _settings.IntervalMinutes,
                LastStartedUtc = _lastStarted,
                LastSucceededUtc = _lastSucceeded,
                LastError = _lastError,
                Running = _running,
                IncludeDailyMoveKit = _settings.IncludeDailyMoveKit,
                LastMoveKitUtc = _settings.LastMoveKitUtc,
                ZipFileName = MirrorZipFileName,
                Hint =
                    $"One-way zip: live vault → {MirrorZipFileName} in the destination. " +
                    "Never point VaultPath at the mirror. Sync clients (iCloud) handle one archive better than thousands of files."
            };
        }
    }

    public (bool Success, string? Error, VaultMirrorSettings? Settings) SaveSettings(VaultMirrorSettings incoming)
    {
        var dest = (incoming.DestinationPath ?? "").Trim();
        var interval = Math.Clamp(incoming.IntervalMinutes <= 0 ? 15 : incoming.IntervalMinutes, 5, 24 * 60);

        if (incoming.Enabled)
        {
            if (string.IsNullOrWhiteSpace(dest))
                return (false, "Destination path is required when mirror is enabled.", null);
            if (!Path.IsPathRooted(dest))
                return (false, "Destination must be an absolute path.", null);

            var err = ValidateDestination(dest);
            if (err is not null) return (false, err, null);
        }

        var next = new VaultMirrorSettings
        {
            Enabled = incoming.Enabled,
            DestinationPath = dest,
            IntervalMinutes = interval,
            IncludeDailyMoveKit = incoming.IncludeDailyMoveKit,
            LastSucceededUtc = _settings.LastSucceededUtc,
            LastError = _settings.LastError,
            LastMoveKitUtc = _settings.LastMoveKitUtc
        };

        lock (_gate)
        {
            Persist(next);
            _settings = next;
        }

        return (true, null, Clone(next));
    }

    public async Task<(bool Success, string? Error)> RunNowAsync(CancellationToken ct = default)
    {
        string dest;
        lock (_gate)
        {
            if (_running)
                return (false, "Mirror already running.");
            if (!_paths.IsConfigured)
                return (false, "Vault not configured.");
            dest = (_settings.DestinationPath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(dest))
                return (false, "Mirror destination not set.");
            var err = ValidateDestination(dest);
            if (err is not null) return (false, err);
            _running = true;
            _lastStarted = DateTimeOffset.UtcNow;
            _lastError = null;
        }

        try
        {
            Directory.CreateDirectory(dest);
            var source = _paths.VaultRoot;
            // Hard cap so a stuck sync destination cannot block the schedule forever.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(12));
            var result = await Task.Run(
                    () => CreateVaultZipMirror(source, dest, timeoutCts.Token),
                    timeoutCts.Token)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (result.Success)
                {
                    _lastSucceeded = DateTimeOffset.UtcNow;
                    _lastError = null;
                    _settings.LastSucceededUtc = _lastSucceeded;
                    _settings.LastError = null;
                    Persist(_settings);
                }
                else
                {
                    _lastError = result.Error;
                    _settings.LastError = result.Error;
                    Persist(_settings);
                }
            }

            if (result.Success)
                _logger.LogInformation("Vault mirror zip succeeded → {Dest}\\{Zip}", dest, MirrorZipFileName);
            else
                _logger.LogWarning("Vault mirror failed: {Error}", result.Error);

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            const string msg = "Mirror timed out after 12 minutes (often slow sync destination). Will retry on the next interval.";
            lock (_gate) _lastError = msg;
            _logger.LogWarning("{Msg}", msg);
            return (false, msg);
        }
        catch (Exception ex)
        {
            lock (_gate) _lastError = ex.Message;
            _logger.LogWarning(ex, "Vault mirror failed");
            return (false, ex.Message);
        }
        finally
        {
            lock (_gate) _running = false;
        }
    }

    private string? ValidateDestination(string dest)
    {
        try
        {
            var full = Path.GetFullPath(dest);
            if (_paths.IsConfigured)
            {
                var vault = Path.GetFullPath(_paths.VaultRoot).TrimEnd('\\', '/');
                var destTrim = full.TrimEnd('\\', '/');
                if (string.Equals(vault, destTrim, StringComparison.OrdinalIgnoreCase))
                    return "Destination cannot be the live vault folder.";
                if (destTrim.StartsWith(vault + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    destTrim.StartsWith(vault + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return "Destination cannot be inside the live vault.";
                if (vault.StartsWith(destTrim + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    vault.StartsWith(destTrim + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return "Destination cannot be a parent of the live vault.";
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Zip the live vault into destination\<see cref="MirrorZipFileName"/>, then remove legacy
    /// file-tree mirror contents (preserves move-kits folder and the zip itself). Never writes the live vault.
    /// </summary>
    private (bool Success, string? Error) CreateVaultZipMirror(string source, string dest, CancellationToken ct)
    {
        var sourceRoot = Path.GetFullPath(source).TrimEnd('\\', '/');
        var destRoot = Path.GetFullPath(dest).TrimEnd('\\', '/');
        if (string.Equals(sourceRoot, destRoot, StringComparison.OrdinalIgnoreCase))
            return (false, "Destination cannot be the live vault folder.");

        var zipPath = Path.Combine(destRoot, MirrorZipFileName);
        var tempZip = Path.Combine(destRoot, MirrorZipFileName + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var skipped = 0;
        var added = 0;

        try
        {
            if (File.Exists(tempZip))
            {
                try { File.Delete(tempZip); } catch { /* ignore */ }
            }

            using (var archive = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    string rel;
                    try
                    {
                        rel = Path.GetRelativePath(sourceRoot, file);
                    }
                    catch
                    {
                        skipped++;
                        continue;
                    }

                    if (ShouldSkipVaultRelativePath(rel))
                        continue;

                    var entryName = rel.Replace('\\', '/');
                    try
                    {
                        // Fastest keeps scheduled mirrors snappy; sync clients care about file count more than size.
                        archive.CreateEntryFromFile(file, entryName, CompressionLevel.Fastest);
                        added++;
                    }
                    catch (IOException)
                    {
                        skipped++;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        skipped++;
                    }
                }
            }

            if (added == 0)
            {
                try { File.Delete(tempZip); } catch { /* ignore */ }
                return (false, "No files could be read from the live vault to build the mirror zip.");
            }

            // Atomic replace of the destination zip (does not touch the live vault).
            File.Move(tempZip, zipPath, overwrite: true);

            WriteMirrorReadme(destRoot);

            // Remove old robocopy-style tree so iCloud only has the zip (+ optional move kits).
            var cleanupError = CleanupLegacyMirrorContents(destRoot, ct);
            if (cleanupError is not null)
                _logger.LogWarning("Mirror zip written but legacy cleanup incomplete: {Error}", cleanupError);

            if (skipped > 0)
                _logger.LogInformation("Vault mirror zip included {Added} files; skipped {Skipped} locked/unreadable", added, skipped);

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* ignore */ }
            throw;
        }
        catch (Exception ex)
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* ignore */ }
            return (false, ex.Message);
        }
    }

    private static bool ShouldSkipVaultRelativePath(string relativePath)
    {
        var rel = relativePath.Replace('/', Path.DirectorySeparatorChar);
        if (rel.Equals(".git", StringComparison.OrdinalIgnoreCase)) return true;
        if (rel.StartsWith(".git" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return true;
        var name = Path.GetFileName(rel);
        if (name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static void WriteMirrorReadme(string destRoot)
    {
        var path = Path.Combine(destRoot, MirrorReadmeFileName);
        var text =
            """
            Jotdex vault mirror (read-only zip)
            ==================================

            This folder holds a one-way copy of the live vault as a single archive:

              jotdex-vault-mirror.zip

            Why a zip? Sync clients on Windows (especially iCloud) struggle with thousands of
            small Markdown/asset files. One archive syncs more reliably.

            Rules
            -----
            - Do NOT open this folder (or unzip into it) as the live Jotdex vault.
            - Keep VaultPath on local disk only.
            - Optional jotdex-move-kits\ is a separate daily recovery kit, not the live vault.
            - To browse notes offline, unzip jotdex-vault-mirror.zip somewhere else.

            """;
        try
        {
            File.WriteAllText(path, text);
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// Delete everything in the mirror destination except the zip, readme, and move-kits folder.
    /// Never called on the live vault path (validated before mirror runs).
    /// </summary>
    private static string? CleanupLegacyMirrorContents(string destRoot, CancellationToken ct)
    {
        try
        {
            destRoot = Path.GetFullPath(destRoot).TrimEnd('\\', '/');
            foreach (var entry in Directory.EnumerateFileSystemEntries(destRoot))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (name.Equals(MirrorZipFileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals(MirrorReadmeFileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Equals(MoveKitsFolderName, StringComparison.OrdinalIgnoreCase)) continue;
                // Ignore in-progress temp zips from a crashed prior run briefly — delete them too if .tmp
                try
                {
                    ClearReadOnlyBestEffort(entry);
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else if (File.Exists(entry))
                        File.Delete(entry);
                }
                catch (Exception ex)
                {
                    return $"Could not remove legacy mirror item '{name}': {ex.Message}";
                }
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void ClearReadOnlyBestEffort(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            if (Directory.Exists(path))
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true
                }))
                {
                    try
                    {
                        var a = File.GetAttributes(child);
                        if ((a & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(child, a & ~FileAttributes.ReadOnly);
                    }
                    catch
                    {
                        /* skip */
                    }
                }
            }
        }
        catch
        {
            /* best-effort for iCloud placeholders */
        }
    }

    private VaultMirrorSettings? Load()
    {
        var path = StorePath();
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<VaultMirrorSettings>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read vault mirror settings");
            return null;
        }
    }

    private void Persist(VaultMirrorSettings settings)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "vault-mirror.json");

    private static VaultMirrorSettings Clone(VaultMirrorSettings s) => new()
    {
        Enabled = s.Enabled,
        DestinationPath = s.DestinationPath,
        IntervalMinutes = s.IntervalMinutes,
        LastSucceededUtc = s.LastSucceededUtc,
        LastError = s.LastError,
        IncludeDailyMoveKit = s.IncludeDailyMoveKit,
        LastMoveKitUtc = s.LastMoveKitUtc
    };

    public void RecordMoveKitPublished(DateTimeOffset utc)
    {
        lock (_gate)
        {
            _settings.LastMoveKitUtc = utc;
            Persist(_settings);
        }
    }
}

public sealed class VaultMirrorHostedService : BackgroundService
{
    private readonly IVaultMirrorService _mirror;
    private readonly ILogger<VaultMirrorHostedService> _logger;

    public VaultMirrorHostedService(IVaultMirrorService mirror, ILogger<VaultMirrorHostedService> logger)
    {
        _mirror = mirror;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = _mirror.GetSettings();
            var delay = TimeSpan.FromMinutes(Math.Clamp(settings.IntervalMinutes, 5, 24 * 60));

            if (settings.Enabled && !string.IsNullOrWhiteSpace(settings.DestinationPath))
            {
                var status = _mirror.GetStatus();
                if (status.Running)
                {
                    _logger.LogInformation("Skipping scheduled mirror — previous run still in progress");
                }
                else
                {
                    try
                    {
                        await _mirror.RunNowAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Scheduled vault mirror failed");
                    }
                }
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
