using Jotdex.Infrastructure.Net;

namespace Jotdex.Tests.Smoke;

public class PageHtmlParserTests
{
    [Fact]
    public void Parse_ReadsOpenGraphAndStripsScripts()
    {
        const string html = """
            <html><head>
            <title>Ignored Title</title>
            <meta property="og:title" content="How to Set Up UniFi" />
            <meta property="og:description" content="UniFi OS is pre-installed on UniFi Consoles." />
            </head><body>
            <script>evil()</script>
            <article><p>Article body for excerpt.</p></article>
            </body></html>
            """;
        var (title, description, excerpt) = PageHtmlParser.Parse(html);
        Assert.Equal("How to Set Up UniFi", title);
        Assert.Equal("UniFi OS is pre-installed on UniFi Consoles.", description);
        Assert.Contains("Article body", excerpt);
        Assert.DoesNotContain("evil", excerpt);
    }
}
