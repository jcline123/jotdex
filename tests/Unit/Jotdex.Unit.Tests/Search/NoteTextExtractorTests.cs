using Jotdex.Infrastructure.Search;

namespace Jotdex.Unit.Tests.Search;

public class NoteTextExtractorTests
{
    [Fact]
    public void Indexes_visible_dialect_v2_text_without_comment_noise()
    {
        var markdown = """
            ==highlighted==
            <!-- jotdex-details -->
            Summary
            Hidden details body
            <!-- /jotdex-details -->
            See \(E=mc^2\)
            ![plain](Stress.assets/plain.png)
            """;
        var doc = NoteTextExtractor.FromIndexed(
            Guid.NewGuid(),
            "Stress",
            "Stress.md",
            "",
            Array.Empty<string>(),
            markdown,
            Array.Empty<string>(),
            null,
            false);
        Assert.Contains("highlighted", doc.Body, StringComparison.Ordinal);
        Assert.Contains("Hidden details body", doc.Body, StringComparison.Ordinal);
        Assert.Contains("E=mc^2", doc.Body, StringComparison.Ordinal);
        Assert.Contains("plain", doc.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("jotdex-details", doc.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("==", doc.Body, StringComparison.Ordinal);
    }
}
