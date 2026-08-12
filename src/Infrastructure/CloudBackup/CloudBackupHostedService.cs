using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.Maintenance;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupHostedService : BackgroundService
{
    private readonly ICloudBackupSettingsService _settings;
    private readonly ICloudBackupCoordinator _coordinator;
    private readonly ICloudBackupSnapshotService _snapshot;
    private readonly IMoveKitCryptoService _crypto;
    private readonly ILogger<CloudBackupHostedService> _logger;

    public CloudBackupHostedService(
        ICloudBackupSettingsService settings,
        ICloudBackupCoordinator coordinator,
        ICloudBackupSnapshotService snapshot,
        IMoveKitCryptoService crypto,
        ILogger<CloudBackupHostedService> logger)
    {
        _settings = settings;
        _coordinator = coordinator;
        _snapshot = snapshot;
        _crypto = crypto;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            _snapshot.CleanOrphanedStaging(TimeSpan.FromDays(7));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cloud backup staging cleanup failed");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Cloud backup schedule tick failed");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task EvaluateAsync(CancellationToken ct)
    {
        var settings = _settings.Get();
        var enabled = settings.Providers.Where(p => p.Enabled).ToList();
        if (enabled.Count == 0)
            return;

        if (!_crypto.IsPasswordProtectionEnabled || !_crypto.HasEncryptionKey)
        {
            _logger.LogDebug("Cloud backup skipped: encryption not ready");
            return;
        }

        var summary = _coordinator.GetSummary();
        if (summary.ActiveOperation?.Running == true)
            return;

        var health = summary.Health;
        var due = false;

        // Never completed → catch-up
        if (health.Providers.Any(p =>
                p.Health is CloudBackupHealthLevel.Pending or CloudBackupHealthLevel.Error
                && p.LastVerifiedUtc is null))
            due = true;

        var state = summary.State;
        foreach (var cfg in enabled)
        {
            var pst = state.Providers.FirstOrDefault(p => p.Provider == cfg.Provider);
            var last = pst?.MoveKit.LastVerifiedUtc ?? pst?.LastVerifiedUtc;
            if (last is null)
            {
                due = true;
                break;
            }

            if (DateTimeOffset.UtcNow - last.Value >= TimeSpan.FromHours(settings.IntervalHours))
            {
                due = true;
                break;
            }

            if (pst?.NextDueUtc is DateTimeOffset next && next <= DateTimeOffset.UtcNow)
            {
                due = true;
                break;
            }
        }

        if (!due) return;

        _logger.LogInformation("Starting scheduled/catch-up cloud backup");
        await _coordinator.StartRunAsync(CloudBackupRunTrigger.StartupCatchUp, provider: null, ct)
            .ConfigureAwait(false);
    }
}
