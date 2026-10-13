using System.Text;

namespace Jotdex.Infrastructure.Vault;

/// <summary>Safe Content-Type / Content-Disposition for vault attachment downloads.</summary>
public static class AttachmentServing
{
    /// <summary>Types safe to display inline in the browser (images only).</summary>
    public static bool IsInlineImage(string? contentType) =>
        contentType is "image/png" or "image/jpeg" or "image/gif" or "image/webp";

    /// <summary>
    /// Active / scriptable types must never execute in Jotdex's origin — force download as octets.
    /// </summary>
    public static bool IsActiveOrUnsafeInline(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        var ct = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return ct is "text/html" or "application/xhtml+xml" or "image/svg+xml"
            or "text/javascript" or "application/javascript" or "application/x-javascript"
            or "text/ecmascript" or "application/ecmascript"
            or "application/xml" or "text/xml";
    }

    public static string EffectiveMediaType(string contentType, bool forceDownload)
    {
        if (forceDownload || IsActiveOrUnsafeInline(contentType) || !IsInlineImage(contentType))
            return "application/octet-stream";
        return contentType;
    }

    /// <summary>RFC 6266 / 5987 disposition so Safari/iOS keep the original filename.</summary>
    public static string ContentDispositionHeader(string fileName, bool attachment)
    {
        var safe = SanitizeFileName(fileName);
        var ascii = ToAsciiFallback(safe);
        var utf8 = Uri.EscapeDataString(safe);
        var kind = attachment ? "attachment" : "inline";
        return $"{kind}; filename=\"{ascii}\"; filename*=UTF-8''{utf8}";
    }

    public static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName?.Trim() ?? "") ;
        if (string.IsNullOrWhiteSpace(name)) return "download.bin";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (Array.IndexOf(invalid, ch) >= 0 || ch < 32)
                sb.Append('_');
            else
                sb.Append(ch);
        }
        var cleaned = sb.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "download.bin" : cleaned;
    }

    private static string ToAsciiFallback(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch is '"' or '\\' or '\r' or '\n')
                sb.Append('_');
            else if (ch <= 0x7F)
                sb.Append(ch);
            else
                sb.Append('_');
        }
        var s = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(s) ? "download.bin" : s;
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }
}
