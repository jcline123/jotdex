using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Vault;

public class AttachmentServingTests
{
    [Fact]
    public void Inline_images_only_for_safe_raster_types()
    {
        Assert.True(AttachmentServing.IsInlineImage("image/png"));
        Assert.True(AttachmentServing.IsInlineImage("image/jpeg"));
        Assert.False(AttachmentServing.IsInlineImage("image/svg+xml"));
        Assert.False(AttachmentServing.IsInlineImage("application/pdf"));
        Assert.False(AttachmentServing.IsInlineImage("text/html"));
    }

    [Fact]
    public void Active_content_forced_to_octets()
    {
        Assert.True(AttachmentServing.IsActiveOrUnsafeInline("text/html"));
        Assert.True(AttachmentServing.IsActiveOrUnsafeInline("image/svg+xml"));
        Assert.True(AttachmentServing.IsActiveOrUnsafeInline("application/javascript"));
        Assert.Equal("application/octet-stream", AttachmentServing.EffectiveMediaType("text/html", forceDownload: false));
        Assert.Equal("image/png", AttachmentServing.EffectiveMediaType("image/png", forceDownload: false));
        Assert.Equal("application/octet-stream", AttachmentServing.EffectiveMediaType("image/png", forceDownload: true));
    }

    [Fact]
    public void Content_disposition_includes_filename_star()
    {
        var header = AttachmentServing.ContentDispositionHeader("Report (final).pdf", attachment: true);
        Assert.Contains("attachment;", header, StringComparison.Ordinal);
        Assert.Contains("filename=", header, StringComparison.Ordinal);
        Assert.Contains("filename*=UTF-8''", header, StringComparison.Ordinal);
        Assert.Contains("Report", header, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatByteSize_is_human_readable()
    {
        Assert.Equal("512 B", AttachmentServing.FormatByteSize(512));
        Assert.Contains("KB", AttachmentServing.FormatByteSize(2048));
        Assert.Contains("MB", AttachmentServing.FormatByteSize(2 * 1024 * 1024));
    }
}
