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
}

public interface IVaultMirrorService
{
    VaultMirrorSettings GetSettings();
    (bool Success, string? Error, VaultMirrorSettings? Settings) SaveSettings(VaultMirrorSettings incoming);
    VaultMirrorStatus GetStatus();
    Task<(bool Success, string? Error)> RunNowAsync(CancellationToken ct = default);
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
            IntervalMinutes = interval
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
            var result = await Task.Run(() => RunRobocopy(source, dest), ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (result.Success)
                {
                    _lastSucceeded = DateTimeOffset.UtcNow;
                    _lastError = null;
                }
                else
                {
                    _lastError = result.Error;
                }
            }

            if (result.Success)
                _logger.LogInformation("Vault mirror succeeded → {Dest}", dest);
            else
                _logger.LogWarning("Vault mirror failed: {Error}", result.Error);

            return result;
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

    private static (bool Success, string? Error) RunRobocopy(string source, string dest)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "robocopy.exe",
            ArgumentList =
            {
                source,
                dest,
                "/MIR",
                "/R:2",
                "/W:2",
                "/NFL",
                "/NDL",
                "/NJH",
                "/NJS",
                "/NP",
                "/XD", ".git"
            },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start robocopy.");
        proc.WaitForExit();
        // robocopy: 0–7 = success/partial copy; >=8 = failure
        if (proc.ExitCode >= 8)
            return (false, $"robocopy failed with exit code {proc.ExitCode}");
        return (true, null);
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
        IntervalMinutes = s.IntervalMinutes
    };
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
                try
                {
                    await _mirror.RunNowAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Scheduled vault mirror failed");
                }
            }

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
