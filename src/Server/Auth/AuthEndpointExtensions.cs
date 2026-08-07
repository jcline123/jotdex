using System.Security.Claims;
using Jotdex.Core.Auth;
using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Config;
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
            var enforce = ShouldEnforceAuth(env, options);
            return Results.Json(new
            {
                setupComplete = status.SetupComplete,
                authenticated = status.Authenticated,
                authEnforced = enforce,
                setupRequired = enforce && !status.SetupComplete,
                authRequired = enforce && status.SetupComplete && !status.Authenticated,
                username = status.Username,
                displayName = status.DisplayName,
                developmentBypass = !enforce
            });
        });

        app.MapPost("/api/auth/setup", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth) =>
        {
            if (auth.IsSetupComplete)
                return Results.BadRequest(new { error = "Administrator already exists." });

            var body = await request.ReadFromJsonAsync<SetupBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.CreateAdmin(body.Username ?? "", body.Password ?? "", body.DisplayName);
            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.Username!),
                new(ClaimTypes.Role, "admin")
            };
            await ctx.SignInAsync(CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme)));
            return Results.Json(new { success = true, username = result.Username });
        });

        app.MapPost("/api/auth/login", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth) =>
        {
            var body = await request.ReadFromJsonAsync<LoginBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.ValidateCredentials(body.Username ?? "", body.Password ?? "");
            if (!result.Success)
            {
                return Results.Json(new
                {
                    success = false,
                    error = result.Error,
                    lockedOut = result.LockedOut,
                    retryAfterSeconds = result.RetryAfterSeconds
                }, statusCode: result.LockedOut ? StatusCodes.Status429TooManyRequests : StatusCodes.Status401Unauthorized);
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, result.Username!),
                new(ClaimTypes.Role, "admin")
            };
            await ctx.SignInAsync(CookieScheme, new ClaimsPrincipal(new ClaimsIdentity(claims, CookieScheme)));
            return Results.Json(new { success = true, username = result.Username });
        });

        app.MapPost("/api/auth/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieScheme);
            return Results.Json(new { success = true });
        });

        app.MapPost("/api/auth/change-password", async (HttpRequest request, HttpContext ctx, ILocalAuthService auth) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true || string.IsNullOrEmpty(ctx.User.Identity.Name))
                return Results.Unauthorized();

            var body = await request.ReadFromJsonAsync<ChangePasswordBody>();
            if (body is null)
                return Results.BadRequest(new { error = "Invalid body" });

            var result = auth.ChangePassword(ctx.User.Identity.Name, body.CurrentPassword ?? "", body.NewPassword ?? "");
            return result.Success
                ? Results.Json(new { success = true })
                : Results.BadRequest(new { error = result.Error });
        });

        app.MapGet("/api/settings/network", (INetworkSettingsService network) =>
        {
            var s = network.Get();
            return Results.Json(new
            {
                bindMode = s.BindMode,
                port = s.Port,
                httpsPfxPath = s.HttpsPfxPath,
                httpsEnabled = s.HttpsEnabled,
                httpsPasswordConfigured = !string.IsNullOrEmpty(s.HttpsPfxPassword)
                    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JOTDEX_HTTPS_PFX_PASSWORD")),
                listenHost = s.ListenHost,
                isLan = s.IsLan,
                listenUrl = s.ToListenUrl(),
                restartRequiredHint = "Changing bind, port, or HTTPS certificate requires a server restart."
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
                httpsPfxPath = settings.HttpsPfxPath,
                httpsEnabled = settings.HttpsEnabled,
                listenHost = settings.ListenHost,
                isLan = settings.IsLan,
                listenUrl = settings.ToListenUrl(),
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
                IntervalMinutes = body.IntervalMinutes ?? 15
            });

            if (!ok || settings is null)
                return Results.BadRequest(new { error });

            return Results.Json(new
            {
                success = true,
                enabled = settings.Enabled,
                destinationPath = settings.DestinationPath,
                intervalMinutes = settings.IntervalMinutes,
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
    }

    public static IApplicationBuilder UseJotdexAuthGate(this IApplicationBuilder app)
    {
        return app.Use(async (ctx, next) =>
        {
            var env = ctx.RequestServices.GetRequiredService<IHostEnvironment>();
            var options = ctx.RequestServices.GetRequiredService<IOptions<JotdexOptions>>();
            if (!ShouldEnforceAuth(env, options))
            {
                await next();
                return;
            }

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
            if (!auth.IsSetupComplete)
            {
                if (IsSetupAllowedApi(path))
                {
                    await next();
                    return;
                }

                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await ctx.Response.WriteAsJsonAsync(new { error = "Setup required", setupRequired = true });
                return;
            }

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
        path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase);

    private static bool IsSetupAllowedApi(string path) =>
        path.StartsWith("/api/settings/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/setup", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/status", StringComparison.OrdinalIgnoreCase);

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
    }

    private sealed class ChangePasswordBody
    {
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
    }

    private sealed class NetworkBody
    {
        public string? BindMode { get; set; }
        public int? Port { get; set; }
        public string? HttpsPfxPath { get; set; }
        /// <summary>Omit to keep existing; empty string clears; value sets.</summary>
        public string? HttpsPfxPassword { get; set; }
    }

    private sealed class MirrorBody
    {
        public bool? Enabled { get; set; }
        public string? DestinationPath { get; set; }
        public int? IntervalMinutes { get; set; }
    }
}
