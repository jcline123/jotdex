using Jotdex.Infrastructure.Html;

namespace Jotdex.Tests.Smoke;

public class HtmlClipSanitizerTests
{
    [Fact]
    public void Strips_script_and_event_handlers()
    {
        var html = """
            <div onclick="alert(1)">
              <p>Hello</p>
              <script>alert('xss')</script>
              <a href="javascript:alert(2)">bad</a>
              <img src="x" onerror="steal()">
            </div>
            """;

        var clean = HtmlClipSanitizer.SanitizeFragment(html);

        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hello", clean, StringComparison.Ordinal);
        Assert.Contains("#blocked", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void Strips_svg_script_payload()
    {
        var html = """
            <p>Safe</p>
            <svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)">
              <script>alert(2)</script>
              <circle cx="10" cy="10" r="5"/>
            </svg>
            """;
        var clean = HtmlClipSanitizer.SanitizeFragment(html);
        Assert.DoesNotContain("<svg", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Safe", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void WrapDocument_includes_source_and_charset()
    {
        var doc = HtmlClipSanitizer.WrapDocument("<p>Body</p>", "https://example.com/page", "Clip");
        Assert.Contains("<!DOCTYPE html>", doc, StringComparison.Ordinal);
        Assert.Contains("charset", doc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://example.com/page", doc, StringComparison.Ordinal);
        Assert.Contains("<p>Body</p>", doc, StringComparison.Ordinal);
    }
}
