using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Net;

public sealed class PageFetchResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? TextExcerpt { get; init; }
    public string? FinalUrl { get; init; }
}

/// <summary>Fetch public HTML pages for From URL / clip enrichment (SSRF-safe).</summary>
public sealed class SafeRemotePageClient
{
    public const int MaxRedirects = 5;
    public const long MaxBytes = 2L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly ILogger<SafeRemotePageClient> _logger;

    public SafeRemotePageClient(ILogger<SafeRemotePageClient> logger) => _logger = logger;

    public async Task<PageFetchResult> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return Fail("Invalid URL");

        var gate = await SafeRemoteImageClient.ValidateUriAsync(uri, ct);
        if (gate is not null) return Fail(gate);

        // Prefer curl on Windows: many CDNs (Cloudflare) challenge .NET HttpClient TLS fingerprints.
        if (OperatingSystem.IsWindows() && IsCurlAvailable())
        {
            var viaCurl = await FetchViaCurlAsync(uri, ct);
            if (viaCurl.Success && !LooksLikeChallenge(viaCurl.Title, viaCurl.TextExcerpt))
                return viaCurl;
            if (viaCurl.Success)
                _logger.LogInformation("curl returned a challenge page; trying HttpClient");
            else
                _logger.LogInformation("curl fetch failed ({Error}); trying HttpClient", viaCurl.Error);
        }

        return await FetchViaHttpClientAsync(uri, ct);
    }

    private async Task<PageFetchResult> FetchViaHttpClientAsync(Uri start, CancellationToken ct)
    {
        var uri = start;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var gate = await SafeRemoteImageClient.ValidateUriAsync(uri, ct);
            if (gate is not null) return Fail(gate);

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = Timeout
            };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyBrowserHeaders(req);

            HttpResponseMessage res;
            try
            {
                res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote page fetch failed");
                return Fail("Could not download the page");
            }

            using (res)
            {
                if ((int)res.StatusCode is >= 300 and < 400)
                {
                    var loc = res.Headers.Location;
                    if (loc is null) return Fail("Redirect without Location");
                    uri = loc.IsAbsoluteUri ? loc : new Uri(uri, loc);
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                    return Fail($"HTTP {(int)res.StatusCode}");

                var media = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!string.IsNullOrEmpty(media) &&
                    !media.Contains("html", StringComparison.OrdinalIgnoreCase) &&
                    !media.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
                    !media.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                    return Fail($"Not an HTML page ({media})");

                var len = res.Content.Headers.ContentLength;
                if (len is > MaxBytes) return Fail("Page too large");

                var bytes = await ReadCappedAsync(await res.Content.ReadAsStreamAsync(ct), ct);
                if (bytes is null) return Fail("Page too large");
                if (bytes.Length == 0) return Fail("Empty response");

                var charset = res.Content.Headers.ContentType?.CharSet;
                var html = DecodeBytes(bytes, charset);
                var parsed = PageHtmlParser.Parse(html);
                if (LooksLikeChallenge(parsed.Title, parsed.Excerpt))
                    return Fail("Site blocked automated fetch (try the bookmarklet from that page)");

                return new PageFetchResult
                {
                    Success = true,
                    Title = parsed.Title,
                    Description = parsed.Description,
                    TextExcerpt = parsed.Excerpt,
                    FinalUrl = uri.ToString()
                };
            }
        }

        return Fail("Too many redirects");
    }

    private async Task<PageFetchResult> FetchViaCurlAsync(Uri uri, CancellationToken ct)
    {
        // Re-validate final URL host before spawning curl (SSRF).
        var gate = await SafeRemoteImageClient.ValidateUriAsync(uri, ct);
        if (gate is not null) return Fail(gate);

        var tmp = Path.Combine(Path.GetTempPath(), "jotdex-page-" + Guid.NewGuid().ToString("N") + ".html");
        try
        {
            var args =
                $"-sL --max-time 20 --max-redirs {MaxRedirects} --max-filesize {MaxBytes} " +
                "-A \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36\" " +
                "-H \"Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8\" " +
                "-H \"Accept-Language: en-US,en;q=0.9\" " +
                $"-o \"{tmp}\" -w \"%{{url_effective}}\" \"{uri}\"";

            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "curl.exe",
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!proc.Start()) return Fail("Could not start curl");

            var finalUrlTask = proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            var finalUrl = (await finalUrlTask).Trim();
            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                _logger.LogWarning("curl exit {Code}: {Err}", proc.ExitCode, err.Trim());
                return Fail("Could not download the page");
            }

            if (!string.IsNullOrWhiteSpace(finalUrl) && Uri.TryCreate(finalUrl, UriKind.Absolute, out var finalUri))
            {
                var finalGate = await SafeRemoteImageClient.ValidateUriAsync(finalUri, ct);
                if (finalGate is not null) return Fail(finalGate);
            }
            else
            {
                finalUrl = uri.ToString();
            }

            if (!File.Exists(tmp)) return Fail("Empty response");
            var fi = new FileInfo(tmp);
            if (fi.Length == 0) return Fail("Empty response");
            if (fi.Length > MaxBytes) return Fail("Page too large");

            var html = await File.ReadAllTextAsync(tmp, Encoding.UTF8, ct);
            var parsed = PageHtmlParser.Parse(html);
            if (LooksLikeChallenge(parsed.Title, parsed.Excerpt))
                return Fail("Site blocked automated fetch (try the bookmarklet from that page)");

            return new PageFetchResult
            {
                Success = true,
                Title = parsed.Title,
                Description = parsed.Description,
                TextExcerpt = parsed.Excerpt,
                FinalUrl = finalUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "curl page fetch failed");
            return Fail("Could not download the page");
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static void ApplyBrowserHeaders(HttpRequestMessage req)
    {
        req.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
        req.Headers.TryAddWithoutValidation(
            "Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
        req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
        req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
        req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
    }

    private static bool LooksLikeChallenge(string? title, string? excerpt)
    {
        var t = title ?? "";
        if (t.Contains("Just a moment", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.Contains("Attention Required", StringComparison.OrdinalIgnoreCase)) return true;
        var e = excerpt ?? "";
        return e.Contains("Enable JavaScript and cookies to continue", StringComparison.OrdinalIgnoreCase)
               || e.Contains("cf-browser-verification", StringComparison.OrdinalIgnoreCase);
    }

    private static bool? _curlAvailable;
    private static bool IsCurlAvailable()
    {
        if (_curlAvailable is bool known) return known;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "curl.exe",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(3000);
            _curlAvailable = p is { ExitCode: 0 };
        }
        catch
        {
            _curlAvailable = false;
        }

        return _curlAvailable == true;
    }

    private static async Task<byte[]?> ReadCappedAsync(Stream stream, CancellationToken ct)
    {
        await using (stream)
        {
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                total += read;
                if (total > MaxBytes) return null;
                ms.Write(buffer, 0, read);
            }

            return ms.ToArray();
        }
    }

    private static string DecodeBytes(byte[] bytes, string? charset)
    {
        try
        {
            var enc = !string.IsNullOrWhiteSpace(charset)
                ? Encoding.GetEncoding(charset.Trim().Trim('"'))
                : Encoding.UTF8;
            return enc.GetString(bytes);
        }
        catch
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    private static PageFetchResult Fail(string error) => new() { Success = false, Error = error };
}

