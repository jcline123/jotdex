using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Markdown;

public class HeadingAnchorHtmlTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    [Theory]
    [InlineData("### 3C Service Call Queue", "3c-service-call-queue")]
    [InlineData("### Trident", "trident")]
    [InlineData("### Hello", "hello")]
    [InlineData("### Carrie CQ", "carrie-cq")]
    public void Heading_ids_keep_leading_digits_and_match_slug(string markdown, string expectedId)
    {
        var html = _renderer.ToHtml(markdown);
        Assert.Contains($"id=\"{expectedId}\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Explicit_html_anchor_survives_html_pass()
    {
        var html = _renderer.ToHtml("<a id=\"cq-3c-service\"></a>\n\n### 3C Service Call Queue\n");
        Assert.Contains("id=\"cq-3c-service\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fragment_only_href_is_preserved()
    {
        var html = _renderer.ToHtml("[Go](#cq-3c-service)\n");
        Assert.Contains("href=\"#cq-3c-service\"", html, StringComparison.OrdinalIgnoreCase);
    }
}
