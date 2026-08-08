using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Notifications;
using Jotdex.Core.Secrets;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Notifications;

public sealed class OpsAlertSender : IOpsAlertSender
{
    private readonly INotificationSettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpsAlertSender> _logger;

    public OpsAlertSender(
        INotificationSettingsService settings,
        ISecretStore secrets,
        IHttpClientFactory httpClientFactory,
        ILogger<OpsAlertSender> logger)
    {
        _settings = settings;
        _secrets = secrets;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public Task<(bool Success, string? Error)> SendTestAsync(CancellationToken ct = default) =>
        SendAsync("Jotdex test alert", "This is a test message from Jotdex ops notifications.", ct);

    public async Task<(bool Success, string? Error)> SendAsync(string subject, string body, CancellationToken ct = default)
    {
        var cfg = _settings.GetSettings();
        var errors = new List<string>();
        var any = false;
        var ok = false;

        if (cfg.Smtp.Enabled)
        {
            any = true;
            var (sOk, sErr) = await SendSmtpAsync(cfg.Smtp, subject, body, ct).ConfigureAwait(false);
            if (sOk) ok = true;
            else if (sErr is not null) errors.Add("SMTP: " + sErr);
        }

        if (cfg.Telegram.Enabled)
        {
            any = true;
            var (tOk, tErr) = await SendTelegramAsync(cfg.Telegram, subject, body, ct).ConfigureAwait(false);
            if (tOk) ok = true;
            else if (tErr is not null) errors.Add("Telegram: " + tErr);
        }

        if (!any)
            return (false, "No notification channels enabled. Enable SMTP and/or Telegram in Settings → Notifications.");

        if (ok) return (true, errors.Count > 0 ? string.Join("; ", errors) : null);
        return (false, string.Join("; ", errors));
    }

    private async Task<(bool Success, string? Error)> SendSmtpAsync(
        SmtpChannelSettings smtp,
        string subject,
        string body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(smtp.Host))
            return (false, "SMTP host is required.");
        if (string.IsNullOrWhiteSpace(smtp.ToAddress) || string.IsNullOrWhiteSpace(smtp.FromAddress))
            return (false, "SMTP from/to addresses are required.");

        _secrets.TryGet(SecretKeys.SmtpPassword, out var password);

        try
        {
            using var message = new MailMessage
            {
                From = new MailAddress(smtp.FromAddress.Trim(), string.IsNullOrWhiteSpace(smtp.FromDisplayName) ? "Jotdex" : smtp.FromDisplayName.Trim()),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            message.To.Add(smtp.ToAddress.Trim());

            using var client = new SmtpClient(smtp.Host.Trim(), smtp.Port)
            {
                EnableSsl = smtp.UseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 30_000
            };

            if (!string.IsNullOrWhiteSpace(smtp.Username))
            {
                client.Credentials = new NetworkCredential(smtp.Username.Trim(), password ?? "");
            }

            await client.SendMailAsync(message, ct).ConfigureAwait(false);
            _logger.LogInformation("Ops alert email sent to {To}", smtp.ToAddress);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMTP alert failed");
            return (false, ex.Message);
        }
    }

    private async Task<(bool Success, string? Error)> SendTelegramAsync(
        TelegramChannelSettings tg,
        string subject,
        string body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(tg.ChatId))
            return (false, "Telegram chat id is required.");
        if (!_secrets.TryGet(SecretKeys.TelegramBotToken, out var token) || string.IsNullOrWhiteSpace(token))
            return (false, "Telegram bot token is not set.");

        try
        {
            var client = _httpClientFactory.CreateClient("telegram");
            var text = $"*{EscapeMd(subject)}*\n{EscapeMd(body)}";
            var url = $"https://api.telegram.org/bot{token.Trim()}/sendMessage";
            using var content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    chat_id = tg.ChatId.Trim(),
                    text,
                    parse_mode = "Markdown"
                }),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.UserAgent.ParseAdd("Jotdex-OpsAlert");
            using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
            var resBody = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram alert failed: {Status} {Body}", res.StatusCode, resBody);
                return (false, $"HTTP {(int)res.StatusCode}");
            }

            _logger.LogInformation("Ops alert Telegram message sent");
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram alert failed");
            return (false, ex.Message);
        }
    }

    private static string EscapeMd(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("*", "\\*").Replace("_", "\\_").Replace("`", "\\`").Replace("[", "\\[");
}
