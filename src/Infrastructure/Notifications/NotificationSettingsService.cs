using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Jotdex.Core.Notifications;
using Jotdex.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Notifications;

public sealed class NotificationSettingsService : INotificationSettingsService, IMirrorAlertState
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ISecretStore _secrets;
    private readonly ILogger<NotificationSettingsService> _logger;
    private readonly object _gate = new();
    private NotificationSettings _settings;
    private DateTimeOffset? _lastMirrorAlertUtc;
    private string? _lastAlertError;

    public NotificationSettingsService(
        IDataRootResolver dataRoot,
        ISecretStore secrets,
        ILogger<NotificationSettingsService> logger)
    {
        _dataRoot = dataRoot;
        _secrets = secrets;
        _logger = logger;
        _settings = Load() ?? new NotificationSettings();
        LoadAlertState();
    }

    public DateTimeOffset? LastMirrorAlertUtc
    {
        get { lock (_gate) return _lastMirrorAlertUtc; }
    }

    public string? LastAlertError
    {
        get { lock (_gate) return _lastAlertError; }
    }

    public void RecordAlert(DateTimeOffset utc, string? error)
    {
        lock (_gate)
        {
            _lastMirrorAlertUtc = utc;
            _lastAlertError = error;
            PersistAlertState();
        }
    }

    public NotificationSettings GetSettings()
    {
        lock (_gate) return Clone(_settings);
    }

    public NotificationStatus GetStatus()
    {
        lock (_gate)
        {
            var smtp = _settings.Smtp;
            var tg = _settings.Telegram;
            return new NotificationStatus
            {
                SmtpConfigured = smtp.Enabled &&
                                 !string.IsNullOrWhiteSpace(smtp.Host) &&
                                 !string.IsNullOrWhiteSpace(smtp.ToAddress) &&
                                 !string.IsNullOrWhiteSpace(smtp.FromAddress),
                SmtpPasswordSet = _secrets.Has(SecretKeys.SmtpPassword),
                TelegramConfigured = tg.Enabled && !string.IsNullOrWhiteSpace(tg.ChatId),
                TelegramTokenSet = _secrets.Has(SecretKeys.TelegramBotToken),
                LastMirrorAlertUtc = _lastMirrorAlertUtc,
                LastAlertError = _lastAlertError
            };
        }
    }

    public void SaveSettings(
        NotificationSettings settings,
        string? smtpPassword,
        string? telegramBotToken,
        bool clearSmtpPassword,
        bool clearTelegramToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var next = Clone(settings);
        next.Smtp.Port = next.Smtp.Port <= 0 ? 587 : Math.Clamp(next.Smtp.Port, 1, 65535);
        next.Alerts.MirrorStaleHours = Math.Clamp(
            next.Alerts.MirrorStaleHours <= 0 ? 24 : next.Alerts.MirrorStaleHours, 1, 24 * 30);

        lock (_gate)
        {
            Persist(next);
            _settings = next;
        }

        if (clearSmtpPassword)
            _secrets.Remove(SecretKeys.SmtpPassword);
        else if (!string.IsNullOrEmpty(smtpPassword))
            _secrets.Set(SecretKeys.SmtpPassword, smtpPassword);

        if (clearTelegramToken)
            _secrets.Remove(SecretKeys.TelegramBotToken);
        else if (!string.IsNullOrEmpty(telegramBotToken))
            _secrets.Set(SecretKeys.TelegramBotToken, telegramBotToken);

        _logger.LogInformation("Notification settings saved");
    }

    private NotificationSettings? Load()
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<NotificationSettings>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read notification settings");
            return null;
        }
    }

    private void Persist(NotificationSettings settings)
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private void LoadAlertState()
    {
        var path = AlertStatePath();
        if (!File.Exists(path)) return;
        try
        {
            var state = JsonSerializer.Deserialize<AlertStateFile>(File.ReadAllText(path), JsonOpts);
            _lastMirrorAlertUtc = state?.LastMirrorAlertUtc;
            _lastAlertError = state?.LastAlertError;
        }
        catch { /* ignore */ }
    }

    private void PersistAlertState()
    {
        var path = AlertStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var file = new AlertStateFile
        {
            LastMirrorAlertUtc = _lastMirrorAlertUtc,
            LastAlertError = _lastAlertError
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOpts));
    }

    private string SettingsPath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "notifications.json");

    private string AlertStatePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "notification-alert-state.json");

    private static NotificationSettings Clone(NotificationSettings s) => new()
    {
        Smtp = new SmtpChannelSettings
        {
            Enabled = s.Smtp.Enabled,
            Host = s.Smtp.Host ?? "",
            Port = s.Smtp.Port,
            UseSsl = s.Smtp.UseSsl,
            Username = s.Smtp.Username ?? "",
            FromAddress = s.Smtp.FromAddress ?? "",
            FromDisplayName = s.Smtp.FromDisplayName ?? "Jotdex",
            ToAddress = s.Smtp.ToAddress ?? ""
        },
        Telegram = new TelegramChannelSettings
        {
            Enabled = s.Telegram.Enabled,
            ChatId = s.Telegram.ChatId ?? ""
        },
        Alerts = new AlertRulesSettings
        {
            MirrorStaleEnabled = s.Alerts.MirrorStaleEnabled,
            MirrorStaleHours = s.Alerts.MirrorStaleHours
        }
    };

    private sealed class AlertStateFile
    {
        public DateTimeOffset? LastMirrorAlertUtc { get; set; }
        public string? LastAlertError { get; set; }
    }
}
