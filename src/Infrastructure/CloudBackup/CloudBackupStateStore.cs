using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupStateStore : ICloudBackupStateStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<CloudBackupStateStore> _logger;
    private readonly object _gate = new();
    private CloudBackupRuntimeState _current;

    public CloudBackupStateStore(IDataRootResolver dataRoot, ILogger<CloudBackupStateStore> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
        _current = Load() ?? new CloudBackupRuntimeState();
        EnsureProviders(_current);
    }

    public CloudBackupRuntimeState Get()
    {
        lock (_gate) return Clone(_current);
    }

    public void Save(CloudBackupRuntimeState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureProviders(state);
        lock (_gate)
        {
            Persist(state);
            _current = Clone(state);
        }
    }

    private static void EnsureProviders(CloudBackupRuntimeState state)
    {
        state.Providers ??= [];
        foreach (CloudProviderKind kind in Enum.GetValues<CloudProviderKind>())
        {
            if (state.Providers.Any(p => p.Provider == kind)) continue;
            state.Providers.Add(new CloudProviderBackupStatus { Provider = kind });
        }
    }

    private CloudBackupRuntimeState? Load()
    {
        var path = StorePath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CloudBackupRuntimeState>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cloud-backup status; starting fresh");
            return null;
        }
    }

    private void Persist(CloudBackupRuntimeState state)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "state", "cloud-backup", "status.json");

    private static CloudBackupRuntimeState Clone(CloudBackupRuntimeState s) =>
        JsonSerializer.Deserialize<CloudBackupRuntimeState>(
            JsonSerializer.Serialize(s, JsonOpts), JsonOpts) ?? new CloudBackupRuntimeState();
}
