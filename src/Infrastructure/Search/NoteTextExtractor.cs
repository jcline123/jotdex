using System.Text;
using System.Text.RegularExpressions;
using Jotdex.Core.Vault;

namespace Jotdex.Infrastructure.Search;

public sealed class NoteSearchDocument
{
    public required Guid NoteId { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderPath { get; init; }
    public required string Tags { get; init; }
    public required string Headings { get; init; }
    public required string Body { get; init; }
    public required string Code { get; init; }
    public required string AttachmentNames { get; init; }
    public required string CombinedLiteral { get; init; }
    public DateTimeOffset? Modified { get; init; }
    public bool HasAttachments { get; init; }
}

public static partial class NoteTextExtractor
{
    public static NoteSearchDocument FromNote(NoteDetail note)
    {
        var fm = FrontMatterParserAdapter.StripForIndex(note.Markdown);
        Extract(fm, out var headings, out var body, out var code);
        var tags = string.Join(' ', note.Tags);
        var att = string.Join(' ', note.Attachments.Select(a => a.FileName));
        var combined = string.Join('\n', note.Title, note.FolderPath, tags, headings, body, code, att);

        return new NoteSearchDocument
        {
            NoteId = note.Id,
            Title = note.Title,
            RelativePath = note.RelativePath,
            FolderPath = note.FolderPath,
            Tags = tags,
            Headings = headings,
            Body = body,
            Code = code,
            AttachmentNames = att,
            CombinedLiteral = combined,
            Modified = note.Modified,
            HasAttachments = note.Attachments.Count > 0
        };
    }

    public static NoteSearchDocument FromIndexed(
        Guid id,
        string title,
        string relativePath,
        string folderPath,
        IReadOnlyList<string> tags,
        string markdownBody,
        IEnumerable<string> attachmentNames,
        DateTimeOffset? modified,
        bool hasAttachments,
        string? attachmentText = null)
    {
        Extract(markdownBody, out var headings, out var body, out var code);
        var tagText = string.Join(' ', tags);
        var att = string.Join(' ', attachmentNames);
        var attBody = attachmentText ?? "";
        var combined = string.Join('\n', title, folderPath, tagText, headings, body, code, att, attBody);
        return new NoteSearchDocument
        {
            NoteId = id,
            Title = title,
            RelativePath = relativePath,
            FolderPath = folderPath,
            Tags = tagText,
            Headings = headings,
            Body = string.IsNullOrEmpty(attBody) ? body : body + "\n" + attBody,
            Code = code,
            AttachmentNames = att,
            CombinedLiteral = combined,
            Modified = modified,
            HasAttachments = hasAttachments
        };
    }

    private static void Extract(string markdown, out string headings, out string body, out string code)
    {
        var headingSb = new StringBuilder();
        var bodySb = new StringBuilder();
        var codeSb = new StringBuilder();
        var inFence = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
            {
                codeSb.AppendLine(line);
                continue;
            }

            var h = HeadingLineRegex().Match(line);
            if (h.Success)
            {
                headingSb.AppendLine(h.Groups[1].Value.Trim());
                bodySb.AppendLine(h.Groups[1].Value.Trim());
            }
            else
            {
                var stripped = StripDialectNoise(line);
                stripped = stripped.Replace("\\[", " ", StringComparison.Ordinal).Replace("\\]", " ", StringComparison.Ordinal);
                bodySb.AppendLine(stripped);
            }
        }

        headings = headingSb.ToString();
        body = bodySb.ToString();
        code = codeSb.ToString();
    }

    private static string StripDialectNoise(string line)
    {
        var s = CommentNoise().Replace(line, " ");
        s = HighlightMarks().Replace(s, "$1");
        s = InlineMath().Replace(s, " $1 ");
        s = ImageAlt().Replace(s, " $1 $2 ");
        s = HtmlTags().Replace(s, " ");
        return s;
    }

    [GeneratedRegex(@"<!--\s*/?jotdex-[^>]*-->")]
    private static partial Regex CommentNoise();

    [GeneratedRegex(@"==([^=]+)==")]
    private static partial Regex HighlightMarks();

    [GeneratedRegex(@"\\\(([\s\S]*?)\\\)")]
    private static partial Regex InlineMath();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImageAlt();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex(@"^#{1,6}\s+(.+)$")]
    private static partial Regex HeadingLineRegex();
}

/// <summary>Avoid circular ref: reuse FrontMatterParser from Vault namespace.</summary>
internal static class FrontMatterParserAdapter
{
    public static string StripForIndex(string markdown)
    {
        var parsed = Vault.FrontMatterParser.Parse(markdown);
        return parsed.Body;
    }
}
