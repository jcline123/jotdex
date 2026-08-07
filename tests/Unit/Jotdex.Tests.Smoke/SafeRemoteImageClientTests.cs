using System.Net;
using Jotdex.Infrastructure.Net;

namespace Jotdex.Tests.Smoke;

public class SafeRemoteImageClientTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("169.254.1.1")]
    [InlineData("0.0.0.0")]
    public void Blocks_private_ipv4(string ip)
    {
        Assert.True(SafeRemoteImageClient.IsBlockedIp(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    public void Allows_public_ipv4(string ip)
    {
        Assert.False(SafeRemoteImageClient.IsBlockedIp(IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task ValidateUri_rejects_http_to_localhost()
    {
        var err = await SafeRemoteImageClient.ValidateUriAsync(new Uri("http://127.0.0.1/x.png"));
        Assert.NotNull(err);
    }

    [Fact]
    public void Sniff_detects_png()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.Equal("image/png", SafeRemoteImageClient.SniffImageContentType(png));
    }

    [Fact]
    public void ExtractRemoteImageUrls_finds_http_images()
    {
        var md = "![a](https://example.com/a.png)\n![b](/local.png)\n![c](http://cdn.example/x.jpg)";
        var urls = Jotdex.Infrastructure.Images.ImageLocalizer.ExtractRemoteImageUrls(md);
        Assert.Contains("https://example.com/a.png", urls);
        Assert.Contains("http://cdn.example/x.jpg", urls);
        Assert.DoesNotContain(urls, u => u.StartsWith('/'));
    }
}
