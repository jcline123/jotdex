using Jotdex.Core.CloudBackup;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupHealthService : ICloudBackupHealthService
{
    public CloudBackupHealth Calculate(CloudBackupSettings settings, CloudBackupRuntimeState state, bool running)
    {
        var enabled = settings.Providers.Where(p => p.Enabled).ToList();
        var staleHours = Math.Max(2 * settings.IntervalHours, 36);
        var now = DateTimeOffset.UtcNow;
        var items = new List<CloudProviderHealthItem>();

        foreach (var cfg in enabled)
        {
            var st = state.Providers.FirstOrDefault(p => p.Provider == cfg.Provider)
                     ?? new CloudProviderBackupStatus { Provider = cfg.Provider };

            var item = EvaluateProvider(cfg, st, settings.IncludePlainVaultZip, settings.IntervalHours, staleHours, now, running);
            items.Add(item);
        }

        CloudBackupHealthLevel aggregate;
        if (enabled.Count == 0)
            aggregate = CloudBackupHealthLevel.NotConfigured;
        else if (running)
            aggregate = CloudBackupHealthLevel.Running;
        else if (items.Any(i => i.Health == CloudBackupHealthLevel.Error))
            aggregate = CloudBackupHealthLevel.Error;
        else if (items.Any(i => i.Health is CloudBackupHealthLevel.Warning or CloudBackupHealthLevel.Pending))
            aggregate = CloudBackupHealthLevel.Warning;
        else
            aggregate = CloudBackupHealthLevel.Healthy;

        return new CloudBackupHealth
        {
            AggregateHealth = aggregate,
            EnabledProviderCount = enabled.Count,
            Running = running,
            Providers = items
        };
    }

    private static CloudProviderHealthItem EvaluateProvider(
        CloudProviderSettings cfg,
        CloudProviderBackupStatus st,
        bool includeVaultZip,
        int intervalHours,
        int staleHours,
        DateTimeOffset now,
        bool running)
    {
        if (running && (st.Health == CloudBackupHealthLevel.Running || st.LastAttemptUtc > now.AddMinutes(-30)))
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Running, st.ConnectionState, CloudBackupFailureCode.None,
                "Backup in progress.", st.MoveKit.LastVerifiedUtc ?? st.LastVerifiedUtc);
        }

        if (st.ConnectionState is CloudConnectionState.ReconnectRequired or CloudConnectionState.ConfigurationUnavailable)
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Error, st.ConnectionState,
                st.LastFailureCode == CloudBackupFailureCode.None
                    ? CloudBackupFailureCode.AuthenticationRequired
                    : st.LastFailureCode,
                st.LastFailureMessage ?? "Provider must be reconnected.",
                st.MoveKit.LastVerifiedUtc ?? st.LastVerifiedUtc);
        }

        if (st.ConnectionState == CloudConnectionState.NotConfigured ||
            st.ConnectionState == CloudConnectionState.Disconnected)
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Pending, st.ConnectionState, CloudBackupFailureCode.None,
                "Waiting for first connection/backup.", null);
        }

        var moveVerified = st.MoveKit.LastVerifiedUtc ?? st.LastVerifiedUtc;
        var moveFail = st.MoveKit.LastFailureCode;

        // Move Kit is critical
        if (moveFail is not CloudBackupFailureCode.None and not CloudBackupFailureCode.RetentionFailed
            && (moveVerified is null || st.MoveKit.LastAttemptUtc > moveVerified))
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Error, st.ConnectionState, moveFail,
                st.MoveKit.LastFailureMessage ?? st.LastFailureMessage ?? "Encrypted Move Kit backup failed.",
                moveVerified);
        }

        if (moveVerified is null)
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Pending, st.ConnectionState, CloudBackupFailureCode.None,
                "First encrypted Move Kit backup has not completed yet.", null);
        }

        var age = now - moveVerified.Value;
        if (age > TimeSpan.FromHours(staleHours))
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Error, st.ConnectionState,
                CloudBackupFailureCode.RemoteFileMissing,
                $"Encrypted Move Kit is stale (>{staleHours}h since last verified backup).",
                moveVerified);
        }

        // Partial: Move Kit OK, vault ZIP required but failed
        if (includeVaultZip)
        {
            var zipFail = st.VaultZip.LastFailureCode;
            var zipVerified = st.VaultZip.LastVerifiedUtc;
            if (zipFail is not CloudBackupFailureCode.None and not CloudBackupFailureCode.RetentionFailed
                && (zipVerified is null || st.VaultZip.LastAttemptUtc > zipVerified))
            {
                return Item(cfg.Provider, CloudBackupHealthLevel.Warning, st.ConnectionState, zipFail,
                    st.VaultZip.LastFailureMessage
                    ?? "Readable vault ZIP failed. The encrypted Move Kit is current.",
                    moveVerified);
            }

            if (zipVerified is null && st.VaultZip.LastAttemptUtc is not null)
            {
                return Item(cfg.Provider, CloudBackupHealthLevel.Warning, st.ConnectionState,
                    st.VaultZip.LastFailureCode == CloudBackupFailureCode.None
                        ? CloudBackupFailureCode.UploadFailed
                        : st.VaultZip.LastFailureCode,
                    "Readable vault ZIP has not been verified. The encrypted Move Kit is current.",
                    moveVerified);
            }
        }

        if (st.LastFailureCode == CloudBackupFailureCode.RetentionFailed)
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Warning, st.ConnectionState,
                CloudBackupFailureCode.RetentionFailed,
                st.LastFailureMessage ?? "Retention cleanup failed.",
                moveVerified);
        }

        if (age > TimeSpan.FromHours(intervalHours))
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Warning, st.ConnectionState, CloudBackupFailureCode.None,
                "Backup is overdue but still within the stale threshold.",
                moveVerified);
        }

        if (st.QuotaRemainingBytes is long rem && rem >= 0 && rem < 50L * 1024 * 1024)
        {
            return Item(cfg.Provider, CloudBackupHealthLevel.Warning, st.ConnectionState, CloudBackupFailureCode.None,
                "Cloud storage quota is running low.",
                moveVerified);
        }

        return Item(cfg.Provider, CloudBackupHealthLevel.Healthy, st.ConnectionState, CloudBackupFailureCode.None,
            null, moveVerified);
    }

    private static CloudProviderHealthItem Item(
        CloudProviderKind provider,
        CloudBackupHealthLevel health,
        CloudConnectionState connection,
        CloudBackupFailureCode code,
        string? message,
        DateTimeOffset? lastVerified) =>
        new()
        {
            Provider = provider,
            Health = health,
            ConnectionState = connection,
            FailureCode = code,
            Message = message,
            LastVerifiedUtc = lastVerified
        };
}
