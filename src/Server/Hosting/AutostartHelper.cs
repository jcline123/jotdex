using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;

namespace Jotdex.Server.Hosting;

internal static class AutostartHelper
{
    private const string ShortcutName = "Jotdex Server.cmd";

    public static object GetStatus()
    {
        var startupDir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        var shortcut = Path.Combine(startupDir, ShortcutName);
        var (installed, name, status, startType) = GetServiceStatus();

        return new
        {
            platform = RuntimeInformation.OSDescription,
            userStartupFolder = startupDir,
            userStartupEnabled = File.Exists(shortcut),
            userStartupPath = File.Exists(shortcut) ? shortcut : null,
            windowsService = new
            {
                installed,
                name,
                status,
                startType
            },
            hint = installed
                ? "Windows Service is installed (starts automatically after reboot)."
                : "Enable “Start with Windows” for this user, or run install-service.ps1 as Administrator for a machine-wide service."
        };
    }

    public static (bool Success, string? Error, object? Status) SetUserStartupShortcut(bool enable)
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
                return (true, null, GetStatus());
            }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return (false, "Could not locate Jotdex.Server executable.", null);

            var workDir = Path.GetDirectoryName(exe)!;
            // Prefer network.json; do not hard-code --urls so LAN settings apply after reboot.
            var cmd = new StringBuilder();
            cmd.AppendLine("@echo off");
            cmd.Append("cd /d ").Append('"').Append(workDir).Append('"').AppendLine();
            cmd.Append("start \"\" ").Append('"').Append(exe).Append('"').AppendLine();
            File.WriteAllText(shortcut, cmd.ToString(), Encoding.ASCII);

            return (true, null, GetStatus());
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

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
