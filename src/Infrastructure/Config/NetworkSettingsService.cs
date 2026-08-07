using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Config;

public sealed class NetworkSettings
{
    /// <summary>loopback (127.0.0.1 only) or lan (0.0.0.0).</summary>
    public string BindMode { get; set; } = "loopback";

    public int Port { get; set; } = 5180;

    /// <summary>Optional absolute path to a PFX for HTTPS (restart required).</summary>
    public string? HttpsPfxPath { get; set; }

    /// <summary>Optional PFX password. Prefer env JOTDEX_HTTPS_PFX_PASSWORD. Never return via API.</summary>
    public string? HttpsPfxPassword { get; set; }

    public string ListenHost =>
        string.Equals(BindMode, "lan", StringComparison.OrdinalIgnoreCase) ? "0.0.0.0" : "127.0.0.1";

    public bool IsLan => string.Equals(BindMode, "lan", StringComparison.OrdinalIgnoreCase);

    public bool HttpsEnabled => !string.IsNullOrWhiteSpace(HttpsPfxPath);

    public string ToHttpUrl() => $"http://{ListenHost}:{Port}";

    public string ToListenUrl() => HttpsEnabled ? $"https://{ListenHost}:{Port}" : ToHttpUrl();
}

public interface INetworkSettingsService
{
    NetworkSettings Get();
    /// <param name="updatePassword">When true, apply <see cref="NetworkSettings.HttpsPfxPassword"/> (null/empty clears).</param>
    (bool Success, string? Error, NetworkSettings? Settings) Save(NetworkSettings incoming, bool updatePassword = false);
}

public sealed class NetworkSettingsService : INetworkSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<NetworkSettingsService> _logger;
    private readonly object _gate = new();
    private NetworkSettings _current;

    public NetworkSettingsService(IDataRootResolver dataRoot, ILogger<NetworkSettingsService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
        _current = Load() ?? new NetworkSettings();
    }

    public NetworkSettings Get()
    {
        lock (_gate)
            return Clone(_current);
    }

    public (bool Success, string? Error, NetworkSettings? Settings) Save(NetworkSettings incoming, bool updatePassword = false)
    {
        if (incoming.Port is < 1 or > 65535)
            return (false, "Port must be between 1 and 65535.", null);

        var mode = (incoming.BindMode ?? "loopback").Trim().ToLowerInvariant();
        if (mode is not ("loopback" or "lan"))
            return (false, "BindMode must be 'loopback' or 'lan'.", null);

        if (!string.IsNullOrWhiteSpace(incoming.HttpsPfxPath) && !File.Exists(incoming.HttpsPfxPath))
            return (false, "HTTPS certificate file not found.", null);

        lock (_gate)
        {
            var next = new NetworkSettings
            {
                BindMode = mode,
                Port = incoming.Port,
                HttpsPfxPath = string.IsNullOrWhiteSpace(incoming.HttpsPfxPath) ? null : incoming.HttpsPfxPath.Trim(),
                HttpsPfxPassword = updatePassword
                    ? (string.IsNullOrEmpty(incoming.HttpsPfxPassword) ? null : incoming.HttpsPfxPassword)
                    : _current.HttpsPfxPassword
            };

            Persist(next);
            _current = next;

            if (next.IsLan && !next.HttpsEnabled)
                _logger.LogWarning("LAN binding enabled without HTTPS — credentials may travel in cleartext; prefer a PFX or VPN");

            return (true, null, Clone(next));
        }
    }

    private NetworkSettings? Load()
    {
        var path = StorePath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<NetworkSettings>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read network settings");
            return null;
        }
    }

    private void Persist(NetworkSettings settings)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "network.json");

    private static NetworkSettings Clone(NetworkSettings s) => new()
    {
        BindMode = s.BindMode,
        Port = s.Port,
        HttpsPfxPath = s.HttpsPfxPath,
        HttpsPfxPassword = s.HttpsPfxPassword
    };
}
