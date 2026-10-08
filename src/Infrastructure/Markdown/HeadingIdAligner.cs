using System.Text;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Jotdex.Infrastructure.Markdown;

/// <summary>
/// Replaces Markdig AutoIdentifier ids with digit-safe slugs that match the editor outline.
/// </summary>
internal static class HeadingIdAligner
{
    public static void Apply(MarkdownDocument doc)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var heading in doc.Descendants().OfType<HeadingBlock>())
        {
            var text = PlainText(heading);
            heading.GetAttributes().Id = HeadingSlug.Unique(HeadingSlug.Slugify(text), used);
        }
    }

    private static string PlainText(HeadingBlock heading)
    {
        if (heading.Inline is null) return "";
        var sb = new StringBuilder();
        AppendInline(heading.Inline, sb);
        return sb.ToString();
    }

    private static void AppendInline(Inline? inline, StringBuilder sb)
    {
        for (var cur = inline; cur is not null; cur = cur.NextSibling)
        {
            switch (cur)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content);
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case ContainerInline container:
                    AppendInline(container.FirstChild, sb);
                    break;
            }
        }
    }
}
