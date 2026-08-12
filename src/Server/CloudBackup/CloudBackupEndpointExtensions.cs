using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.CloudBackup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Jotdex.Server.CloudBackup;

public static class CloudBackupEndpointExtensions
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static IEndpointRouteBuilder MapCloudBackupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/cloud-backup", (ICloudBackupCoordinator coordinator) =>
        {
            var summary = coordinator.GetSummary();
            return Results.Json(summary, JsonOpts);
        });

        app.MapGet("/api/cloud-backup/health", (ICloudBackupCoordinator coordinator) =>
        {
            var health = coordinator.GetHealth();
            return Results.Json(health, JsonOpts);
        });

        app.MapPut("/api/cloud-backup/settings", async (HttpRequest request, ICloudBackupSettingsService settings) =>
        {
            var body = await request.ReadFromJsonAsync<CloudBackupSettingsBody>(JsonOpts);
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var current = settings.Get();
            var incoming = new CloudBackupSettings
            {
                SchemaVersion = current.SchemaVersion,
                BackupSetId = current.BackupSetId,
                BackupSetName = body.BackupSetName ?? current.BackupSetName,
                IntervalHours = body.IntervalHours ?? current.IntervalHours,
                VersionsToKeep = body.VersionsToKeep ?? current.VersionsToKeep,
                FullVerificationIntervalDays = body.FullVerificationIntervalDays ?? current.FullVerificationIntervalDays,
                IncludePlainVaultZip = body.IncludePlainVaultZip ?? current.IncludePlainVaultZip,
                Providers = MergeProviders(current.Providers, body.Providers)
            };
            var saved = settings.Save(incoming);
            return Results.Json(new { success = true, settings = saved }, JsonOpts);
        });

        app.MapPost("/api/cloud-backup/run", async (HttpRequest request, ICloudBackupCoordinator coordinator, CancellationToken ct) =>
        {
            CloudProviderKind? filter = null;
            if (request.ContentLength is > 0)
            {
                var body = await request.ReadFromJsonAsync<CloudBackupRunBody>(JsonOpts);
                if (body?.Provider is string p && Enum.TryParse<CloudProviderKind>(p, ignoreCase: true, out var kind))
                    filter = kind;
            }

            var op = await coordinator.StartRunAsync(CloudBackupRunTrigger.Manual, filter, ct);
            return Results.Json(new
            {
                accepted = true,
                operationId = op.OperationId,
                runId = op.RunId,
                running = op.Running,
                phase = op.Phase
            }, JsonOpts, statusCode: StatusCodes.Status202Accepted);
        });

        app.MapGet("/api/cloud-backup/operations/{operationId}", (string operationId, ICloudBackupCoordinator coordinator) =>
        {
            var op = coordinator.GetOperation(operationId);
            return op is null ? Results.NotFound(new { error = "Unknown operation" }) : Results.Json(op, JsonOpts);
        });

        app.MapPost("/api/cloud-backup/providers/{provider}/connect", async (
            string provider,
            HttpContext ctx,
            ICloudOAuthConnectionService oauth,
            ICloudBackupCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (!IsLoopback(ctx))
                return Results.Json(new { error = "Cloud provider connect is only allowed from loopback." },
                    statusCode: StatusCodes.Status403Forbidden);

            if (!TryParseProvider(provider, out var kind))
                return Results.BadRequest(new { error = "Unknown provider" });

            var attempt = await oauth.BeginConnectAsync(kind, ct);
            if (string.Equals(attempt.Error, "ConfigurationUnavailable", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new
                {
                    success = false,
                    connectionState = CloudConnectionState.ConfigurationUnavailable.ToString(),
                    error = "Provider unavailable in this build.",
                    attempt
                }, JsonOpts, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            // Local/public-client Connect can complete immediately only if BeginConnect already finished.
            if (attempt is { Success: true, Completed: true })
                _ = coordinator.StartRunAsync(CloudBackupRunTrigger.ProviderConnected, kind, CancellationToken.None);

            return Results.Json(new { success = true, attempt }, JsonOpts);
        });

        app.MapGet("/api/cloud-backup/providers/{provider}/connect/{attemptId}", (
            string provider,
            string attemptId,
            ICloudOAuthConnectionService oauth) =>
        {
            if (!TryParseProvider(provider, out var kind))
                return Results.BadRequest(new { error = "Unknown provider" });
            var attempt = oauth.GetAttempt(kind, attemptId);
            return attempt is null
                ? Results.NotFound(new { error = "Unknown attempt" })
                : Results.Json(new { attempt }, JsonOpts);
        });

        app.MapPost("/api/cloud-backup/providers/{provider}/connect/{attemptId}/complete", async (
            string provider,
            string attemptId,
            HttpRequest request,
            HttpContext ctx,
            ICloudOAuthConnectionService oauth,
            ICloudBackupCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (!IsLoopback(ctx))
                return Results.Json(new { error = "Cloud provider connect is only allowed from loopback." },
                    statusCode: StatusCodes.Status403Forbidden);

            if (!TryParseProvider(provider, out var kind))
                return Results.BadRequest(new { error = "Unknown provider" });

            var body = await request.ReadFromJsonAsync<CloudBackupConnectCompleteBody>(JsonOpts);
            var attempt = await oauth.CompleteAsync(kind, attemptId, body?.Code ?? body?.AuthorizationCode, ct);
            if (attempt.Success)
            {
                // Kick an immediate backup for the newly connected provider
                _ = coordinator.StartRunAsync(CloudBackupRunTrigger.ProviderConnected, kind, CancellationToken.None);
            }

            return Results.Json(new { success = attempt.Success, attempt }, JsonOpts);
        });

        app.MapPost("/api/cloud-backup/providers/{provider}/disconnect", async (
            string provider,
            ICloudOAuthConnectionService oauth,
            CancellationToken ct) =>
        {
            if (!TryParseProvider(provider, out var kind))
                return Results.BadRequest(new { error = "Unknown provider" });
            await oauth.DisconnectAsync(kind, ct);
            return Results.Json(new { success = true });
        });

        app.MapPost("/api/cloud-backup/providers/{provider}/retry", async (
            string provider,
            ICloudBackupCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (!TryParseProvider(provider, out var kind))
                return Results.BadRequest(new { error = "Unknown provider" });
            var op = await coordinator.StartRunAsync(CloudBackupRunTrigger.Retry, kind, ct);
            return Results.Json(new
            {
                accepted = true,
                operationId = op.OperationId,
                runId = op.RunId
            }, JsonOpts, statusCode: StatusCodes.Status202Accepted);
        });

        // Loopback OAuth redirects (PKCE). Browser returns here with ?code=&state=.
        MapOAuthCallback(app, "dropbox", CloudProviderKind.Dropbox);
        MapOAuthCallback(app, "google", CloudProviderKind.GoogleDrive);
        MapOAuthCallback(app, "onedrive", CloudProviderKind.OneDrive);

        return app;
    }

    private static void MapOAuthCallback(IEndpointRouteBuilder app, string pathSegment, CloudProviderKind kind)
    {
        app.MapGet($"/oauth/{pathSegment}", async (
            HttpContext ctx,
            ICloudOAuthConnectionService oauth,
            ICloudBackupCoordinator coordinator,
            CancellationToken ct) =>
        {
            if (!IsLoopback(ctx))
            {
                return Results.Content(
                    OAuthHtmlPage(false, kind, "OAuth callback is only allowed from loopback."),
                    "text/html",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var error = ctx.Request.Query["error"].FirstOrDefault();
            var code = ctx.Request.Query["code"].FirstOrDefault();
            var state = ctx.Request.Query["state"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(error))
            {
                if (!string.IsNullOrWhiteSpace(state))
                    await oauth.CompleteAsync(kind, state, null, ct);
                return Results.Content(
                    OAuthHtmlPage(false, kind, "Authorization was denied or failed: " + error),
                    "text/html");
            }

            if (string.IsNullOrWhiteSpace(state))
            {
                return Results.Content(
                    OAuthHtmlPage(false, kind, "Missing OAuth state."),
                    "text/html",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var attempt = await oauth.CompleteAsync(kind, state, code, ct);
            if (attempt.Success)
            {
                _ = coordinator.StartRunAsync(CloudBackupRunTrigger.ProviderConnected, kind, CancellationToken.None);
                return Results.Content(
                    OAuthHtmlPage(true, kind, attempt.AccountDisplayName ?? attempt.AccountEmail),
                    "text/html");
            }

            return Results.Content(
                OAuthHtmlPage(false, kind, attempt.Error ?? "Connection failed."),
                "text/html");
        });
    }

    private static string OAuthHtmlPage(bool success, CloudProviderKind kind, string? detail)
    {
        var title = success ? "Connected" : "Connection failed";
        var headline = WebUtility.HtmlEncode(success ? $"{kind} connected" : $"{kind} connection failed");
        var bodyMsg = success
            ? "You can close this tab and return to Jotdex Settings."
            : "You can close this tab and try connecting again from Jotdex Settings.";
        var safeDetail = WebUtility.HtmlEncode(detail ?? "");
        var color = success ? "#0a7a3e" : "#a11";
        var detailHtml = string.IsNullOrWhiteSpace(safeDetail)
            ? ""
            : "<p><strong>Details:</strong> " + safeDetail + "</p>";
        return "<!DOCTYPE html>\n<html lang=\"en\"><head><meta charset=\"utf-8\" />"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />"
            + "<title>Jotdex — " + WebUtility.HtmlEncode(title) + "</title>"
            + "<style>body{font-family:Segoe UI,system-ui,sans-serif;margin:2.5rem;background:#f6f7f9;color:#1a1a1a;}"
            + "h1{color:" + color + ";font-size:1.35rem;}p{max-width:36rem;line-height:1.45;}</style></head><body>"
            + "<h1>" + headline + "</h1><p>" + bodyMsg + "</p>" + detailHtml
            + "</body></html>";
    }

    private static List<CloudProviderSettings> MergeProviders(
        List<CloudProviderSettings> current,
        List<CloudProviderSettingsBody>? incoming)
    {
        if (incoming is null || incoming.Count == 0)
            return current.Select(CloneProvider).ToList();

        var result = current.Select(CloneProvider).ToList();
        foreach (var body in incoming)
        {
            if (body.Provider is null || !Enum.TryParse<CloudProviderKind>(body.Provider, ignoreCase: true, out var kind))
                continue;
            var existing = result.FirstOrDefault(p => p.Provider == kind);
            if (existing is null)
            {
                existing = new CloudProviderSettings
                {
                    Provider = kind,
                    Enabled = body.Enabled ?? false
                };
                result.Add(existing);
            }
            else if (body.Enabled is bool en)
            {
                existing.Enabled = en;
            }

            if (body.OAuthClientId is not null)
                existing.OAuthClientId = string.IsNullOrWhiteSpace(body.OAuthClientId) ? null : body.OAuthClientId.Trim();
            if (body.OAuthRedirectUri is not null)
                existing.OAuthRedirectUri = string.IsNullOrWhiteSpace(body.OAuthRedirectUri) ? null : body.OAuthRedirectUri.Trim();
            if (body.ClearOAuthClientSecret)
                existing.OAuthClientSecret = null;
            else if (body.OAuthClientSecret is not null)
                existing.OAuthClientSecret = string.IsNullOrWhiteSpace(body.OAuthClientSecret) ? null : body.OAuthClientSecret.Trim();
        }
        return result;
    }

    private static CloudProviderSettings CloneProvider(CloudProviderSettings p) =>
        new()
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
        };

    private static bool TryParseProvider(string provider, out CloudProviderKind kind) =>
        Enum.TryParse(provider, ignoreCase: true, out kind);

    private static bool IsLoopback(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return true;
        if (IPAddress.IsLoopback(ip)) return true;
        // IPv4-mapped IPv6
        if (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4())) return true;
        return false;
    }

    private sealed class CloudBackupSettingsBody
    {
        public string? BackupSetName { get; set; }
        public int? IntervalHours { get; set; }
        public int? VersionsToKeep { get; set; }
        public int? FullVerificationIntervalDays { get; set; }
        public bool? IncludePlainVaultZip { get; set; }
        public List<CloudProviderSettingsBody>? Providers { get; set; }
    }

    private sealed class CloudProviderSettingsBody
    {
        public string? Provider { get; set; }
        public bool? Enabled { get; set; }
        [JsonPropertyName("oauthClientId")]
        public string? OAuthClientId { get; set; }
        [JsonPropertyName("oauthClientSecret")]
        public string? OAuthClientSecret { get; set; }
        [JsonPropertyName("oauthRedirectUri")]
        public string? OAuthRedirectUri { get; set; }
        public bool ClearOAuthClientSecret { get; set; }
    }

    private sealed class CloudBackupRunBody
    {
        public string? Provider { get; set; }
    }

    private sealed class CloudBackupConnectCompleteBody
    {
        public string? Code { get; set; }
        public string? AuthorizationCode { get; set; }
    }
}
