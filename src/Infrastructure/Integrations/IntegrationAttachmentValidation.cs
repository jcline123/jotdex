namespace Jotdex.Infrastructure.Integrations;

/// <summary>Magic-byte / allowlist checks for Integrations attachment uploads.</summary>
public static class IntegrationAttachmentValidation
{
    /// <summary>
    /// Broad common office / document / archive / text / image types.
    /// Active content (html/js/svg) is allowed to upload but is always force-downloaded by the server.
    /// Executables and scripts that are not documents stay blocked.
    /// </summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Images
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".tif", ".tiff", ".heic", ".heif",
        // Documents
        ".pdf", ".doc", ".docx", ".rtf", ".odt", ".txt", ".md", ".csv", ".json", ".xml", ".log",
        // Spreadsheets / presentations
        ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp",
        // Archives
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz",
        // Other common
        ".eml", ".msg", ".ics", ".vcf", ".epub",
        // HTML sidecars (served as download-only octets, never executed)
        ".html", ".htm",
    };

    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".com", ".msi", ".scr", ".ps1", ".psm1", ".vbs", ".vbe",
        ".js", ".mjs", ".cjs", ".wsf", ".wsh", ".jar", ".apk", ".dmg", ".pkg", ".sh", ".bash",
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
        if (string.IsNullOrEmpty(ext))
        {
            error = "File name must include an extension";
            return false;
        }

        if (BlockedExtensions.Contains(ext))
        {
            error = "Executable or script file types are not allowed";
            return false;
        }

        if (!AllowedExtensions.Contains(ext))
        {
            error =
                "File type not allowed for Integrations uploads. Use common documents, images, archives, or text " +
                "(e.g. pdf, docx, xlsx, pptx, zip, txt, png).";
            return false;
        }

        var detected = DetectMime(header, ext);
        if (detected is null)
        {
            // Text-like and some binaries: trust extension when magic is inconclusive but not blocked.
            if (ext is ".txt" or ".md" or ".csv" or ".log" or ".json" or ".xml" or ".ics" or ".vcf" or ".eml")
            {
                contentType = PreferDeclared(declaredContentType, GuessFromExtension(ext));
                return true;
            }
            // OLE legacy Office (.doc/.xls/.ppt) often looks like compound files — accept by extension.
            if (ext is ".doc" or ".xls" or ".ppt" or ".msg")
            {
                contentType = PreferDeclared(declaredContentType, GuessFromExtension(ext));
                return true;
            }
            error = "File contents do not match an allowed type";
            return false;
        }

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
        ".bmp" => mime == "image/bmp",
        ".tif" or ".tiff" => mime == "image/tiff",
        ".pdf" => mime == "application/pdf",
        ".docx" => mime is "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => mime is "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => mime is "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".odt" or ".ods" or ".odp" => mime.StartsWith("application/vnd.oasis.opendocument", StringComparison.Ordinal),
        ".zip" or ".docx" or ".xlsx" or ".pptx" or ".odt" or ".ods" or ".odp" or ".epub" or ".jar"
            => mime is "application/zip" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                or "application/vnd.openxmlformats-officedocument.presentationml.presentation"
                or "application/epub+zip"
                or "application/vnd.oasis.opendocument.text"
                or "application/vnd.oasis.opendocument.spreadsheet"
                or "application/vnd.oasis.opendocument.presentation",
        ".gz" or ".tgz" => mime is "application/gzip" or "application/x-gzip",
        ".7z" => mime == "application/x-7z-compressed",
        ".rar" => mime is "application/vnd.rar" or "application/x-rar-compressed",
        ".txt" or ".md" or ".log" => mime == "text/plain",
        ".csv" => mime is "text/csv" or "text/plain",
        ".json" => mime is "application/json" or "text/plain",
        ".xml" or ".html" or ".htm" => mime is "text/html" or "application/xml" or "text/xml" or "text/plain",
        ".heic" or ".heif" => mime.StartsWith("image/", StringComparison.Ordinal),
        _ => true
    };

    private static string GuessFromExtension(string ext) => ext switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".doc" => "application/msword",
        ".xls" => "application/vnd.ms-excel",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".csv" => "text/csv",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".html" or ".htm" => "text/html",
        ".md" => "text/markdown",
        ".zip" => "application/zip",
        ".7z" => "application/x-7z-compressed",
        ".rar" => "application/vnd.rar",
        ".gz" or ".tgz" => "application/gzip",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
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
        if (h.Length >= 2 && h[0] == 0x42 && h[1] == 0x4D)
            return "image/bmp";
        if (h.Length >= 4 && h[0] == 0x49 && h[1] == 0x49 && h[2] == 0x2A && h[3] == 0x00)
            return "image/tiff";
        if (h.Length >= 4 && h[0] == 0x4D && h[1] == 0x4D && h[2] == 0x00 && h[3] == 0x2A)
            return "image/tiff";
        if (h.Length >= 5 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46)
            return "application/pdf";
        if (h.Length >= 3 && h[0] == 0x1F && h[1] == 0x8B)
            return "application/gzip";
        if (h.Length >= 6 && h[0] == 0x37 && h[1] == 0x7A && h[2] == 0xBC && h[3] == 0xAF && h[4] == 0x27 && h[5] == 0x1C)
            return "application/x-7z-compressed";
        if (h.Length >= 7 && h[0] == 0x52 && h[1] == 0x61 && h[2] == 0x72 && h[3] == 0x21 && h[4] == 0x1A && h[5] == 0x07)
            return "application/vnd.rar";

        // ZIP / OOXML / ODF / EPUB
        if (h.Length >= 4 && h[0] == 0x50 && h[1] == 0x4B && (h[2] == 0x03 || h[2] == 0x05 || h[2] == 0x07))
        {
            return ext switch
            {
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".odt" => "application/vnd.oasis.opendocument.text",
                ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
                ".odp" => "application/vnd.oasis.opendocument.presentation",
                ".epub" => "application/epub+zip",
                ".zip" => "application/zip",
                _ => "application/zip"
            };
        }

        if (ext is ".txt" or ".md" or ".csv" or ".log" or ".json" or ".xml" or ".html" or ".htm" or ".ics" or ".vcf" or ".eml")
        {
            if (h.IsEmpty) return GuessFromExtension(ext);
            var sample = h.Length > 512 ? h[..512] : h;
            var bad = 0;
            foreach (var b in sample)
            {
                if (b == 0) { bad++; continue; }
                if (b < 9 || (b > 13 && b < 32)) bad++;
            }
            if (bad > sample.Length / 10) return null;
            if (ext is ".html" or ".htm") return "text/html";
            if (ext == ".csv") return "text/csv";
            if (ext == ".json") return "application/json";
            if (ext == ".xml") return "application/xml";
            return "text/plain";
        }

        return null;
    }

    public static string BuildMarkdownSnippet(bool isImage, string altOrLabel, string markdownPath)
    {
        var label = string.IsNullOrWhiteSpace(altOrLabel) ? (isImage ? "image" : "file") : altOrLabel.Trim();
        label = label.Replace("[", "\\[", StringComparison.Ordinal).Replace("]", "\\]", StringComparison.Ordinal);
        return isImage ? $"![{label}]({markdownPath})" : $"[{label}]({markdownPath})";
    }
}
