using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.CloudBackup;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupSettingsService : ICloudBackupSettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<CloudBackupSettingsService> _logger;
    private readonly object _gate = new();
    private CloudBackupSettings _current;

    public CloudBackupSettingsService(IDataRootResolver dataRoot, ILogger<CloudBackupSettingsService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
        _current = Normalize(Load() ?? new CloudBackupSettings());
        Persist(_current);
    }

    public CloudBackupSettings Get()
    {
        lock (_gate) return Clone(_current);
    }

    public CloudBackupSettings Save(CloudBackupSettings incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var next = Normalize(incoming);
        lock (_gate)
        {
            // Preserve BackupSetId once minted
            if (!string.IsNullOrWhiteSpace(_current.BackupSetId))
                next.BackupSetId = _current.BackupSetId;
            Persist(next);
            _current = next;
            return Clone(_current);
        }
    }

    private CloudBackupSettings Normalize(CloudBackupSettings s)
    {
        var providers = s.Providers ?? [];
        EnsureProvider(providers, CloudProviderKind.OneDrive);
        EnsureProvider(providers, CloudProviderKind.GoogleDrive);
        EnsureProvider(providers, CloudProviderKind.Dropbox);

        var id = string.IsNullOrWhiteSpace(s.BackupSetId)
            ? Guid.NewGuid().ToString("D")
            : s.BackupSetId.Trim();

        return new CloudBackupSettings
        {
            SchemaVersion = s.SchemaVersion <= 0 ? 1 : s.SchemaVersion,
            BackupSetId = id,
            BackupSetName = (s.BackupSetName ?? "").Trim(),
            IntervalHours = Math.Clamp(s.IntervalHours <= 0 ? 24 : s.IntervalHours, 1, 168),
            VersionsToKeep = Math.Clamp(s.VersionsToKeep <= 0 ? 3 : s.VersionsToKeep, 2, 30),
            FullVerificationIntervalDays = Math.Clamp(
                s.FullVerificationIntervalDays <= 0 ? 30 : s.FullVerificationIntervalDays, 1, 365),
            IncludePlainVaultZip = s.IncludePlainVaultZip,
            Providers = providers
                .GroupBy(p => p.Provider)
                .Select(g => g.Last())
                .OrderBy(p => (int)p.Provider)
                .ToList()
        };
    }

    private static void EnsureProvider(List<CloudProviderSettings> list, CloudProviderKind kind)
    {
        if (list.Any(p => p.Provider == kind)) return;
        list.Add(new CloudProviderSettings { Provider = kind, Enabled = false });
    }

    private CloudBackupSettings? Load()
    {
        var path = StorePath();
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<CloudBackupSettings>(json, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cloud-backup settings; recreating defaults");
            return null;
        }
    }

    private void Persist(CloudBackupSettings settings)
    {
        var path = StorePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string StorePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "cloud-backup.json");

    private static CloudBackupSettings Clone(CloudBackupSettings s) =>
        new()
        {
            SchemaVersion = s.SchemaVersion,
            BackupSetId = s.BackupSetId,
            BackupSetName = s.BackupSetName,
            IntervalHours = s.IntervalHours,
            VersionsToKeep = s.VersionsToKeep,
            FullVerificationIntervalDays = s.FullVerificationIntervalDays,
            IncludePlainVaultZip = s.IncludePlainVaultZip,
            Providers = s.Providers.Select(p => new CloudProviderSettings
            {
                Provider = p.Provider,
                Enabled = p.Enabled,
                AccountId = p.AccountId,
                AccountDisplayName = p.AccountDisplayName,
                AccountEmail = p.AccountEmail,
                RemoteRootId = p.RemoteRootId,
                RemoteRootDisplayPath = p.RemoteRootDisplayPath,
                OAuthClientId = p.OAuthClientId,
                OAuthClientSecret = p.OAuthClientSecret,
                OAuthRedirectUri = p.OAuthRedirectUri
            }).ToList()
        };
}
