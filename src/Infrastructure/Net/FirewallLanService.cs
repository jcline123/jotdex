using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Net;

public sealed class FirewallEnsureResult
{
    public required bool Success { get; init; }
    /// <summary>User cancelled UAC or elevation failed.</summary>
    public bool ElevationDenied { get; init; }
    public bool SkippedNotWindows { get; init; }
    public string? Message { get; init; }
    public int? ExitCode { get; init; }
}

public interface IFirewallLanService
{
    /// <summary>
    /// Prompts UAC and adds inbound allow rules for HTTP (and HTTPS when httpsPort &gt; 0).
    /// LAN settings are independent — callers should still save bind=lan if this fails.
    /// </summary>
    FirewallEnsureResult EnsureLanRules(int httpPort, int httpsPort = 0, bool enable = true);
}

public sealed class FirewallLanService : IFirewallLanService
{
    private readonly IHostEnvironment _env;
    private readonly ILogger<FirewallLanService> _logger;

    public FirewallLanService(IHostEnvironment env, ILogger<FirewallLanService> logger)
    {
        _env = env;
        _logger = logger;
    }

    public FirewallEnsureResult EnsureLanRules(int httpPort, int httpsPort = 0, bool enable = true)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new FirewallEnsureResult
            {
                Success = false,
                SkippedNotWindows = true,
                Message = "Firewall automation is only available on Windows."
            };
        }

        if (httpPort is < 1 or > 65535)
            return new FirewallEnsureResult { Success = false, Message = "Invalid HTTP port." };
        if (httpsPort is not 0 and (< 1 or > 65535))
            return new FirewallEnsureResult { Success = false, Message = "Invalid HTTPS port." };

        var script = ResolveScriptPath();
        if (script is null)
        {
            return new FirewallEnsureResult
            {
                Success = false,
                Message =
                    "Ensure-JotdexFirewall.ps1 not found beside the app. LAN was still saved — open Windows Firewall manually for TCP " +
                    DescribePorts(httpPort, httpsPort) + ", or copy the script from the Jotdex scripts folder."
            };
        }

        var program = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(program) || !program.EndsWith("Jotdex.Server.exe", StringComparison.OrdinalIgnoreCase))
            program = "";

        var args = enable
            ? $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -HttpPort {httpPort}" +
              (httpsPort > 0 ? $" -HttpsPort {httpsPort}" : "") +
              (string.IsNullOrEmpty(program) ? "" : $" -ProgramPath \"{program}\"")
            : $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Disable";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return new FirewallEnsureResult
                {
                    Success = false,
                    ElevationDenied = true,
                    Message = FailMessage(httpPort, httpsPort, "Could not start elevated PowerShell.")
                };
            }

            if (!proc.WaitForExit(120_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new FirewallEnsureResult
                {
                    Success = false,
                    Message = FailMessage(httpPort, httpsPort, "Firewall helper timed out.")
                };
            }

            if (proc.ExitCode == 0)
            {
                _logger.LogInformation("Firewall LAN rules {Action} for ports HTTP={Http} HTTPS={Https}",
                    enable ? "ensured" : "removed", httpPort, httpsPort);
                return new FirewallEnsureResult
                {
                    Success = true,
                    Message = enable
                        ? $"Windows Firewall allow rules added for {DescribePorts(httpPort, httpsPort)}."
                        : "Jotdex LAN firewall rules removed (if they existed)."
                };
            }

            return new FirewallEnsureResult
            {
                Success = false,
                ExitCode = proc.ExitCode,
                Message = FailMessage(httpPort, httpsPort,
                    proc.ExitCode == 2
                        ? "Elevation did not grant Administrator rights."
                        : $"Firewall helper exited with code {proc.ExitCode}.")
            };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user clicked No on UAC
            _logger.LogInformation("User cancelled UAC for firewall rules");
            return new FirewallEnsureResult
            {
                Success = false,
                ElevationDenied = true,
                Message = FailMessage(httpPort, httpsPort, "UAC prompt was cancelled.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firewall ensure failed");
            return new FirewallEnsureResult
            {
                Success = false,
                Message = FailMessage(httpPort, httpsPort, ex.Message)
            };
        }
    }

    private string? ResolveScriptPath()
    {
        var names = new[] { "Ensure-JotdexFirewall.ps1" };
        var dirs = new List<string>();
        try
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var d = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(d)) dirs.Add(d);
            }
        }
        catch { /* ignore */ }

        if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
            dirs.Add(_env.ContentRootPath);
        dirs.Add(AppContext.BaseDirectory);

        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var name in names)
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return Path.GetFullPath(p);
            }
        }

        // Dev: repo scripts/
        try
        {
            var probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "Ensure-JotdexFirewall.ps1"));
            if (File.Exists(probe)) return probe;
            probe = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "scripts", "Ensure-JotdexFirewall.ps1"));
            if (File.Exists(probe)) return probe;
        }
        catch { /* ignore */ }

        return null;
    }

    private static string DescribePorts(int http, int https) =>
        https > 0 ? $"TCP {http} (HTTP) and {https} (HTTPS)" : $"TCP {http} (HTTP)";

    private static string FailMessage(int http, int https, string reason) =>
        $"{reason} LAN is still enabled — other PCs may be blocked until you allow {DescribePorts(http, https)} in Windows Firewall " +
        $"(or re-run Ensure-JotdexFirewall.ps1 as Administrator).";
}
