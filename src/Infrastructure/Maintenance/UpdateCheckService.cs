using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

public sealed class UpdateCheckResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string CurrentVersion { get; init; } = "";
    public string? LatestTag { get; init; }
    public string? LatestName { get; init; }
    public string? HtmlUrl { get; init; }
    public string? DownloadUrl { get; init; }
    public string? DownloadName { get; init; }
    public bool UpdateAvailable { get; init; }
    public string? Notes { get; init; }
    public string Repo { get; init; } = "jcline123/jotdex";
    public string? InstallPath { get; init; }
    public string? UpdateScriptPath { get; init; }
    public string BackupHoldPath { get; init; } = @"C:\JotdexBackupHold";
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}

public sealed class UpdateCheckService : IUpdateCheckService
{
    public const string DefaultRepo = "jcline123/jotdex";

    private readonly IAppVersion _version;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateCheckService> _logger;

    public UpdateCheckService(
        IAppVersion version,
        IHttpClientFactory httpClientFactory,
        ILogger<UpdateCheckService> logger)
    {
        _version = version;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var install = ResolveInstallPath();
        var script = install is null ? null : Path.Combine(install, "Update-Jotdex.ps1");
        if (script is not null && !File.Exists(script))
            script = null;

        var current = _version.Version;
        try
        {
            var client = _httpClientFactory.CreateClient("github");
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{DefaultRepo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("Jotdex-UpdateCheck");
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var res = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Success = true,
                    CurrentVersion = current,
                    UpdateAvailable = false,
                    Notes =
                        "No GitHub Releases published yet. When a release with a portable win-x64 zip is published, Check for updates will find it. " +
                        $"Repo: https://github.com/{DefaultRepo}/releases",
                    Repo = DefaultRepo,
                    HtmlUrl = $"https://github.com/{DefaultRepo}/releases",
                    InstallPath = install,
                    UpdateScriptPath = script
                };
            }

            if (!res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                _logger.LogWarning("GitHub release check failed: {Status} {Body}", res.StatusCode, body);
                return new UpdateCheckResult
                {
                    Success = false,
                    Error = $"GitHub returned {(int)res.StatusCode}. Try again later or open the Releases page.",
                    CurrentVersion = current,
                    Repo = DefaultRepo,
                    HtmlUrl = $"https://github.com/{DefaultRepo}/releases",
                    InstallPath = install,
                    UpdateScriptPath = script
                };
            }

            await using var stream = await res.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOpts, ct).ConfigureAwait(false);
            if (release is null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Error = "Could not parse GitHub release response.",
                    CurrentVersion = current,
                    Repo = DefaultRepo,
                    InstallPath = install,
                    UpdateScriptPath = script
                };
            }

            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) ||
                a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase) ||
                (a.Name.StartsWith("jotdex", StringComparison.OrdinalIgnoreCase) &&
                 a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)));

            var latestNorm = NormalizeVersion(release.TagName);
            var currentNorm = NormalizeVersion(current);
            var newer = IsNewer(latestNorm, currentNorm);

            string? notes;
            if (asset is null)
            {
                notes =
                    $"Release {release.TagName} exists but has no portable zip asset yet. Upload jotdex-win-x64.zip from artifacts\\win-x64 on the Release.";
                newer = false;
            }
            else if (!newer)
            {
                notes = $"You are on {current}. Latest release is {release.TagName}.";
            }
            else
            {
                notes =
                    $"Update available: {release.TagName}. Run Update-Jotdex.ps1 in your install folder (it backs up the program to C:\\JotdexBackupHold first).";
            }

            return new UpdateCheckResult
            {
                Success = true,
                CurrentVersion = current,
                LatestTag = release.TagName,
                LatestName = release.Name,
                HtmlUrl = release.HtmlUrl,
                DownloadUrl = asset?.BrowserDownloadUrl,
                DownloadName = asset?.Name,
                UpdateAvailable = newer && asset is not null,
                Notes = notes,
                Repo = DefaultRepo,
                InstallPath = install,
                UpdateScriptPath = script ?? (install is null ? null : Path.Combine(install, "Update-Jotdex.ps1"))
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            return new UpdateCheckResult
            {
                Success = false,
                Error = ex.Message,
                CurrentVersion = current,
                Repo = DefaultRepo,
                HtmlUrl = $"https://github.com/{DefaultRepo}/releases",
                InstallPath = install,
                UpdateScriptPath = script
            };
        }
    }

    private string? ResolveInstallPath()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
            if (string.IsNullOrWhiteSpace(dir)) return null;
            if (!File.Exists(Path.Combine(dir, "Jotdex.Server.exe"))) return null;
            var norm = dir.Replace('/', '\\');
            if (norm.Contains(@"\src\Server\", StringComparison.OrdinalIgnoreCase)) return null;
            return dir;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeVersion(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s[1..];
        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "0.0.0";
        return string.Join('.', parts.Take(3).Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n.ToString() : "0"));
    }

    private static bool IsNewer(string latest, string current)
    {
        if (!Version.TryParse(Pad(latest), out var l)) return false;
        if (!Version.TryParse(Pad(current), out var c)) return true;
        return l > c;
    }

    private static string Pad(string v)
    {
        var parts = v.Split('.');
        while (parts.Length < 3) parts = parts.Append("0").ToArray();
        return string.Join('.', parts.Take(3));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAsset>? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
