namespace Jotdex.Infrastructure.Integrations;

/// <summary>Magic-byte / allowlist checks for Integrations attachment uploads.</summary>
public static class IntegrationAttachmentValidation
{
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp",
        ".pdf", ".docx", ".xlsx", ".txt", ".csv"
    };

    public static bool TryDetect(
        ReadOnlySpan<byte> header,
        string? declaredFileName,
        string? declaredContentType,
        out string contentType,
        out string? error)
    {
        contentType = "application/octet-stream";
        error = null;

        var ext = Path.GetExtension(declaredFileName ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
        {
            error = "File type not allowed. Allowed: png, jpg, jpeg, gif, webp, pdf, docx, xlsx, txt, csv";
            return false;
        }

        var detected = DetectMime(header, ext);
        if (detected is null)
        {
            error = "File contents do not match an allowed type";
            return false;
        }

        // Extension must agree with detected family (images vs pdf vs office vs text).
        if (!ExtensionMatches(ext, detected))
        {
            error = "File extension does not match contents";
            return false;
        }

        contentType = PreferDeclared(declaredContentType, detected);
        return true;
    }

    private static string PreferDeclared(string? declared, string detected)
    {
        if (string.IsNullOrWhiteSpace(declared)) return detected;
        if (declared.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            detected.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return detected;
        if (declared.StartsWith("text/", StringComparison.OrdinalIgnoreCase) &&
            detected.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return detected;
        return detected;
    }

    private static bool ExtensionMatches(string ext, string mime) => ext switch
    {
        ".png" => mime == "image/png",
        ".jpg" or ".jpeg" => mime == "image/jpeg",
        ".gif" => mime == "image/gif",
        ".webp" => mime == "image/webp",
        ".pdf" => mime == "application/pdf",
        ".docx" => mime is "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => mime is "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".txt" => mime == "text/plain",
        ".csv" => mime is "text/csv" or "text/plain",
        _ => false
    };

    private static string? DetectMime(ReadOnlySpan<byte> h, string ext)
    {
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47)
            return "image/png";
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
            return "image/jpeg";
        if (h.Length >= 6 && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38)
            return "image/gif";
        if (h.Length >= 12 &&
            h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46 &&
            h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50)
            return "image/webp";
        if (h.Length >= 5 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46)
            return "application/pdf";

        // OOXML (docx/xlsx) is a ZIP container.
        if (h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B && (h[2] == 0x03 || h[2] == 0x05 || h[2] == 0x07))
        {
            if (ext == ".docx")
                return "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            if (ext == ".xlsx")
                return "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            return null;
        }

        // Text / CSV: reject if high ratio of NUL or control bytes (except tab/CR/LF).
        if (ext is ".txt" or ".csv")
        {
            if (h.IsEmpty) return ext == ".csv" ? "text/csv" : "text/plain";
            var sample = h.Length > 512 ? h[..512] : h;
            var bad = 0;
            foreach (var b in sample)
            {
                if (b == 0) { bad++; continue; }
                if (b < 9 || (b > 13 && b < 32)) bad++;
            }
            if (bad > sample.Length / 10) return null;
            return ext == ".csv" ? "text/csv" : "text/plain";
        }

        return null;
    }

    public static string BuildMarkdownSnippet(bool isImage, string altOrLabel, string markdownPath)
    {
        var label = string.IsNullOrWhiteSpace(altOrLabel) ? (isImage ? "image" : "file") : altOrLabel.Trim();
        // Escape brackets in label lightly
        label = label.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        return isImage ? $"![{label}]({markdownPath})" : $"[{label}]({markdownPath})";
    }
}
