using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jotdex.Core.Integrations;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Infrastructure.Integrations;

public static class NoteProvenance
{
    public static string ApplyOnCreate(string fullMarkdown, NoteChangeContext ctx, DateTimeOffset now)
    {
        var via = ViaString(ctx.Via);
        var by = ctx.ActorDisplayName ?? "";
        var md = fullMarkdown;
        md = Upsert(md, ProvenanceKeys.CreatedVia, via);
        if (!string.IsNullOrWhiteSpace(by))
            md = Upsert(md, ProvenanceKeys.CreatedBy, by);
        md = Upsert(md, ProvenanceKeys.UpdatedVia, via);
        if (!string.IsNullOrWhiteSpace(by))
            md = Upsert(md, ProvenanceKeys.UpdatedBy, by);
        if (ctx.Via == NoteChangeVia.Api)
        {
            md = Upsert(md, ProvenanceKeys.LastApiUpdateAt, now.ToString("O"));
            if (!string.IsNullOrWhiteSpace(by))
                md = Upsert(md, ProvenanceKeys.LastApiUpdateBy, by);
        }
        md = Upsert(md, ProvenanceKeys.ProvenanceHash, ComputeDigest(md));
        return md;
    }

    public static string ApplyOnUpdate(string fullMarkdown, NoteChangeContext ctx, DateTimeOffset now)
    {
        var via = ViaString(ctx.Via);
        var by = ctx.ActorDisplayName ?? "";
        var md = fullMarkdown;
        md = Upsert(md, ProvenanceKeys.UpdatedVia, via);
        md = string.IsNullOrWhiteSpace(by)
            ? RemoveKey(md, ProvenanceKeys.UpdatedBy)
            : Upsert(md, ProvenanceKeys.UpdatedBy, by);
        if (ctx.Via == NoteChangeVia.Api)
        {
            md = Upsert(md, ProvenanceKeys.LastApiUpdateAt, now.ToString("O"));
            if (!string.IsNullOrWhiteSpace(by))
                md = Upsert(md, ProvenanceKeys.LastApiUpdateBy, by);
        }
        md = Upsert(md, ProvenanceKeys.ProvenanceHash, ComputeDigest(md));
        return md;
    }

    public static AttributionView ResolveAttribution(IReadOnlyDictionary<string, string?> fm, string bodyMarkdown)
    {
        var storedHash = fm.GetValueOrDefault(ProvenanceKeys.ProvenanceHash);
        var current = ComputeDigestFromParts(fm, bodyMarkdown);
        var match = !string.IsNullOrWhiteSpace(storedHash) &&
                    string.Equals(storedHash, current, StringComparison.OrdinalIgnoreCase);

        if (!match && HasAnyAttribution(fm))
        {
            return new AttributionView(
                CreatedVia: fm.GetValueOrDefault(ProvenanceKeys.CreatedVia) ?? "unknown",
                CreatedBy: fm.GetValueOrDefault(ProvenanceKeys.CreatedBy),
                UpdatedVia: "external",
                UpdatedBy: null,
                LastApiUpdateAt: ParseDate(fm.GetValueOrDefault(ProvenanceKeys.LastApiUpdateAt)),
                LastApiUpdateBy: fm.GetValueOrDefault(ProvenanceKeys.LastApiUpdateBy),
                ProvenanceInferred: true);
        }

        return new AttributionView(
            CreatedVia: fm.GetValueOrDefault(ProvenanceKeys.CreatedVia) ?? "unknown",
            CreatedBy: fm.GetValueOrDefault(ProvenanceKeys.CreatedBy),
            UpdatedVia: fm.GetValueOrDefault(ProvenanceKeys.UpdatedVia) ?? "unknown",
            UpdatedBy: fm.GetValueOrDefault(ProvenanceKeys.UpdatedBy),
            LastApiUpdateAt: ParseDate(fm.GetValueOrDefault(ProvenanceKeys.LastApiUpdateAt)),
            LastApiUpdateBy: fm.GetValueOrDefault(ProvenanceKeys.LastApiUpdateBy),
            ProvenanceInferred: string.IsNullOrWhiteSpace(storedHash));
    }

    public static string ComputeDigest(string fullMarkdown)
    {
        var parsed = FrontMatterParser.Parse(fullMarkdown);
        return ComputeDigestFromParts(parsed.Fields, parsed.Body);
    }

    public static string ComputeDigestFromParts(IReadOnlyDictionary<string, string?> fields, string body)
    {
        var sb = new StringBuilder();
        foreach (var kv in fields.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (ProvenanceKeys.ServerOwned.Contains(kv.Key)) continue;
            sb.Append(kv.Key).Append('=').Append(kv.Value ?? "").Append('\n');
        }
        sb.Append("---\n").Append(body.Replace("\r\n", "\n").TrimEnd());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    private static bool HasAnyAttribution(IReadOnlyDictionary<string, string?> fm) =>
        fm.ContainsKey(ProvenanceKeys.UpdatedVia) || fm.ContainsKey(ProvenanceKeys.CreatedVia);

    private static DateTimeOffset? ParseDate(string? s) =>
        DateTimeOffset.TryParse(s, out var d) ? d : null;

    private static string ViaString(NoteChangeVia via) => via switch
    {
        NoteChangeVia.Ui => "ui",
        NoteChangeVia.Api => "api",
        NoteChangeVia.Import => "import",
        NoteChangeVia.External => "external",
        _ => "unknown"
    };

    private static string Upsert(string markdown, string key, string value)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal))
        {
            return $"---\n{key}: {YamlEscape(value)}\n---\n\n{markdown.TrimStart()}";
        }
        var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return markdown;
        var header = markdown[3..end];
        var rest = markdown[(end + "\n---".Length)..];
        var line = $"{key}: {YamlEscape(value)}";
        var keyRe = new Regex($@"^{Regex.Escape(key)}:\s*.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (keyRe.IsMatch(header))
            header = keyRe.Replace(header, line);
        else
            header = header.TrimEnd() + "\n" + line;
        return "---" + header + "\n---" + rest;
    }

    private static string RemoveKey(string markdown, string key)
    {
        if (!markdown.StartsWith("---", StringComparison.Ordinal)) return markdown;
        var end = markdown.IndexOf("\n---", 3, StringComparison.Ordinal);
        if (end < 0) return markdown;
        var header = markdown[3..end];
        var rest = markdown[(end + "\n---".Length)..];
        header = Regex.Replace(header, $@"^{Regex.Escape(key)}:\s*.*\r?\n?", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        return "---" + header + "\n---" + rest;
    }

    private static string YamlEscape(string value)
    {
        if (value.Contains(':') || value.Contains('#') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return value;
    }
}

public sealed record AttributionView(
    string CreatedVia,
    string? CreatedBy,
    string UpdatedVia,
    string? UpdatedBy,
    DateTimeOffset? LastApiUpdateAt,
    string? LastApiUpdateBy,
    bool ProvenanceInferred);
