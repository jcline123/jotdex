namespace Jotdex.Infrastructure.CloudBackup;

public static class CloudBackupRemoteNaming
{
    public static string ShortRunId(string runId)
    {
        var hex = (runId ?? "").Replace("-", "", StringComparison.Ordinal);
        if (hex.Length >= 8)
            return hex[..8].ToLowerInvariant();
        return Guid.NewGuid().ToString("N")[..8];
    }

    public static string UtcStamp(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

    public static string MoveKitFileName(DateTimeOffset createdUtc, string runId) =>
        $"jotdex-move-{UtcStamp(createdUtc)}-{ShortRunId(runId)}.jotdexkit";

    public static string VaultZipFileName(DateTimeOffset createdUtc, string runId) =>
        $"jotdex-vault-{UtcStamp(createdUtc)}-{ShortRunId(runId)}.zip";

    public static string GenerationManifestFileName(DateTimeOffset createdUtc, string runId) =>
        $"jotdex-backup-{UtcStamp(createdUtc)}-{ShortRunId(runId)}.manifest.json";

    /// <summary>Extract short run id from a known cloud-backup artifact name, or null.</summary>
    public static string? TryParseShortRunId(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var name = Path.GetFileName(fileName);
        // jotdex-move-20260812T143500Z-a1b2c3d4.jotdexkit
        var parts = name.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4) return null;
        var last = parts[^1];
        var dot = last.IndexOf('.');
        var id = dot > 0 ? last[..dot] : last;
        return id.Length == 8 ? id.ToLowerInvariant() : null;
    }

    public static bool IsMoveKitName(string fileName) =>
        Path.GetFileName(fileName).StartsWith("jotdex-move-", StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(".jotdexkit", StringComparison.OrdinalIgnoreCase);

    public static bool IsVaultZipName(string fileName) =>
        Path.GetFileName(fileName).StartsWith("jotdex-vault-", StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public static bool IsGenerationManifestName(string fileName) =>
        Path.GetFileName(fileName).StartsWith("jotdex-backup-", StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase);
}
