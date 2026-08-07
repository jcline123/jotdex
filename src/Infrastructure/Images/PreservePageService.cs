using System.Text;
using System.Text.Json.Serialization;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Html;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Images;

public sealed class PreservePageResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public string? FileName { get; init; }
    public string? MarkdownPath { get; init; }
    public string? MarkdownSnippet { get; init; }
    public string? AttachmentId { get; init; }
    public NoteDetail? Note { get; init; }
    [JsonPropertyName("etag")]
    public string? ETag { get; init; }
}

public interface IPreservePageService
{
    PreservePageResult SaveClip(Guid noteId, string html, string? sourceUrl, string? expectedETag);
}

public sealed class PreservePageService : IPreservePageService
{
    private readonly IVaultService _vault;
    private readonly INoteCommandService _notes;
    private readonly ILogger<PreservePageService> _logger;

    public PreservePageService(IVaultService vault, INoteCommandService notes, ILogger<PreservePageService> logger)
    {
        _vault = vault;
        _notes = notes;
        _logger = logger;
    }

    public PreservePageResult SaveClip(Guid noteId, string html, string? sourceUrl, string? expectedETag)
    {
        var note = _vault.GetNote(noteId);
        if (note is null)
            return new PreservePageResult { Success = false, Error = "Note not found" };

        if (string.IsNullOrWhiteSpace(html))
            return new PreservePageResult { Success = false, Error = "html required" };

        try
        {
            var fragment = HtmlClipSanitizer.SanitizeFragment(html);
            if (string.IsNullOrWhiteSpace(fragment))
                return new PreservePageResult { Success = false, Error = "Nothing left after sanitizing HTML" };

            var title = note.Title;
            var doc = HtmlClipSanitizer.WrapDocument(fragment, sourceUrl, title);
            var fileName = $"clipped-page-{DateTime.Now:yyyy-MM-dd_HHmmss}.html";
            var bytes = Encoding.UTF8.GetBytes(doc);
            using var ms = new MemoryStream(bytes);
            var uploaded = _notes.AddAttachment(noteId, ms, fileName, "text/html; charset=utf-8");
            if (!uploaded.Success || string.IsNullOrWhiteSpace(uploaded.MarkdownPath))
                return new PreservePageResult { Success = false, Error = uploaded.Error ?? "Could not write HTML sidecar" };

            var sourceLine = string.IsNullOrWhiteSpace(sourceUrl) ? "" : $"\nSource: {sourceUrl}\n";
            var snippet =
                $"\n\n<details>\n<summary>Clipped page ({fileName})</summary>\n{sourceLine}\n" +
                $"[Open clipped page]({uploaded.MarkdownPath})\n\n" +
                $"</details>\n\n";

            var markdown = note.Markdown.TrimEnd() + snippet;
            var etag = expectedETag ?? note.ETag;
            var save = _notes.Save(noteId, markdown, etag, force: string.IsNullOrEmpty(expectedETag));
            if (!save.Success && save.Conflict)
                save = _notes.Save(noteId, markdown, note.ETag, force: true);

            if (!save.Success)
            {
                return new PreservePageResult
                {
                    Success = false,
                    Error = save.Error ?? "Sidecar saved but note update failed",
                    FileName = uploaded.FileName,
                    MarkdownPath = uploaded.MarkdownPath,
                    AttachmentId = uploaded.AttachmentId
                };
            }

            return new PreservePageResult
            {
                Success = true,
                FileName = uploaded.FileName,
                MarkdownPath = uploaded.MarkdownPath,
                MarkdownSnippet = snippet.Trim(),
                AttachmentId = uploaded.AttachmentId,
                Note = save.Note,
                ETag = save.ETag
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preserve-page clip failed");
            return new PreservePageResult { Success = false, Error = ex.Message };
        }
    }
}
