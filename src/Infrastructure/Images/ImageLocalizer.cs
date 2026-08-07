using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Net;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Images;

public sealed class LocalizeImagesResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public int Localized { get; init; }
    public IReadOnlyList<string> Failed { get; init; } = [];
    public NoteDetail? Note { get; init; }
    public string? ETag { get; init; }
    public string? Markdown { get; init; }
}

public interface IImageLocalizer
{
    Task<LocalizeImagesResult> LocalizeAsync(Guid noteId, IReadOnlyList<string>? urls, string? expectedETag, CancellationToken ct = default);
}

public sealed partial class ImageLocalizer : IImageLocalizer
{
    private readonly IVaultService _vault;
    private readonly INoteCommandService _notes;
    private readonly SafeRemoteImageClient _http;
    private readonly ILogger<ImageLocalizer> _logger;

    public ImageLocalizer(
        IVaultService vault,
        INoteCommandService notes,
        SafeRemoteImageClient http,
        ILogger<ImageLocalizer> logger)
    {
        _vault = vault;
        _notes = notes;
        _http = http;
        _logger = logger;
    }

    public async Task<LocalizeImagesResult> LocalizeAsync(
        Guid noteId,
        IReadOnlyList<string>? urls,
        string? expectedETag,
        CancellationToken ct = default)
    {
        var note = _vault.GetNote(noteId);
        if (note is null)
            return new LocalizeImagesResult { Success = false, Error = "Note not found" };

        var targets = (urls is { Count: > 0 } ? urls : ExtractRemoteImageUrls(note.Markdown))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToList();

        if (targets.Count == 0)
            return new LocalizeImagesResult { Success = true, Localized = 0, Note = note, ETag = note.ETag, Markdown = note.Markdown };

        var markdown = note.Markdown;
        var failed = new List<string>();
        var localized = 0;

        foreach (var url in targets)
        {
            ct.ThrowIfCancellationRequested();
            var fetch = await _http.FetchImageAsync(url, ct);
            if (!fetch.Success || fetch.Bytes is null)
            {
                failed.Add($"{url}: {fetch.Error}");
                continue;
            }

            var ext = ExtForContentType(fetch.ContentType);
            var name = $"remote-{DateTime.Now:yyyy-MM-dd_HHmmss}-{localized + 1}{ext}";
            await using var ms = new MemoryStream(fetch.Bytes);
            var uploaded = _notes.AddAttachment(noteId, ms, name, fetch.ContentType);
            if (!uploaded.Success || string.IsNullOrWhiteSpace(uploaded.MarkdownPath))
            {
                failed.Add($"{url}: {uploaded.Error ?? "upload failed"}");
                continue;
            }

            markdown = ReplaceImageUrl(markdown, url, uploaded.MarkdownPath);
            localized++;
            note = uploaded.Note ?? _vault.GetNote(noteId);
        }

        if (localized == 0)
        {
            return new LocalizeImagesResult
            {
                Success = false,
                Error = failed.Count > 0 ? string.Join("; ", failed.Take(3)) : "No images localized",
                Failed = failed,
                Note = note,
                ETag = note?.ETag,
                Markdown = note?.Markdown
            };
        }

        // Persist rewritten markdown (force if etag omitted — we just mutated attachments)
        var etag = expectedETag ?? note?.ETag ?? "";
        var save = _notes.Save(noteId, markdown, etag, force: string.IsNullOrEmpty(expectedETag));
        if (!save.Success && save.Conflict && note is not null)
            save = _notes.Save(noteId, markdown, note.ETag, force: true);

        if (!save.Success)
        {
            return new LocalizeImagesResult
            {
                Success = false,
                Error = save.Error ?? "Could not save rewritten markdown",
                Localized = localized,
                Failed = failed,
                Note = save.Note,
                ETag = save.ETag,
                Markdown = markdown
            };
        }

        return new LocalizeImagesResult
        {
            Success = true,
            Localized = localized,
            Failed = failed,
            Note = save.Note,
            ETag = save.ETag,
            Markdown = save.Note?.Markdown ?? markdown
        };
    }

    public static IReadOnlyList<string> ExtractRemoteImageUrls(string markdown)
    {
        var list = new List<string>();
        foreach (Match m in ImageMarkdownRegex().Matches(markdown))
        {
            var url = m.Groups["url"].Value.Trim();
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                list.Add(url);
        }

        return list;
    }

    private static string ReplaceImageUrl(string markdown, string remoteUrl, string localPath)
    {
        // Replace exact URL occurrences inside markdown image/link targets
        return markdown.Replace(remoteUrl, localPath, StringComparison.Ordinal);
    }

    private static string ExtForContentType(string? ct) => ct?.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        _ => ".png"
    };

    [GeneratedRegex(@"!\[[^\]]*\]\((?<url>[^)\s]+)\)", RegexOptions.Compiled)]
    private static partial Regex ImageMarkdownRegex();
}
