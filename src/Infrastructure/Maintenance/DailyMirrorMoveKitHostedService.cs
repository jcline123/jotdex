using Jotdex.Infrastructure.Config;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

/// <summary>
/// About once a day (when mirror is enabled), write one move-kit archive into the mirror folder.
/// Prefer .jotdexkit (password-encrypted) when a Jotdex password is set.
/// </summary>
public sealed class DailyMirrorMoveKitHostedService : BackgroundService
{
    private readonly IVaultMirrorService _mirror;
    private readonly IMoveKitService _moveKit;
    private readonly ILogger<DailyMirrorMoveKitHostedService> _logger;

    public DailyMirrorMoveKitHostedService(
        IVaultMirrorService mirror,
        IMoveKitService moveKit,
        ILogger<DailyMirrorMoveKitHostedService> logger)
    {
        _mirror = mirror;
        _moveKit = moveKit;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryPublishAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Daily mirror move-kit check failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task TryPublishAsync(CancellationToken ct)
    {
        var settings = _mirror.GetSettings();
        if (!settings.Enabled || !settings.IncludeDailyMoveKit)
            return;
        if (string.IsNullOrWhiteSpace(settings.DestinationPath))
            return;

        var last = settings.LastMoveKitUtc;
        if (last is not null && last > DateTimeOffset.UtcNow.AddHours(-20))
            return; // already published in the last ~day

        var destRoot = Path.Combine(settings.DestinationPath.Trim(), "jotdex-move-kits");
        Directory.CreateDirectory(destRoot);

        var result = await _moveKit.CreateAsync(
            includeAuth: true,
            includeHistory: true,
            passwordForInit: null,
            outputDirectory: destRoot,
            ct: ct).ConfigureAwait(false);

        if (!result.Success || string.IsNullOrWhiteSpace(result.BundlePath))
        {
            _logger.LogWarning("Daily move kit for mirror failed: {Error}", result.Error);
            return;
        }

        // Stable latest name for easy restore + keep dated copy
        var ext = Path.GetExtension(result.BundlePath);
        var latest = Path.Combine(destRoot, "jotdex-move-latest" + ext);
        try
        {
            File.Copy(result.BundlePath, latest, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write jotdex-move-latest");
        }

        CopyRecoveryHelpers(destRoot);

        PruneOld(destRoot, keep: 7);
        _mirror.RecordMoveKitPublished(DateTimeOffset.UtcNow);
        _logger.LogInformation("Daily move kit published to mirror: {Path}", result.BundlePath);
    }

    private void CopyRecoveryHelpers(string destRoot)
    {
        try
        {
            var readme = """
                Jotdex recovery kit (inside your cloud mirror)
                =============================================

                The mirror still copies your whole vault as normal Markdown files.
                This folder adds one encrypted (or zip) move kit for full recovery
                (settings, password, history, optional app).

                On a new PC:
                1. Copy jotdex-move-latest.jotdexkit (or .zip) AND Restore-Jotdex.ps1
                   to a folder that also has Jotdex.Server.exe
                   (from your portable install, or extract a fresh portable build).
                2. Right-click Restore-Jotdex.ps1 → Run with PowerShell
                3. If asked, enter your Jotdex unlock password (decrypts the kit).
                4. Choose install folder + local vault folder — done.

                You do NOT need a separate decrypt command.
                """;
            File.WriteAllText(Path.Combine(destRoot, "README-RECOVERY.txt"), readme);

            foreach (var name in new[] { "Restore-Jotdex.ps1", "Decrypt-JotdexKit.ps1" })
            {
                var src = ResolveHelper(name);
                if (src is not null)
                    File.Copy(src, Path.Combine(destRoot, name), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not copy recovery helpers into mirror kits folder");
        }
    }

    private static string? ResolveHelper(string fileName)
    {
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

        dirs.Add(AppContext.BaseDirectory);
        foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var p = Path.Combine(dir, fileName);
            if (File.Exists(p)) return p;
        }

        // Dev: scripts next to repo
        try
        {
            var probe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", fileName));
            if (File.Exists(probe)) return probe;
        }
        catch { /* ignore */ }

        return null;
    }

    private static void PruneOld(string dir, int keep)
    {
        try
        {
            var files = Directory.GetFiles(dir, "jotdex-move-*")
                .Where(f => !Path.GetFileName(f).StartsWith("jotdex-move-latest", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(keep)
                .ToList();
            foreach (var f in files)
            {
                try { f.Delete(); } catch { /* ignore */ }
            }
        }
        catch { /* ignore */ }
    }
}
