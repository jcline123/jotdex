using Jotdex.Infrastructure.Integrations;

namespace Jotdex.Unit.Tests.Integrations;

public class IntegrationAttachmentValidationTests
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    [Fact]
    public void Accepts_png_and_builds_image_markdown()
    {
        Assert.True(IntegrationAttachmentValidation.TryDetect(TinyPng, "shot.png", "image/png", out var ct, out var err));
        Assert.Equal("image/png", ct);
        Assert.Null(err);
        Assert.Equal("![alt](Note.assets/shot.png)",
            IntegrationAttachmentValidation.BuildMarkdownSnippet(true, "alt", "Note.assets/shot.png"));
        Assert.Equal("[report.pdf](Note.assets/report.pdf)",
            IntegrationAttachmentValidation.BuildMarkdownSnippet(false, "report.pdf", "Note.assets/report.pdf"));
    }

    [Fact]
    public void Rejects_extension_mismatch_and_unknown()
    {
        Assert.False(IntegrationAttachmentValidation.TryDetect(TinyPng, "shot.jpg", null, out _, out var err));
        Assert.Contains("match", err!, StringComparison.OrdinalIgnoreCase);

        Assert.False(IntegrationAttachmentValidation.TryDetect(TinyPng, "evil.exe", null, out _, out _));

        var pdf = "%PDF-1.4\n%"u8.ToArray();
        Assert.True(IntegrationAttachmentValidation.TryDetect(pdf, "a.pdf", null, out var pct, out _));
        Assert.Equal("application/pdf", pct);
    }
}
