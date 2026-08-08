using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Secrets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class MoveKitResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? BundlePath { get; init; }
    public long Bytes { get; init; }
    public bool IncludedApp { get; init; }
    public bool IncludedAuth { get; init; }
    public bool IncludedHistory { get; init; }
    public bool Encrypted { get; init; }
    public string? Hint { get; init; }
}

public interface IMoveKitService
{
    Task<MoveKitResult> CreateAsync(
        bool includeAuth = true,
        bool includeHistory = true,
        string? passwordForInit = null,
        string? outputDirectory = null,
        CancellationToken ct = default);
}

/// <summary>
/// ZIP for moving to another PC: vault + appdata + portable app (when available) + restore script.
/// </summary>
public sealed class MoveKitService : IMoveKitService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly IVaultPathGuard _paths;
    private readonly IHostEnvironment _env;
    private readonly ISecretStore _secrets;
    private readonly IMoveKitCryptoService _crypto;
    private readonly ILogger<MoveKitService> _logger;

    public MoveKitService(
        IDataRootResolver dataRoot,
        IVaultPathGuard paths,
        IHostEnvironment env,
        ISecretStore secrets,
        IMoveKitCryptoService crypto,
        ILogger<MoveKitService> logger)
    {
        _dataRoot = dataRoot;
        _paths = paths;
        _env = env;
        _secrets = secrets;
        _crypto = crypto;
        _logger = logger;
    }

    public async Task<MoveKitResult> CreateAsync(
        bool includeAuth = true,
        bool includeHistory = true,
        string? passwordForInit = null,
        string? outputDirectory = null,
        CancellationToken ct = default)
    {
        if (!_paths.IsConfigured)
            return new MoveKitResult { Success = false, Error = "Vault not configured." };

        var vault = _paths.VaultRoot;
        if (!Directory.Exists(vault))
            return new MoveKitResult { Success = false, Error = "Vault folder missing." };

        if (_crypto.IsPasswordProtectionEnabled)
        {
            if (!_crypto.HasEncryptionKey)
            {
                if (string.IsNullOrEmpty(passwordForInit))
                {
                    return new MoveKitResult
                    {
                        Success = false,
                        Error =
                            "A Jotdex password is set, but move-kit encryption is not initialized yet. " +
                            "Re-enter your password in the Create move kit dialog (or change/re-save password in Security)."
                    };
                }
                try { _crypto.EnsureInitialized(passwordForInit); }
                catch (Exception ex)
                {
                    return new MoveKitResult { Success = false, Error = "Could not init encryption: " + ex.Message };
                }
            }
        }

        var dataRoot = _dataRoot.ResolveDataRoot();
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var outDir = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.Combine(dataRoot, "exports", "backups")
            : outputDirectory;
        Directory.CreateDirectory(outDir);
        var zipPath = Path.Combine(outDir, $"jotdex-move-{stamp}.zip");

        var appDir = ResolvePortableAppDir();
        var includedApp = appDir is not null;

        try
        {
            await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

                AddDirectory(zip, vault, "vault", ct, skipRelative: null);

                var configDir = Path.Combine(dataRoot, "config");
                if (Directory.Exists(configDir))
                    AddDirectory(zip, configDir, "appdata/config", ct, skipRelative: null);

                if (includeAuth)
                {
                    var authDir = Path.Combine(dataRoot, "auth");
                    if (Directory.Exists(authDir))
                        AddDirectory(zip, authDir, "appdata/auth", ct, skipRelative: null);
                }

                if (includeHistory)
                {
                    var histDir = Path.Combine(dataRoot, "history");
                    if (Directory.Exists(histDir))
                        AddDirectory(zip, histDir, "appdata/history", ct, skipRelative: null);
                }

                PortableSecretsZip.AddToZip(zip, _secrets);

                if (appDir is not null)
                {
                    AddDirectory(zip, appDir, "app", ct, skipRelative: ShouldSkipAppRelative);
                }

                AddTextEntry(zip, "Restore-Jotdex.ps1", LoadRestoreScript());
                AddTextEntry(zip, "README-MOVE.txt", BuildReadme(includedApp, encrypt: _crypto.IsPasswordProtectionEnabled));

                var manifest = new
                {
                    kind = "jotdex-move-kit",
                    createdUtc = DateTimeOffset.UtcNow,
                    vaultPath = vault,
                    includeAuth,
                    includeHistory,
                    includedApp,
                    encrypted = _crypto.IsPasswordProtectionEnabled,
                    appSource = appDir,
                    note = "Unzip (decrypt .jotdexkit with your Jotdex password first if encrypted), run Restore-Jotdex.ps1."
                };
                AddTextEntry(zip, "MANIFEST.json",
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }, ct).ConfigureAwait(false);

            var finalPath = zipPath;
            var encrypted = false;
            if (_crypto.IsPasswordProtectionEnabled && _crypto.HasEncryptionKey)
            {
                finalPath = _crypto.EncryptZipFile(zipPath);
                encrypted = true;
            }

            var bytes = new FileInfo(finalPath).Length;
            _logger.LogInformation("Move kit created: {Path} ({Bytes} bytes, app={App}, enc={Enc})", finalPath, bytes, includedApp, encrypted);

            string? hint = null;
            if (encrypted)
                hint = "Encrypted with your Jotdex unlock password (.jotdexkit). Decrypt before Restore-Jotdex.ps1.";
            else if (!includedApp)
            {
                hint =
                    "This kit includes your vault and app data, but not Jotdex.Server.exe (you are not running the portable build). " +
                    "On the new PC you will need a portable build, or create the kit from the portable exe / run scripts\\create-move-kit.ps1.";
            }

            return new MoveKitResult
            {
                Success = true,
                BundlePath = finalPath,
                Bytes = bytes,
                IncludedApp = includedApp,
                IncludedAuth = includeAuth,
                IncludedHistory = includeHistory,
                Encrypted = encrypted,
                Hint = hint
            };
        }
        catch (Exception ex)
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { /* ignore */ }
            _logger.LogWarning(ex, "Move kit failed");
            return new MoveKitResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>Directory containing the portable Jotdex.Server.exe, or null when running under dotnet/SDK.</summary>
    internal string? ResolvePortableAppDir()
    {
        var candidates = new List<string>();

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
            if (!string.IsNullOrWhiteSpace(dir))
                candidates.Add(dir);
        }

        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (!string.IsNullOrWhiteSpace(baseDir))
                candidates.Add(Path.GetFullPath(baseDir));
        }
        catch { /* ignore */ }

        if (!string.IsNullOrWhiteSpace(_env.ContentRootPath))
            candidates.Add(Path.GetFullPath(_env.ContentRootPath));

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (LooksLikePortableAppDir(dir))
                return dir;
        }

        return null;
    }

    private static bool LooksLikePortableAppDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            return false;

        // Never pack a source/build tree.
        var norm = dir.Replace('/', '\\');
        if (norm.Contains(@"\src\Server\", StringComparison.OrdinalIgnoreCase))
            return false;
        if (norm.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase))
        {
            // artifacts\win-x64 may not use bin\, but publish output under Server\bin\Release\net*\win-x64 can
            // — only accept if start-portable.cmd is present (copied by publish script).
            if (!File.Exists(Path.Combine(dir, "start-portable.cmd")))
                return false;
        }

        var exeName = "Jotdex.Server.exe";
        var hasExe = File.Exists(Path.Combine(dir, exeName));
        var hasStart = File.Exists(Path.Combine(dir, "start-portable.cmd"));
        return hasExe && hasStart;
    }

    private static bool ShouldSkipAppRelative(string relativeUnix)
    {
        // Keep the kit from nesting live app data / previous exports inside app/
        if (relativeUnix.Equals("data", StringComparison.OrdinalIgnoreCase) ||
            relativeUnix.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (relativeUnix.StartsWith("exports/", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static void AddDirectory(
        ZipArchive zip,
        string sourceDir,
        string entryPrefix,
        CancellationToken ct,
        Func<string, bool>? skipRelative)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(sourceDir, file).Replace('\\', '/');
            if (rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (skipRelative is not null && skipRelative(rel))
                continue;

            zip.CreateEntryFromFile(file, $"{entryPrefix}/{rel}", CompressionLevel.Optimal);
        }
    }

    private static void AddTextEntry(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        w.Write(content);
    }

    private string LoadRestoreScript()
    {
        foreach (var path in EnumerateRestoreScriptCandidates())
        {
            try
            {
                if (File.Exists(path))
                    return File.ReadAllText(path);
            }
            catch { /* try next */ }
        }

        return EmbeddedRestoreScriptFallback;
    }

    private IEnumerable<string> EnumerateRestoreScriptCandidates()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            var dir = Path.GetDirectoryName(processPath);
            if (!string.IsNullOrWhiteSpace(dir))
                yield return Path.Combine(dir, "Restore-Jotdex.ps1");
        }

        yield return Path.Combine(AppContext.BaseDirectory, "Restore-Jotdex.ps1");
        yield return Path.Combine(_env.ContentRootPath, "Restore-Jotdex.ps1");

        // Dev: repo scripts/ next to src/Server
        var content = _env.ContentRootPath;
        if (!string.IsNullOrWhiteSpace(content))
        {
            yield return Path.GetFullPath(Path.Combine(content, "..", "..", "scripts", "Restore-Jotdex.ps1"));
            yield return Path.GetFullPath(Path.Combine(content, "..", "scripts", "Restore-Jotdex.ps1"));
        }

        var asm = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(asm))
        {
            var asmDir = Path.GetDirectoryName(asm);
            if (!string.IsNullOrWhiteSpace(asmDir))
                yield return Path.Combine(asmDir, "Restore-Jotdex.ps1");
        }
    }

    private static string BuildReadme(bool includedApp, bool encrypt) =>
        $$"""
        Jotdex move kit
        ===============

        Simple restore
        --------------
        1. Put this kit (.jotdexkit or .zip) in a folder with Restore-Jotdex.ps1 and Jotdex.Server.exe
           (portable install folder is fine).
        2. Right-click Restore-Jotdex.ps1 → Run with PowerShell
        3. If the kit is encrypted, type your Jotdex unlock password when asked.
        4. Choose install folder + local vault folder.

        No separate decrypt step — Restore does it for you.

        {{(encrypt ? "This build produced an encrypted .jotdexkit (safer in cloud mirrors).\n" : "")}}
        Contents (inside the archive)
        -----------------------------
        - vault\          Your notes
        - appdata\        Password, settings, history, portable secrets
        - app\            Portable program {{(includedApp ? "(included)" : "(not included)")}}
        - Restore-Jotdex.ps1
        """;

    /// <summary>Minimal fallback if scripts/Restore-Jotdex.ps1 is not on disk.</summary>
    private const string EmbeddedRestoreScriptFallback =
        """
        #Requires -Version 5.1
        $ErrorActionPreference = "Stop"
        Write-Host "Restore-Jotdex.ps1 was missing from the build; re-create the move kit from a current Jotdex release." -ForegroundColor Red
        exit 1
        """;
}
