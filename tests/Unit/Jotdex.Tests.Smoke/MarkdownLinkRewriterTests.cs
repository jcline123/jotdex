using Jotdex.Infrastructure.Vault;

namespace Jotdex.Tests.Smoke;

public class MarkdownLinkRewriterTests
{
    [Fact]
    public void RewriteAssetStem_updates_encoded_and_plain_links()
    {
        var md = """
            ![a](OPNsense%20IPsec%20VPN.assets/screenshot-001.png)
            [b](OPNsense IPsec VPN.assets/policy.json)
            """;

        var updated = MarkdownLinkRewriter.RewriteAssetStem(md, "OPNsense IPsec VPN", "Edge VPN");

        Assert.Contains("Edge%20VPN.assets/screenshot-001.png", updated);
        Assert.Contains("Edge VPN.assets/policy.json", updated);
        Assert.DoesNotContain("OPNsense", updated);
    }

    [Fact]
    public void RewriteFolderPrefix_updates_relative_paths()
    {
        var md = "[x](Personal/Home/Aquarium/Nitrogen%20Cycle.md)";
        var updated = MarkdownLinkRewriter.RewriteFolderPrefix(md, "Personal/Home/Aquarium", "Personal/Home/Fish");
        Assert.Contains("Personal/Home/Fish/Nitrogen%20Cycle.md", updated);
    }
}
