using Jotdex.Infrastructure.Integrations;

namespace Jotdex.Unit.Tests.Integrations;

public class IntegrationMarkdownInsertTests
{
    [Fact]
    public void Insert_top_after_h1_leaves_rest_byte_identical()
    {
        const string full = """
            ---
            id: 11111111-1111-1111-1111-111111111111
            title: N
            ---

            # Title

            Keep me
            More
            """;
        var next = IntegrationMarkdownInsert.Insert(full, "INSERTED", "top", null);
        Assert.NotNull(next);
        Assert.Contains("INSERTED", next);
        Assert.Contains("Keep me", next);
        Assert.Contains("More", next);
        // Front matter unchanged
        Assert.StartsWith("---\nid: 11111111-1111-1111-1111-111111111111\n", next!.Replace("\r\n", "\n"));
        var afterInsert = next.Replace("\r\n", "\n");
        Assert.Contains("\nKeep me\nMore", afterInsert);
    }

    [Fact]
    public void Insert_after_heading_and_missing_heading()
    {
        const string full = """
            ---
            title: N
            ---

            # Intro

            Body

            ## Later

            Tail
            """;
        var ok = IntegrationMarkdownInsert.Insert(full, "NEW", "afterHeading", "Later");
        Assert.NotNull(ok);
        var n = ok!.Replace("\r\n", "\n");
        var laterIdx = n.IndexOf("## Later", StringComparison.Ordinal);
        var newIdx = n.IndexOf("NEW", StringComparison.Ordinal);
        Assert.True(newIdx > laterIdx);
        Assert.Contains("Tail", n);

        Assert.Null(IntegrationMarkdownInsert.Insert(full, "X", "afterHeading", "Missing"));
    }

    [Fact]
    public void FindSnippets_respects_max_and_can_be_empty()
    {
        var body = "Alpha hello world\n\n## Sec\nhello again hello";
        var snips = IntegrationMarkdownInsert.FindSnippets(body, "hello", max: 2);
        Assert.Equal(2, snips.Count);
        Assert.Empty(IntegrationMarkdownInsert.FindSnippets(body, "zzz"));
    }
}
