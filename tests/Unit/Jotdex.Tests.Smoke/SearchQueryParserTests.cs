using Jotdex.Infrastructure.Search;

namespace Jotdex.Tests.Smoke;

public class SearchQueryParserTests
{
    [Fact]
    public void Parses_filters_and_free_text()
    {
        var q = SearchQueryParser.Parse("folder:Networking tag:vpn ipsec");
        Assert.Equal("Networking", q.Folder);
        Assert.Equal("vpn", q.Tag);
        Assert.Equal("ipsec", q.FreeText);
        Assert.False(q.Literal);
    }

    [Fact]
    public void Quoted_string_forces_literal()
    {
        var q = SearchQueryParser.Parse("\"0x80070005\"");
        Assert.True(q.Literal);
        Assert.Equal("0x80070005", q.FreeText);
    }

    [Fact]
    public void Unknown_filter_becomes_ordinary_text()
    {
        var q = SearchQueryParser.Parse("bogus:value hello");
        Assert.Contains("bogus:value", q.FreeText, StringComparison.Ordinal);
        Assert.Contains("hello", q.FreeText, StringComparison.Ordinal);
    }
}
