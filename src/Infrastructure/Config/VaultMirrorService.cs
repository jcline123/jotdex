using System.Diagnostics;
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
                Hint = "One-way copy: live vault (local) → destination. Never point VaultPath at the mirror."
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
            // Hard cap so a stuck iCloud/robocopy cannot block the 15‑minute schedule forever.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromMinutes(12));
            var result = await Task.Run(() => RunRobocopy(source, dest, timeoutCts.Token), timeoutCts.Token)
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
                _logger.LogInformation("Vault mirror succeeded → {Dest}", dest);
            else
                _logger.LogWarning("Vault mirror failed: {Error}", result.Error);

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            const string msg = "Mirror timed out after 12 minutes (often iCloud lag). Will retry on the next interval.";
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

            // Soft warning only — cloud paths are expected for mirrors
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static (bool Success, string? Error) RunRobocopy(string source, string dest, CancellationToken ct) =>
        RunRobocopyCore(source, dest, ct, retryOnAccessDenied: true);

    private static (bool Success, string? Error) RunRobocopyCore(string source, string dest, CancellationToken ct, bool retryOnAccessDenied)
    {
        // Clear ReadOnly on dest with a short budget — full unbounded walks hang on iCloud.
        // /A-:R only applies after a successful copy, so existing ReadOnly dest files still fail.
        try
        {
            ClearReadOnlyAttributes(dest, TimeSpan.FromSeconds(45), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            /* best-effort */
        }

        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            ArgumentList =
            {
                source,
                dest,
                "/MIR",
                "/COPY:DT",
                "/DCOPY:T",
                "/A-:R",
                "/R:2",
                "/W:2",
                "/NP",
                "/NFL",
                "/NDL",
                "/XD", ".git"
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start robocopy.");
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        try
        {
            while (!proc.WaitForExit(500))
            {
                ct.ThrowIfCancellationRequested();
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!proc.HasExited) proc.Kill(entireProcessTree: true);
            }
            catch
            {
                /* ignore */
            }

            throw;
        }

        string stdout;
        string stderr;
        try
        {
            stdout = stdoutTask.GetAwaiter().GetResult();
            stderr = stderrTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            stdout = "";
            stderr = "";
        }

        if (proc.ExitCode < 8)
            return (true, null);

        if (retryOnAccessDenied)
        {
            var denied = ExtractAccessDeniedSourcePaths(stdout + "\n" + stderr);
            if (denied.Count > 0)
            {
                var destRoot = Path.GetFullPath(dest).TrimEnd('\\', '/');
                foreach (var srcPath in denied)
                {
                    try
                    {
                        var rel = Path.GetRelativePath(source, srcPath);
                        if (rel.StartsWith("..", StringComparison.Ordinal)) continue;
                        var destPath = Path.GetFullPath(Path.Combine(dest, rel));
                        if (!destPath.StartsWith(destRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(destPath, destRoot, StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (File.Exists(destPath))
                            File.Delete(destPath);
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                ct.ThrowIfCancellationRequested();
                return RunRobocopyCore(source, dest, ct, retryOnAccessDenied: false);
            }
        }

        var detail = SummarizeRobocopyOutput(stdout, stderr);
        var msg = $"robocopy failed with exit code {proc.ExitCode}";
        if (!string.IsNullOrWhiteSpace(detail))
            msg += ": " + detail;
        return (false, msg);
    }

    private static List<string> ExtractAccessDeniedSourcePaths(string output)
    {
        var paths = new List<string>();
        foreach (var line in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.Contains("ERROR 5", StringComparison.OrdinalIgnoreCase) &&
                !line.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
                continue;
            // Robocopy: "ERROR 5 (0x00000005) Copying File C:\path\file.png"
            const string marker = "Copying File ";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var path = line[(idx + marker.Length)..].Trim();
            if (path.Length > 0 && Path.IsPathRooted(path))
                paths.Add(path);
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToList();
    }

    private static void ClearReadOnlyAttributes(string root, TimeSpan budget, CancellationToken ct)
    {
        if (!Directory.Exists(root)) return;
        var sw = Stopwatch.StartNew();
        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        };

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(root, "*", opts);
        }
        catch
        {
            return;
        }

        foreach (var path in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (sw.Elapsed >= budget) return;
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
            }
            catch
            {
                /* skip locked/cloud-only entries */
            }
        }
    }

    private static string SummarizeRobocopyOutput(string stdout, string stderr)
    {
        var combined = (stdout ?? "") + "\n" + (stderr ?? "");
        var lines = combined
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l =>
                l.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                l.Contains("RETRY LIMIT", StringComparison.OrdinalIgnoreCase))
            .Take(6)
            .ToList();
        if (lines.Count == 0)
        {
            lines = combined
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(8)
                .ToList();
        }

        var text = string.Join(" | ", lines);
        const int max = 900;
        return text.Length <= max ? text : text[..max] + "…";
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
        // Stagger first run so startup isn't blocked
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
