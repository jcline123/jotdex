using Jotdex.Infrastructure.Export;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Export;

public class ShareHtmlAnonymizerTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    [Fact]
    public void Strips_comments_classes_and_data_attributes()
    {
        const string html = """
            <p>Hello</p>
            <!-- jotdex-task id="abc" priority="high" -->
            <details class="jotdex-details"><summary>Sum</summary><p>Body</p></details>
            <div class="jotdex-align-center">Centered</div>
            <figure class="jotdex-figure" data-jotdex-image="1" data-align="center"><img alt="x" /></figure>
            <span class="jotdex-math jotdex-math-inline" data-jotdex-math="inline">a+b</span>
            <div class="jotdex-link-card" data-jotdex-link-card="1">Card</div>
            """;

        var cleaned = ShareHtmlAnonymizer.StripProductIdentifiers(html);

        Assert.DoesNotContain("jotdex", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", cleaned, StringComparison.Ordinal);
        Assert.Contains("class=\"details\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("class=\"align-center\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("class=\"figure\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("data-align=\"center\"", cleaned, StringComparison.Ordinal);
        Assert.Contains("math-inline", cleaned, StringComparison.Ordinal);
        Assert.Contains("link-card", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("<!--", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendered_dialect_html_has_no_product_token_after_strip()
    {
        var markdown = """
            <!-- jotdex-details -->
            Summary

            Body
            <!-- /jotdex-details -->

            <!-- jotdex-align: center -->
            Centered

            Math is \(a+b\).

            - [ ] Review <!-- jotdex-task id="task-1" priority="high" -->
            """;

        var html = _renderer.ToHtml(markdown);
        Assert.Contains("jotdex", html, StringComparison.OrdinalIgnoreCase);

        var cleaned = ShareHtmlAnonymizer.StripProductIdentifiers(html);
        Assert.DoesNotContain("jotdex", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Summary", cleaned, StringComparison.Ordinal);
        Assert.Contains("Centered", cleaned, StringComparison.Ordinal);
        Assert.Contains("a+b", cleaned, StringComparison.Ordinal);
        Assert.Contains("Review", cleaned, StringComparison.Ordinal);
        Assert.Contains("details", cleaned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("align-center", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_eat_unrelated_html_comments()
    {
        const string html = """
            <!-- copyright 2024 -->
            <p>Hello</p>
            <!-- jotdex-task id="abc" -->
            """;

        var cleaned = ShareHtmlAnonymizer.StripProductIdentifiers(html);

        Assert.Contains("<!-- copyright 2024 -->", cleaned, StringComparison.Ordinal);
        Assert.Contains("Hello", cleaned, StringComparison.Ordinal);
        Assert.DoesNotContain("jotdex", cleaned, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Leaves_user_text_that_says_the_product_name()
    {
        var cleaned = ShareHtmlAnonymizer.StripProductIdentifiers("<p>We used Jotdex last year.</p>");
        Assert.Contains("Jotdex", cleaned, StringComparison.Ordinal);
    }
}
