using System.Security.Claims;
using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Core.Notifications;
using Jotdex.Infrastructure.Config;
using Jotdex.Infrastructure.Maintenance;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace Jotdex.Server.Auth;

public static class AuthEndpointExtensions
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static IServiceCollection AddJotdexAuth(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<ILocalAuthService, Jotdex.Infrastructure.Auth.LocalAuthService>();
        services.AddSingleton<INetworkSettingsService, NetworkSettingsService>();
        // Vault mirror registered in Program.cs (hosted service)

        var idle = config.GetSection(JotdexOptions.SectionName).Get<JotdexOptions>()?.Auth.IdleTimeoutMinutes ?? 60;
        services
            .AddAuthentication(CookieScheme)
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.Name = "jotdex_session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(Math.Clamp(idle, 5, 24 * 60));
                o.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                o.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static bool ShouldEnforceAuth(IHostEnvironment env, IOptions<JotdexOptions> options) =>
        !(env.IsDevelopment() && options.Value.Auth.BypassInDevelopment);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/status", (HttpContext ctx, ILocalAuthService auth, IHostEnvironment env, IOptions<JotdexOptions> options) =>
        {
            var username = ctx.User.Identity?.IsAuthenticated == true ? ctx.User.Identity.Name : null;
            var status = auth.GetStatus(username);
            var passwordSet = status.SetupComplete;
            // Password is optional. When set, sign-in is required (even in Development).
            // When not set, the app stays open. Dev bypass only matters when no password exists.
            var openAccess = !passwordSet;
            var developmentBypass = openAccess && env.IsDevelopment() && options.Value.Auth.BypassInDevelopment;
            return Results.Json(new
            {
                setupComplete = passwordSet,
                passwordSet,
                authenticated = status.Authenticated,
                authEnforced = passwordSet,
                setupRequired = false,
                authRequired = passwordSet && !status.Authenticated,
                totpEnabled = status.TotpEnabled,
                username = status.Username,
                displayName = status.DisplayName,
                developmentBypass
            });
        });

        app.MapPost("/api/auth/setup", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth, IMoveKitCryptoService moveKitCrypto) =>
        {
            if (auth.IsSetupComplete)
                return Results.BadRequest(new { error = "A password is already set." });

            var body = await request.ReadFromJsonAsync<SetupBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.CreateAdmin(
                body.Username ?? ILocalAuthService.DefaultUsername,
                body.Password ?? "",
                body.DisplayName);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            moveKitCrypto.OnPasswordSet(body.Password ?? "");

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.Username!),
                new(ClaimTypes.Role, "admin")
            };
            await ctx.SignInAsync(CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme)));
            return Results.Json(new { success = true, username = result.Username });
        });

        app.MapPost("/api/auth/login", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth, IMoveKitCryptoService moveKitCrypto) =>
        {
            var body = await request.ReadFromJsonAsync<LoginBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.ValidateCredentials(body.Username ?? "", body.Password ?? "", body.TotpCode);
            if (!result.Success)
            {
                return Results.Json(new
                {
                    success = false,
                    error = result.Error,
                    requiresTotp = result.RequiresTotp,
                    lockedOut = result.LockedOut,
                    retryAfterSeconds = result.RetryAfterSeconds
                }, statusCode: result.RequiresTotp
                    ? StatusCodes.Status401Unauthorized
                    : result.LockedOut ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized);
            }

            // Existing installs: initialize move-kit encryption wrap on first successful unlock.
            if (!string.IsNullOrEmpty(body.Password) && !moveKitCrypto.HasEncryptionKey)
            {
                try { moveKitCrypto.OnPasswordSet(body.Password); } catch { /* non-fatal */ }
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.Username!),
                new(ClaimTypes.Role, "admin")
            };
            await ctx.SignInAsync(CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme)));
            return Results.Json(new { success = true, username = result.Username });
        });

        app.MapPost("/api/auth/totp/begin", (HttpContext ctx, ILocalAuthService auth) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(ctx.User.Identity.Name))
                return Results.Unauthorized();
            var result = auth.BeginTotpEnrollment(ctx.User.Identity.Name);
            return result.Success
                ? Results.Json(new { success = true, manualKey = result.ManualKey, otpAuthUri = result.OtpAuthUri })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/auth/totp/confirm", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(ctx.User.Identity.Name))
                return Results.Unauthorized();
            var body = await request.ReadFromJsonAsync<TotpCodeBody>();
            if (body is null) return Results.BadRequest(new { error = "Invalid body" });
            var result = auth.ConfirmTotpEnrollment(ctx.User.Identity.Name, body.Code ?? "");
            return result.Success
                ? Results.Json(new { success = true, recoveryCodes = result.RecoveryCodes })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/auth/totp/disable", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(ctx.User.Identity.Name))
                return Results.Unauthorized();
            var body = await request.ReadFromJsonAsync<TotpDisableBody>();
            if (body is null) return Results.BadRequest(new { error = "Invalid body" });
            var result = auth.DisableTotp(ctx.User.Identity.Name, body.Password ?? "", body.TotpCode);
            return result.Success
                ? Results.Json(new { success = true })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieScheme);
            return Results.Json(new { success = true });
        });

        app.MapPost("/api/auth/change-password", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth, IMoveKitCryptoService moveKitCrypto) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(ctx.User.Identity.Name))
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<ChangePasswordBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.ChangePassword(ctx.User.Identity.Name, body.CurrentPassword ?? "", body.NewPassword ?? "");
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            moveKitCrypto.OnPasswordSet(body.NewPassword ?? "");
            return Results.Json(new { success = true });
        });

        app.MapPost("/api/auth/remove-password", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth, IMoveKitCryptoService moveKitCrypto) =>
        {
            var body = await request.ReadFromJsonAsync<RemovePasswordBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.RemovePassword(body.CurrentPassword ?? "");
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            moveKitCrypto.OnPasswordCleared();
            await ctx.SignOutAsync(CookieScheme);
            return Results.Json(new { success = true });
        });

        app.MapGet("/api/settings/network", (INetworkSettingsService network) =>
        {
            var s = network.Get();
            return Results.Json(new
            {
                bindMode = s.BindMode,
                port = s.Port,
                httpsSelfSigned = s.HttpsSelfSigned,
                httpsPort = s.EffectiveHttpsPort,
                httpsPfxPath = s.HttpsPfxPath,
                httpsEnabled = s.HttpsEnabled,
                httpsPasswordConfigured = !string.IsNullOrEmpty(s.HttpsPfxPassword)
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JOTDEX_HTTPS_PFX_PASSWORD")),
                listenHost = s.ListenHost,
                isLan = s.IsLan,
                listenUrl = s.ToListenUrl(),
                httpUrl = s.ToHttpUrl(),
                httpsUrl = s.ToHttpsUrl(),
                restartRequiredHint = "Changing bind, port, or HTTPS requires a server restart."
            });
        });

        app.MapPut("/api/settings/network", async (HttpRequest request, INetworkSettingsService network) =>
        {
            var body = await request.ReadFromJsonAsync<NetworkBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var updatePassword = body.HttpsPfxPassword is not null;
            var (ok, error, settings) = network.Save(new NetworkSettings
            {
                BindMode = body.BindMode ?? "loopback",
                Port = body.Port ?? 5180,
                HttpsSelfSigned = body.HttpsSelfSigned ?? false,
                HttpsPort = body.HttpsPort ?? 0,
                HttpsPfxPath = body.HttpsPfxPath,
                HttpsPfxPassword = body.HttpsPfxPassword
            }, updatePassword);

            if (!ok || settings is null)
                return Results.BadRequest(new { error });

            return Results.Json(new
            {
                success = true,
                bindMode = settings.BindMode,
                port = settings.Port,
                httpsSelfSigned = settings.HttpsSelfSigned,
                httpsPort = settings.EffectiveHttpsPort,
                httpsPfxPath = settings.HttpsPfxPath,
                httpsEnabled = settings.HttpsEnabled,
                listenHost = settings.ListenHost,
                isLan = settings.IsLan,
                listenUrl = settings.ToListenUrl(),
                httpUrl = settings.ToHttpUrl(),
                httpsUrl = settings.ToHttpsUrl(),
                restartRequired = true
            });
        });

        app.MapGet("/api/settings/mirror", (IVaultMirrorService mirror) =>
        {
            var s = mirror.GetSettings();
            var st = mirror.GetStatus();
            return Results.Json(new
            {
                enabled = s.Enabled,
                destinationPath = s.DestinationPath,
                intervalMinutes = s.IntervalMinutes,
                includeDailyMoveKit = s.IncludeDailyMoveKit,
                status = st
            });
        });

        app.MapPut("/api/settings/mirror", async (HttpRequest request, IVaultMirrorService mirror) =>
        {
            var body = await request.ReadFromJsonAsync<MirrorBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var (ok, error, settings) = mirror.SaveSettings(new VaultMirrorSettings
            {
                Enabled = body.Enabled ?? false,
                DestinationPath = body.DestinationPath ?? "",
                IntervalMinutes = body.IntervalMinutes ?? 15,
                IncludeDailyMoveKit = body.IncludeDailyMoveKit ?? false
            });

            if (!ok || settings is null)
                return Results.BadRequest(new { error });

            return Results.Json(new
            {
                success = true,
                enabled = settings.Enabled,
                destinationPath = settings.DestinationPath,
                intervalMinutes = settings.IntervalMinutes,
                includeDailyMoveKit = settings.IncludeDailyMoveKit,
                status = mirror.GetStatus()
            });
        });

        app.MapPost("/api/settings/mirror/run", async (IVaultMirrorService mirror, CancellationToken ct) =>
        {
            var (ok, error) = await mirror.RunNowAsync(ct);
            return ok
                ? Results.Json(new { success = true, status = mirror.GetStatus() })
                : Results.BadRequest(new { error, status = mirror.GetStatus() });
        });

        app.MapGet("/api/settings/notifications", (INotificationSettingsService notify) =>
        {
            var s = notify.GetSettings();
            var st = notify.GetStatus();
            return Results.Json(new
            {
                smtp = s.Smtp,
                telegram = s.Telegram,
                alerts = s.Alerts,
                status = st
            });
        });

        app.MapPut("/api/settings/notifications", async (HttpRequest request, INotificationSettingsService notify) =>
        {
            var body = await request.ReadFromJsonAsync<NotificationsBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var settings = new NotificationSettings
            {
                Smtp = new SmtpChannelSettings
                {
                    Enabled = body.Smtp?.Enabled ?? false,
                    Host = body.Smtp?.Host ?? "",
                    Port = body.Smtp?.Port ?? 587,
                    UseSsl = body.Smtp?.UseSsl ?? true,
                    Username = body.Smtp?.Username ?? "",
                    FromAddress = body.Smtp?.FromAddress ?? "",
                    FromDisplayName = body.Smtp?.FromDisplayName ?? "Jotdex",
                    ToAddress = body.Smtp?.ToAddress ?? ""
                },
                Telegram = new TelegramChannelSettings
                {
                    Enabled = body.Telegram?.Enabled ?? false,
                    ChatId = body.Telegram?.ChatId ?? ""
                },
                Alerts = new AlertRulesSettings
                {
                    MirrorStaleEnabled = body.Alerts?.MirrorStaleEnabled ?? false,
                    MirrorStaleHours = body.Alerts?.MirrorStaleHours ?? 24
                }
            };

            notify.SaveSettings(
                settings,
                body.SmtpPassword,
                body.TelegramBotToken,
                body.ClearSmtpPassword ?? false,
                body.ClearTelegramToken ?? false);

            return Results.Json(new
            {
                success = true,
                smtp = settings.Smtp,
                telegram = settings.Telegram,
                alerts = settings.Alerts,
                status = notify.GetStatus()
            });
        });

        app.MapPost("/api/settings/notifications/test", async (IOpsAlertSender sender, CancellationToken ct) =>
        {
            var (ok, error) = await sender.SendTestAsync(ct);
            return ok
                ? Results.Json(new { success = true, warning = error })
                : Results.BadRequest(new { error });
        });
    }

    public static IApplicationBuilder UseJotdexAuthGate(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            if (IsAnonymousApi(path))
            {
                await next();
                return;
            }

            var auth = ctx.RequestServices.GetRequiredService<ILocalAuthService>();
            // No password configured → open access (optional protection).
            if (!auth.IsSetupComplete)
            {
                await next();
                return;
            }

            // Password is set → require a signed-in session (including Development).
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsJsonAsync(new { error = "Authentication required" });
                return;
            }

            await next();
        });
    }

    private static bool IsAnonymousApi(string path) =>
        path.StartsWith("/api/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/updates/check", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);

    private sealed class SetupBody
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? DisplayName { get; set; }
    }

    private sealed class LoginBody
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }

    private sealed class TotpCodeBody
    {
        public string? Code { get; set; }
    }

    private sealed class TotpDisableBody
    {
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }

    private sealed class ChangePasswordBody
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
    }

    private sealed class RemovePasswordBody
    {
        public string? CurrentPassword { get; set; }
    }

    private sealed class NetworkBody
    {
        public string? BindMode { get; set; }
        public int? Port { get; set; }
        public bool? HttpsSelfSigned { get; set; }
        public int? HttpsPort { get; set; }
        public string? HttpsPfxPath { get; set; }
        /// <summary>Omit to keep existing; empty string clears; value sets.</summary>
        public string? HttpsPfxPassword { get; set; }
    }

    private sealed class MirrorBody
    {
        public bool? Enabled { get; set; }
        public string? DestinationPath { get; set; }
        public int? IntervalMinutes { get; set; }
        public bool? IncludeDailyMoveKit { get; set; }
    }

    private sealed class NotificationsBody
    {
        public SmtpBody? Smtp { get; set; }
        public TelegramBody? Telegram { get; set; }
        public AlertsBody? Alerts { get; set; }
        public string? SmtpPassword { get; set; }
        public string? TelegramBotToken { get; set; }
        public bool? ClearSmtpPassword { get; set; }
        public bool? ClearTelegramToken { get; set; }
    }

    private sealed class SmtpBody
    {
        public bool? Enabled { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public bool? UseSsl { get; set; }
        public string? Username { get; set; }
        public string? FromAddress { get; set; }
        public string? FromDisplayName { get; set; }
        public string? ToAddress { get; set; }
    }

    private sealed class TelegramBody
    {
        public bool? Enabled { get; set; }
        public string? ChatId { get; set; }
    }

    private sealed class AlertsBody
    {
        public bool? MirrorStaleEnabled { get; set; }
        public int? MirrorStaleHours { get; set; }
    }
}
