using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Config;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Jotdex.Server.Hosting;

/// <summary>Apply listen URLs / HTTPS from data/config/network.json when ASPNETCORE_URLS / --urls are unset.</summary>
public static class NetworkListenConfigurator
{
    public static void Apply(WebApplicationBuilder builder)
    {
        // launchSettings applicationUrl injects ASPNETCORE_URLS under `dotnet run`, which used to
        // permanently override saved LAN settings. Prefer network.json whenever it exists.
        // Set JOTDEX_FORCE_URLS=1 to keep an explicit ASPNETCORE_URLS override.
        var forceUrls = string.Equals(
            Environment.GetEnvironmentVariable("JOTDEX_FORCE_URLS"),
            "1",
            StringComparison.OrdinalIgnoreCase);
        if (forceUrls && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
            return;

        try
        {
            var opts = builder.Configuration.GetSection(JotdexOptions.SectionName).Get<JotdexOptions>() ?? new JotdexOptions();
            var dataRoot = ResolveDataRoot(builder, opts);
            var path = Path.Combine(dataRoot, "config", "network.json");

            // No saved settings yet — leave launchSettings / defaults alone.
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<NetworkSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new NetworkSettings();

            var host = settings.ListenHost;
            var port = settings.Port is >= 1 and <= 65535 ? settings.Port : 5180;
            var pfx = settings.HttpsPfxPath?.Trim();
            var useHttps = !string.IsNullOrWhiteSpace(pfx) && File.Exists(pfx);

            // Clear launchSettings-injected URLs so UseUrls / Kestrel Listen win.
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", null);
            builder.WebHost.UseSetting(WebHostDefaults.ServerUrlsKey, null);

            if (!useHttps)
            {
                builder.WebHost.UseUrls(settings.ToHttpUrl());
                Console.WriteLine($"Jotdex: HTTP listening on {settings.ToHttpUrl()} (from network.json)");
                return;
            }

            var password = ResolvePfxPassword(settings);
            X509Certificate2 cert;
            try
            {
                cert = string.IsNullOrEmpty(password)
                    ? X509CertificateLoader.LoadPkcs12FromFile(pfx!, null)
                    : X509CertificateLoader.LoadPkcs12FromFile(pfx!, password);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Jotdex: failed to load HTTPS PFX ({ex.Message}); falling back to HTTP.");
                builder.WebHost.UseUrls(settings.ToHttpUrl());
                return;
            }

            var ip = IPAddress.Parse(host);
            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                kestrel.Listen(ip, port, lo =>
                {
                    lo.Protocols = HttpProtocols.Http1AndHttp2;
                    lo.UseHttps(cert);
                });
            });

            Console.WriteLine($"Jotdex: HTTPS listening on https://{host}:{port}");
        }
        catch
        {
            builder.WebHost.UseUrls("http://127.0.0.1:5180");
        }
    }

    private static string? ResolvePfxPassword(NetworkSettings settings)
    {
        var env = Environment.GetEnvironmentVariable("JOTDEX_HTTPS_PFX_PASSWORD");
        if (!string.IsNullOrEmpty(env))
            return env;
        return string.IsNullOrEmpty(settings.HttpsPfxPassword) ? null : settings.HttpsPfxPassword;
    }

    private static string ResolveDataRoot(WebApplicationBuilder builder, JotdexOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.DataRoot))
            return Path.GetFullPath(opts.DataRoot);

        if (opts.PortableMode)
            return Path.Combine(builder.Environment.ContentRootPath, "data");

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "Jotdex");
    }
}
