using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

namespace Jotdex.Server.Hosting;

internal static class AutostartHelper
{
    private const string ShortcutName = "Jotdex Server.cmd";

    public static object GetStatus(string? contentRootPath = null)
    {
        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcut = Path.Combine(startupDir, ShortcutName);
        var (installed, name, status, startType) = GetServiceStatus();
        var mode = DetectLaunchMode(contentRootPath);

        return new
        {
            platform = RuntimeInformation.OSDescription,
            userStartupFolder = startupDir,
            userStartupEnabled = File.Exists(shortcut),
            userStartupPath = File.Exists(shortcut) ? shortcut : null,
            launchMode = mode.Kind,
            launchHint = mode.Hint,
            windowsService = new
            {
                installed,
                name,
                status,
                startType
            },
            hint = installed
                ? "Windows Service is installed (starts automatically after reboot)."
                : mode.Kind == "dotnet-run"
                    ? "Start with Windows will run `dotnet run` from the project folder (Dev) so the web UI loads after reboot."
                    : "Enable “Start with Windows” for this user, or run install-service.ps1 as Administrator for a machine-wide service."
        };
    }

    public static (bool Success, string? Error, object? Status) SetUserStartupShortcut(bool enable, string? contentRootPath = null)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return (false, "Autostart shortcuts are only supported on Windows.", null);

            var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            Directory.CreateDirectory(startupDir);
            var shortcut = Path.Combine(startupDir, ShortcutName);

            if (!enable)
            {
                if (File.Exists(shortcut)) File.Delete(shortcut);
                return (true, null, GetStatus(contentRootPath));
            }

            var mode = DetectLaunchMode(contentRootPath);
            if (mode.Kind == "none")
                return (false, mode.Hint, null);

            File.WriteAllText(shortcut, mode.CmdText!, Encoding.ASCII);
            return (true, null, GetStatus(contentRootPath));
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    private static (string Kind, string? CmdText, string Hint) DetectLaunchMode(string? contentRootPath)
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
        {
            var workDir = Path.GetDirectoryName(exe)!;
            if (HasWwwroot(workDir))
            {
                var cmd = new StringBuilder();
                cmd.AppendLine("@echo off");
                cmd.Append("cd /d ").Append('"').Append(workDir).Append('"').AppendLine();
                // Prefer network.json; do not hard-code --urls so LAN settings apply after reboot.
                cmd.Append("start \"\" ").Append('"').Append(exe).Append('"').AppendLine();
                return ("portable-exe", cmd.ToString(), "Startup launches this portable Jotdex.Server.exe.");
            }
        }

        // Dev / framework build: ProcessPath has no wwwroot. Prefer content root (dotnet run project dir).
        var projectDir = ResolveDevProjectDir(contentRootPath);
        if (projectDir is not null)
        {
            var cmd = new StringBuilder();
            cmd.AppendLine("@echo off");
            cmd.Append("cd /d ").Append('"').Append(projectDir).Append('"').AppendLine();
            cmd.AppendLine("set ASPNETCORE_ENVIRONMENT=Development");
            cmd.AppendLine("start \"\" /min dotnet run --no-launch-profile");
            return (
                "dotnet-run",
                cmd.ToString(),
                "Startup will run `dotnet run` from the Server project (same as Dev). Requires .NET SDK on PATH.");
        }

        return (
            "none",
            null,
            "Cannot enable Start with Windows: no wwwroot beside the exe and no Dev project folder found. Use a portable build (with wwwroot) or run from the repo with src\\Server\\wwwroot present.");
    }

    private static string? ResolveDevProjectDir(string? contentRootPath)
    {
        foreach (var candidate in CandidateProjectDirs(contentRootPath))
        {
            if (HasWwwroot(candidate) &&
                File.Exists(Path.Combine(candidate, "Jotdex.Server.csproj")))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    private static IEnumerable<string> CandidateProjectDirs(string? contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(contentRootPath))
            yield return contentRootPath;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) yield break;
        var dir = Path.GetDirectoryName(exe);
        // bin/Release/netX.0 → climb to src/Server
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return dir;
            dir = Path.GetDirectoryName(dir);
        }
    }

    private static bool HasWwwroot(string dir) =>
        File.Exists(Path.Combine(dir, "wwwroot", "index.html"));

    private static (bool installed, string? name, string? status, string? startType) GetServiceStatus()
    {
        if (!OperatingSystem.IsWindows()) return (false, null, null, null);
        try
        {
#pragma warning disable CA1416
            using var sc = new ServiceController("Jotdex");
            return (true, sc.ServiceName, sc.Status.ToString(), sc.StartType.ToString());
#pragma warning restore CA1416
        }
        catch
        {
            return (false, null, null, null);
        }
    }
}
