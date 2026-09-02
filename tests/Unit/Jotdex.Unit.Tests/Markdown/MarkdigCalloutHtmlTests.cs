using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Markdown;

public class MarkdigCalloutHtmlTests
{
    private readonly MarkdigMarkdownRenderer _renderer = new();

    [Theory]
    [InlineData("warning", "> [!warning]\n> Careful")]
    [InlineData("tip", "> [!tip]\n> Try this")]
    [InlineData("info", "> [!info]\n> FYI")]
    [InlineData("danger", "> [!danger]\n> Stop")]
    [InlineData("note", "> [!note]\n> Remember")]
    public void Obsidian_callout_is_typed_html_not_literal_marker(string type, string markdown)
    {
        var html = _renderer.ToHtml(markdown);
        Assert.DoesNotContain($"[!{type}]", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"markdown-alert-{type}", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("markdown-alert", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Titled_obsidian_line_becomes_typed_alert()
    {
        var html = _renderer.ToHtml("> [!warning] Watch out\n> Body");
        Assert.DoesNotContain("[!warning]", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("markdown-alert-warning", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Watch out", html, StringComparison.Ordinal);
        Assert.Contains("Body", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Fenced_callout_marker_stays_literal()
    {
        var html = _renderer.ToHtml("```\n> [!warning]\n> fake\n```\n");
        Assert.Contains("[!warning]", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<pre", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fenced_titled_callout_stays_literal()
    {
        var html = _renderer.ToHtml("```\n> [!warning] Watch out\n```\n");
        Assert.Contains("[!warning] Watch out", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("markdown-alert-warning", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Html_data_callout_blockquote_is_preserved()
    {
        var html = _renderer.ToHtml("<blockquote data-callout=\"warning\"><p>Old</p></blockquote>");
        Assert.Contains("data-callout=\"warning\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Old", html, StringComparison.Ordinal);
    }
}
