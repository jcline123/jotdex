namespace Jotdex.Core.Notifications;

public static class SecretKeys
{
    public const string SmtpPassword = "notifications.smtp.password";
    public const string TelegramBotToken = "notifications.telegram.botToken";
    public const string TotpSecret = "auth.totp.secret";
}

public sealed class NotificationSettings
{
    public SmtpChannelSettings Smtp { get; set; } = new();
    public TelegramChannelSettings Telegram { get; set; } = new();
    public AlertRulesSettings Alerts { get; set; } = new();
}

public sealed class SmtpChannelSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromDisplayName { get; set; } = "Jotdex";
    public string ToAddress { get; set; } = "";
}

public sealed class TelegramChannelSettings
{
    public bool Enabled { get; set; }
    public string ChatId { get; set; } = "";
}

public sealed class AlertRulesSettings
{
    public bool MirrorStaleEnabled { get; set; }
    public int MirrorStaleHours { get; set; } = 24;
}

public sealed class NotificationStatus
{
    public bool SmtpConfigured { get; init; }
    public bool SmtpPasswordSet { get; init; }
    public bool TelegramConfigured { get; init; }
    public bool TelegramTokenSet { get; init; }
    public DateTimeOffset? LastMirrorAlertUtc { get; init; }
    public string? LastAlertError { get; init; }
}

public interface INotificationSettingsService
{
    NotificationSettings GetSettings();
    NotificationStatus GetStatus();
    void SaveSettings(NotificationSettings settings, string? smtpPassword, string? telegramBotToken, bool clearSmtpPassword, bool clearTelegramToken);
}

public interface IOpsAlertSender
{
    Task<(bool Success, string? Error)> SendAsync(string subject, string body, CancellationToken ct = default);
    Task<(bool Success, string? Error)> SendTestAsync(CancellationToken ct = default);
}

public interface IMirrorAlertState
{
    DateTimeOffset? LastMirrorAlertUtc { get; }
    string? LastAlertError { get; }
    void RecordAlert(DateTimeOffset utc, string? error);
}
