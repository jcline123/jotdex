using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.CloudBackup;

/// <summary>
/// Optional Development helper: load OAuth client IDs from a gitignored JSON file into process env
/// when the corresponding env vars are not already set. Real cloud OAuth still needs valid app keys.
/// </summary>
public static class CloudBackupLocalOAuthEnv
{
    public const string FileName = "cloud-oauth.local.json";

    /// <summary>
    /// Looks under <paramref name="dataRoot"/>/config and the content root for <see cref="FileName"/>.
    /// </summary>
    public static void TryApply(string? dataRoot, string? contentRoot, ILogger? logger = null)
    {
        foreach (var candidate in CandidatePaths(dataRoot, contentRoot))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidate));
                var applied = 0;
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (!prop.Name.StartsWith("JOTDEX_CLOUD_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(prop.Name)))
                        continue;
                    var value = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    Environment.SetEnvironmentVariable(prop.Name, value.Trim());
                    applied++;
                }

                logger?.LogInformation("Applied {Count} cloud OAuth env value(s) from {Path}", applied, candidate);
                return;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not load cloud OAuth local file {Path}", candidate);
            }
        }
    }

    private static IEnumerable<string> CandidatePaths(string? dataRoot, string? contentRoot)
    {
        if (!string.IsNullOrWhiteSpace(dataRoot))
            yield return Path.Combine(dataRoot, "config", FileName);
        if (!string.IsNullOrWhiteSpace(contentRoot))
            yield return Path.Combine(contentRoot, FileName);
    }
}
