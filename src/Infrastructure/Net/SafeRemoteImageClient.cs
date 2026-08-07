using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Net;

public sealed class RemoteFetchResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public byte[]? Bytes { get; init; }
    public string? ContentType { get; init; }
    public string? FinalUrl { get; init; }
}

/// <summary>HTTP(S) image fetch with SSRF protections (no private/loopback hosts).</summary>
public sealed class SafeRemoteImageClient
{
    public const int MaxRedirects = 3;
    public const long MaxBytes = 15L * 1024 * 1024; // 15 MB per image
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<SafeRemoteImageClient> _logger;

    public SafeRemoteImageClient(ILogger<SafeRemoteImageClient> logger) => _logger = logger;

    public async Task<RemoteFetchResult> FetchImageAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return Fail("Invalid URL");

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var gate = await ValidateUriAsync(uri, ct);
            if (gate is not null) return Fail(gate);

            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = Timeout
            };
            using var client = new HttpClient(handler) { Timeout = Timeout };
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.TryAddWithoutValidation("User-Agent", "Jotdex/1.0 (image-localize)");
            req.Headers.TryAddWithoutValidation("Accept", "image/*,*/*;q=0.8");

            HttpResponseMessage res;
            try
            {
                res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Remote image fetch failed");
                return Fail("Download failed");
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

                var ctHeader = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!string.IsNullOrEmpty(ctHeader) &&
                    !ctHeader.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    !ctHeader.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                    return Fail($"Refusing content-type {ctHeader}");

                var len = res.Content.Headers.ContentLength;
                if (len is > MaxBytes) return Fail("Image too large");

                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                using var ms = new MemoryStream();
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > MaxBytes) return Fail("Image too large");
                    ms.Write(buffer, 0, read);
                }

                var bytes = ms.ToArray();
                if (bytes.Length == 0) return Fail("Empty response");

                var sniff = SniffImageContentType(bytes) ?? (ctHeader.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? ctHeader : null);
                if (sniff is null) return Fail("Response is not a recognized image");

                return new RemoteFetchResult
                {
                    Success = true,
                    Bytes = bytes,
                    ContentType = sniff,
                    FinalUrl = uri.ToString()
                };
            }
        }

        return Fail("Too many redirects");
    }

    public static async Task<string?> ValidateUriAsync(Uri uri, CancellationToken ct = default)
    {
        if (uri.Scheme is not ("http" or "https"))
            return "Only http/https URLs are allowed";
        if (string.IsNullOrWhiteSpace(uri.Host))
            return "Missing host";
        if (uri.IsLoopback)
            return "Loopback addresses are blocked";
        if (IPAddress.TryParse(uri.Host, out var literal) && IsBlockedIp(literal))
            return "Private/internal addresses are blocked";

        IPAddress[] addrs;
        try
        {
            addrs = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
        }
        catch
        {
            return "Host could not be resolved";
        }

        if (addrs.Length == 0) return "Host could not be resolved";
        if (addrs.Any(IsBlockedIp)) return "Private/internal addresses are blocked";
        return null;
    }

    public static bool IsBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            // 0.0.0.0/8, 10/8, 127/8, 169.254/16, 172.16/12, 192.168/16, 100.64/10, 192.0.0/24, 192.0.2/24, 198.18/15, 198.51.100/24, 203.0.113/24, 224+/4 multicast+
            if (b[0] == 0) return true;
            if (b[0] == 10) return true;
            if (b[0] == 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true; // CGNAT
            if (b[0] == 192 && b[1] == 0 && b[2] <= 2) return true;
            if (b[0] == 198 && (b[1] == 18 || b[1] == 19 || (b[1] == 51 && b[2] == 100))) return true;
            if (b[0] == 203 && b[1] == 0 && b[2] == 113) return true;
            if (b[0] >= 224) return true;
            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            var bytes = ip.GetAddressBytes();
            // fc00::/7 unique local
            if ((bytes[0] & 0xfe) == 0xfc) return true;
            // ::ffff:0:0/96 IPv4-mapped — check embedded
            if (ip.IsIPv4MappedToIPv6) return IsBlockedIp(ip.MapToIPv4());
        }

        return false;
    }

    public static string? SniffImageContentType(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return "image/jpeg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return "image/png";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return "image/gif";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return "image/webp";
        return null;
    }

    private static RemoteFetchResult Fail(string error) => new() { Success = false, Error = error };
}
