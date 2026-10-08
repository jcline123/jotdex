using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Integrations;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Integrations;

public sealed class IntegrationConfigService : IIntegrationConfigService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<IntegrationConfigService> _logger;
    private readonly object _gate = new();
    private IntegrationConfig? _cached;

    public IntegrationConfigService(IDataRootResolver dataRoot, ILogger<IntegrationConfigService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public IntegrationConfig Get()
    {
        lock (_gate)
        {
            if (_cached is not null) return Clone(_cached);
            _cached = Load() ?? new IntegrationConfig();
            return Clone(_cached);
        }
    }

    public void Save(IntegrationConfig config)
    {
        lock (_gate)
        {
            var path = ConfigPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var normalized = Normalize(config);
            var json = JsonSerializer.Serialize(normalized, JsonOpts);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, path, overwrite: true);
            _cached = normalized;
        }
    }

    public bool IsFeatureEnabled()
    {
        var c = Get();
        return c.Enabled;
    }

    private IntegrationConfig? Load()
    {
        var path = ConfigPath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return Normalize(JsonSerializer.Deserialize<IntegrationConfig>(json, JsonOpts) ?? new IntegrationConfig());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read integrations config; treating as disabled");
            return new IntegrationConfig { Enabled = false };
        }
    }

    private string ConfigPath() => Path.Combine(_dataRoot.ResolveDataRoot(), "config", "integrations.json");

    private static IntegrationConfig Normalize(IntegrationConfig c) => new()
    {
        Enabled = c.Enabled,
        ReadRequestsPerMinute = Math.Clamp(c.ReadRequestsPerMinute <= 0 ? 120 : c.ReadRequestsPerMinute, 1, 10_000),
        WriteRequestsPerMinute = Math.Clamp(c.WriteRequestsPerMinute <= 0 ? 20 : c.WriteRequestsPerMinute, 1, 10_000),
        MaxBodyBytes = Math.Clamp(c.MaxBodyBytes <= 0 ? 2 * 1024 * 1024 : c.MaxBodyBytes, 1024, 16 * 1024 * 1024),
        MaxPageSize = Math.Clamp(c.MaxPageSize <= 0 ? 100 : c.MaxPageSize, 1, 500),
        IdempotencyRetentionHours = Math.Clamp(c.IdempotencyRetentionHours <= 0 ? 24 : c.IdempotencyRetentionHours, 1, 168)
    };

    private static IntegrationConfig Clone(IntegrationConfig c) => Normalize(c);
}
