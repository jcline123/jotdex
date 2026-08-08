using Jotdex.Core.Notifications;
using Jotdex.Infrastructure.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Notifications;

/// <summary>Hourly: if mirror is enabled and last success is older than N hours, send ops alert (deduped).</summary>
public sealed class MirrorStaleAlertHostedService : BackgroundService
{
    private readonly IVaultMirrorService _mirror;
    private readonly INotificationSettingsService _notifySettings;
    private readonly IOpsAlertSender _sender;
    private readonly IMirrorAlertState _alertState;
    private readonly ILogger<MirrorStaleAlertHostedService> _logger;

    public MirrorStaleAlertHostedService(
        IVaultMirrorService mirror,
        INotificationSettingsService notifySettings,
        IOpsAlertSender sender,
        IMirrorAlertState alertState,
        ILogger<MirrorStaleAlertHostedService> logger)
    {
        _mirror = mirror;
        _notifySettings = notifySettings;
        _sender = sender;
        _alertState = alertState;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Mirror stale alert check failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task CheckOnceAsync(CancellationToken ct)
    {
        var cfg = _notifySettings.GetSettings();
        if (!cfg.Alerts.MirrorStaleEnabled)
            return;

        var status = _notifySettings.GetStatus();
        if (!status.SmtpConfigured && !status.TelegramConfigured)
            return;
        if (status.SmtpConfigured && !status.SmtpPasswordSet && string.IsNullOrWhiteSpace(cfg.Smtp.Username) == false)
        {
            // password may be empty for open relays; still allow
        }

        var mirror = _mirror.GetStatus();
        if (!mirror.Enabled)
            return;

        var hours = Math.Clamp(cfg.Alerts.MirrorStaleHours, 1, 24 * 30);
        var threshold = DateTimeOffset.UtcNow.AddHours(-hours);
        var lastOk = mirror.LastSucceededUtc;

        if (lastOk is not null && lastOk > threshold)
            return;

        // Dedupe: one alert per stale window (don't re-alert within the same stale period unless hours elapsed since last alert)
        var lastAlert = _alertState.LastMirrorAlertUtc;
        if (lastAlert is not null && lastAlert > threshold)
            return;

        var age = lastOk is null
            ? "never succeeded (or not since this install started tracking)"
            : $"last succeeded {lastOk:u}";
        var subject = "Jotdex: vault mirror is stale";
        var body =
            $"Cloud backup mirror has not succeeded for at least {hours} hours.\n" +
            $"Status: {age}.\n" +
            (string.IsNullOrWhiteSpace(mirror.LastError) ? "" : $"Last error: {mirror.LastError}\n") +
            $"Destination: {mirror.DestinationPath ?? "(unset)"}\n";

        var (ok, err) = await _sender.SendAsync(subject, body, ct).ConfigureAwait(false);
        _alertState.RecordAlert(DateTimeOffset.UtcNow, ok ? null : err);
        if (ok)
            _logger.LogInformation("Sent mirror-stale ops alert");
        else
            _logger.LogWarning("Mirror-stale alert send failed: {Error}", err);
    }
}
