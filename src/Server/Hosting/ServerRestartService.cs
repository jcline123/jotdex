using System.Diagnostics;
using System.Text;
using Jotdex.Infrastructure.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Server.Hosting;

public interface IServerRestartService
{
    (bool Success, string? Error, string? Message) ScheduleRestart();
}

/// <summary>
/// Relaunches this process after releasing the listen port.
/// Strips --urls / ASPNETCORE_URLS so data/config/network.json applies on the next start.
/// </summary>
public sealed class ServerRestartService : IServerRestartService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly INetworkSettingsService _network;
    private readonly ILogger<ServerRestartService> _logger;
    private int _scheduled;

    public ServerRestartService(
        IHostApplicationLifetime lifetime,
        INetworkSettingsService network,
        ILogger<ServerRestartService> logger)
    {
        _lifetime = lifetime;
        _network = network;
        _logger = logger;
    }

    public (bool Success, string? Error, string? Message) ScheduleRestart()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1)
            return (false, "A restart is already in progress.", null);

        var settings = _network.Get();
        var listenHint = settings.ToListenUrl();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600).ConfigureAwait(false);
                LaunchDelayedRelaunch();
                _logger.LogWarning("Server restart scheduled — shutting down (next listen: {Url})", listenHint);
                _lifetime.StopApplication();
                await Task.Delay(400).ConfigureAwait(false);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to restart server");
                Interlocked.Exchange(ref _scheduled, 0);
            }
        });

        return (true, null, $"Restarting… reconnect at {listenHint} in a few seconds.");
    }

    private void LaunchDelayedRelaunch()
    {
        var argv = Environment.GetCommandLineArgs();
        if (argv.Length == 0 || string.IsNullOrWhiteSpace(argv[0]))
            throw new InvalidOperationException("Could not determine process path for restart.");

        var exe = argv[0];
        var filtered = FilterUrlArgs(argv.Skip(1));
        var workDir = Directory.GetCurrentDirectory();

        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("timeout /t 2 /nobreak >nul");
        sb.Append("cd /d ").AppendLine(QuoteCmd(workDir));

        // Clear URL overrides so NetworkListenConfigurator reads network.json
        sb.AppendLine("set ASPNETCORE_URLS=");

        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(env))
            sb.Append("set ASPNETCORE_ENVIRONMENT=").AppendLine(env);

        sb.Append(QuoteCmd(exe));
        foreach (var a in filtered)
            sb.Append(' ').Append(QuoteCmd(a));
        sb.AppendLine();
        sb.AppendLine("del \"%~f0\"");

        var script = Path.Combine(Path.GetTempPath(), $"jotdex-restart-{Guid.NewGuid():N}.cmd");
        File.WriteAllText(script, sb.ToString(), Encoding.ASCII);

        Process.Start(new ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
    }

    private static List<string> FilterUrlArgs(IEnumerable<string> args)
    {
        var list = args.ToList();
        var result = new List<string>();
        for (var i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a.Equals("--urls", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < list.Count && LooksLikeUrl(list[i + 1]))
                    i++;
                continue;
            }

            if (a.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
                continue;

            // Bare URL left over from `--urls http://…` split oddly
            if (LooksLikeUrl(a))
                continue;

            result.Add(a);
        }

        return result;
    }

    private static bool LooksLikeUrl(string a) =>
        a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        a.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string QuoteCmd(string value)
    {
        if (value.Length == 0) return "\"\"";
        if (!value.Contains(' ') && !value.Contains('"') && !value.Contains('&'))
            return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