public static class PageHtmlParser
{
    public static (string? Title, string? Description, string? Excerpt) Parse(string html)
    {
        var title =
            MetaContent(html, "og:title")
            ?? MetaContent(html, "twitter:title")
            ?? TagInner(html, "title");

        var description =
            MetaContent(html, "og:description")
            ?? MetaContent(html, "twitter:description")
            ?? MetaName(html, "description");

        var excerpt = BuildExcerpt(html, maxChars: 3500);
        if (string.IsNullOrWhiteSpace(excerpt)) excerpt = null;

        title = CleanWs(Decode(title));
        description = CleanWs(Decode(description));
        excerpt = CleanWs(Decode(excerpt));

        if (title is { Length: > 200 }) title = title[..200].TrimEnd() + "…";
        if (description is { Length: > 500 }) description = description[..500].TrimEnd() + "…";

        return (title, description, excerpt);
    }

    private static string? MetaContent(string html, string property)
    {
        var esc = Regex.Escape(property);
        var m = Regex.Match(
            html,
            $@"<meta\s[^>]*?(?:property|name)\s*=\s*[""']{esc}[""'][^>]*?content\s*=\s*[""'](?<c>[^""']*)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success) return m.Groups["c"].Value;
        m = Regex.Match(
            html,
            $@"<meta\s[^>]*?content\s*=\s*[""'](?<c>[^""']*)[""'][^>]*?(?:property|name)\s*=\s*[""']{esc}[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups["c"].Value : null;
    }

    private static string? MetaName(string html, string name) => MetaContent(html, name);

    private static string? TagInner(string html, string tag)
    {
        var m = Regex.Match(
            html,
            $@"<{tag}\b[^>]*>(?<c>[\s\S]*?)</{tag}>",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["c"].Value : null;
    }

    private static string BuildExcerpt(string html, int maxChars)
    {
        var s = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<noscript[\s\S]*?</noscript>", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"<!--[\s\S]*?-->", " ");
        var main = Regex.Match(s, @"<(?:main|article)\b[^>]*>([\s\S]*?)</(?:main|article)>", RegexOptions.IgnoreCase);
        if (main.Success) s = main.Groups[1].Value;
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length <= maxChars) return s;
        return s[..maxChars].TrimEnd() + "…";
    }

    private static string? Decode(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return WebUtility.HtmlDecode(s);
    }

    private static string? CleanWs(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Regex.Replace(s, @"\s+", " ").Trim();
    }
}
