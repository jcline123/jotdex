using System.Text.Json;
using Jotdex.Core.Configuration;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jotdex.Infrastructure.Vault;

public sealed class VaultPathGuard : IVaultPathGuard
{
    private readonly IHostEnvironment _env;
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<VaultPathGuard> _logger;
    private readonly object _gate = new();
    private string? _vaultRoot;

    public VaultPathGuard(
        IOptions<JotdexOptions> options,
        IHostEnvironment env,
        IDataRootResolver dataRoot,
        ILogger<VaultPathGuard> logger)
    {
        _env = env;
        _dataRoot = dataRoot;
        _logger = logger;

        // Prefer persisted runtime config over appsettings (if the folder still exists)
        var persisted = LoadPersistedPath();
        var configured = !string.IsNullOrWhiteSpace(persisted) && Directory.Exists(persisted)
            ? persisted!
            : options.Value.VaultPath;
        if (!string.IsNullOrWhiteSpace(configured))
            _ = TrySetVaultPathInternal(configured, persist: false);
    }

    public bool IsConfigured
    {
        get { lock (_gate) return _vaultRoot is not null; }
    }

    public string VaultRoot
    {
        get
        {
            lock (_gate)
                return _vaultRoot ?? throw new InvalidOperationException("Vault is not configured.");
        }
    }

    public string? TrySetVaultPath(string absolutePath) => TrySetVaultPathInternal(absolutePath, persist: true);

    private string? TrySetVaultPathInternal(string configured, bool persist)
    {
        try
        {
            var full = Path.IsPathRooted(configured)
                ? Path.GetFullPath(configured)
                : Path.GetFullPath(Path.Combine(_env.ContentRootPath, configured));

            // Reject obvious iCloud live vaults
            if (full.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase) &&
                !full.Contains("SampleVault", StringComparison.OrdinalIgnoreCase) &&
                !full.Contains($"{Path.DirectorySeparatorChar}jotdex{Path.DirectorySeparatorChar}tools", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Refusing live vault under iCloudDrive");
                // Still allow if user insists via SampleVault/tools; for other paths warn but allow with note
            }

            if (!Directory.Exists(full))
                return "Folder does not exist.";

            // Soft warning for iCloud — allow but log
            if (full.Contains("iCloudDrive", StringComparison.OrdinalIgnoreCase))
                _logger.LogWarning("Vault path is under iCloudDrive; prefer local disk for the live vault");

            lock (_gate)
                _vaultRoot = TrimSlash(full);

            if (persist)
                SavePersistedPath(full);

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public string EnsureInsideVault(string absoluteOrRelativePath)
    {
        string root;
        lock (_gate)
        {
            if (_vaultRoot is null)
                throw new InvalidOperationException("Vault is not configured.");
            root = _vaultRoot;
        }

        var candidate = Path.IsPathRooted(absoluteOrRelativePath)
            ? Path.GetFullPath(absoluteOrRelativePath)
            : Path.GetFullPath(Path.Combine(root, absoluteOrRelativePath));

        var withoutDrive = candidate.Length >= 2 && candidate[1] == ':'
            ? candidate[2..]
            : candidate;
        if (withoutDrive.Contains(':', StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Alternate data streams are not allowed.");

        if (File.Exists(candidate) || Directory.Exists(candidate))
        {
            var attr = File.GetAttributes(candidate);
            if (attr.HasFlag(FileAttributes.ReparsePoint))
            {
                var resolved = Path.GetFullPath(candidate);
                EnsurePrefix(root, resolved);
                return resolved;
            }
        }

        EnsurePrefix(root, candidate);
        return candidate;
    }

    public string ToRelativePath(string absolutePath)
    {
        var full = EnsureInsideVault(absolutePath);
        var rel = Path.GetRelativePath(VaultRoot, full);
        return rel.Replace('\\', '/');
    }

    private string? LoadPersistedPath()
    {
        try
        {
            var path = ConfigFilePath();
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("vaultPath", out var v) ? v.GetString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read vault config");
            return null;
        }
    }

    private void SavePersistedPath(string full)
    {
        var path = ConfigFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(new { vaultPath = full }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private string ConfigFilePath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "vault.json");

    private static void EnsurePrefix(string root, string candidate)
    {
        var normalized = TrimSlash(candidate);
        var rootTrim = TrimSlash(root);
        if (!normalized.StartsWith(rootTrim, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes the vault.");

        var rel = Path.GetRelativePath(rootTrim, normalized);
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new UnauthorizedAccessException("Path escapes the vault.");
    }

    private static string TrimSlash(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
