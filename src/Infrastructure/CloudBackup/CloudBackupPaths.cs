using Jotdex.Core.Configuration;

namespace Jotdex.Infrastructure.CloudBackup;

public static class CloudBackupPaths
{
    public const string StagingFolderName = "cloud-backup-staging";

    public static string StagingRoot(IDataRootResolver dataRoot) =>
        Path.Combine(dataRoot.ResolveDataRoot(), "exports", StagingFolderName);

    public static string StagingRoot(string dataRoot) =>
        Path.Combine(dataRoot, "exports", StagingFolderName);

    public static string RunRoot(IDataRootResolver dataRoot, string runId) =>
        Path.Combine(StagingRoot(dataRoot), Sanitize(runId));

    public static string SnapshotVault(IDataRootResolver dataRoot, string runId) =>
        Path.Combine(RunRoot(dataRoot, runId), "snapshot", "vault");

    public static string ArtifactsDirectory(IDataRootResolver dataRoot, string runId) =>
        Path.Combine(RunRoot(dataRoot, runId), "artifacts");

    public static bool IsUnderStaging(string dataRoot, string candidatePath)
    {
        var staging = Path.GetFullPath(StagingRoot(dataRoot));
        var full = Path.GetFullPath(candidatePath);
        return full.StartsWith(TrimSlash(staging) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(full, TrimSlash(staging), StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string runId)
    {
        var s = (runId ?? "").Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? Guid.NewGuid().ToString("N") : s;
    }

    private static string TrimSlash(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
