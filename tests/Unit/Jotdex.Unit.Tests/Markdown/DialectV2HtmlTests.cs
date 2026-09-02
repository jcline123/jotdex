using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Markdown;

public class DialectV2HtmlTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    [Fact]
    public void Details_comment_becomes_html_details()
    {
        var html = _renderer.ToHtml("<!-- jotdex-details -->\nSum\n\nBody\n<!-- /jotdex-details -->\n");
        Assert.Contains("jotdex-details", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sum", html, StringComparison.Ordinal);
        Assert.Contains("Body", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_math_is_not_dollar_syntax()
    {
        var html = _renderer.ToHtml("Cost is $5 and math is \\(a+b\\).");
        Assert.Contains("$5", html, StringComparison.Ordinal);
        Assert.Contains("jotdex-math-inline", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a+b", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Align_comment_wraps_following_line()
    {
        var html = _renderer.ToHtml("<!-- jotdex-align: center -->\nHello\n");
        Assert.Contains("jotdex-align-center", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", html, StringComparison.Ordinal);
    }
}
