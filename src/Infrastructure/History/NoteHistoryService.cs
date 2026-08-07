using System.Security.Cryptography;
using System.Text;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.History;

public sealed class NoteHistoryEntry
{
    public required string SnapshotId { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public required string ContentHash { get; init; }
    public required long SizeBytes { get; init; }
    /// <summary>Short human summary vs the next-newer version (or current note).</summary>
    public string? Summary { get; init; }
    /// <summary>One-line preview of a changed region.</summary>
    public string? Preview { get; init; }
}

public interface INoteHistoryService
{
    NoteHistoryEntry? SnapshotIfChanged(Guid noteId, string markdown);
    IReadOnlyList<NoteHistoryEntry> List(Guid noteId);
    /// <summary>List with diff summaries. Pass current note markdown for the newest row.</summary>
    IReadOnlyList<NoteHistoryEntry> ListWithSummaries(Guid noteId, string? currentMarkdown = null);
    string? ReadSnapshot(Guid noteId, string snapshotId);
    void Prune(Guid noteId, int maxSnapshots = 50, int maxAgeDays = 30);
}

public sealed class NoteHistoryService : INoteHistoryService
{
    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<NoteHistoryService> _logger;

    public NoteHistoryService(IDataRootResolver dataRoot, ILogger<NoteHistoryService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
    }

    public NoteHistoryEntry? SnapshotIfChanged(Guid noteId, string markdown)
    {
        var hash = Hash(markdown);
        var dir = NoteDir(noteId);
        Directory.CreateDirectory(dir);

        var latest = List(noteId).FirstOrDefault();
        if (latest is not null && latest.ContentHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            return null;

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfff");
        var id = $"{stamp}_{hash[..12]}";
        var path = Path.Combine(dir, id + ".md");
        File.WriteAllText(path, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Prune(noteId);
        return new NoteHistoryEntry
        {
            SnapshotId = id,
            CreatedUtc = DateTimeOffset.UtcNow,
            ContentHash = hash,
            SizeBytes = new FileInfo(path).Length
        };
    }

    public IReadOnlyList<NoteHistoryEntry> List(Guid noteId)
    {
        var dir = NoteDir(noteId);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md")
            .Select(ParseEntry)
            .Where(e => e is not null)
            .Cast<NoteHistoryEntry>()
            .OrderByDescending(e => e.CreatedUtc)
            .ToList();
    }

    public IReadOnlyList<NoteHistoryEntry> ListWithSummaries(Guid noteId, string? currentMarkdown = null)
    {
        var entries = List(noteId).ToList();
        if (entries.Count == 0) return entries;

        // entries[0] = oldest change that led to "newer"; compare each to the version after it
        for (var i = 0; i < entries.Count; i++)
        {
            var older = ReadSnapshot(noteId, entries[i].SnapshotId) ?? "";
            string newer;
            if (i == 0)
                newer = currentMarkdown ?? older;
            else
                newer = ReadSnapshot(noteId, entries[i - 1].SnapshotId) ?? older;

            var (summary, preview) = DescribeChange(older, newer);
            entries[i] = new NoteHistoryEntry
            {
                SnapshotId = entries[i].SnapshotId,
                CreatedUtc = entries[i].CreatedUtc,
                ContentHash = entries[i].ContentHash,
                SizeBytes = entries[i].SizeBytes,
                Summary = summary,
                Preview = preview
            };
        }

        return entries;
    }

    private static (string Summary, string? Preview) DescribeChange(string older, string newer)
    {
        if (string.Equals(older, newer, StringComparison.Ordinal))
            return ("No text change", null);

        var oldLines = SplitLines(older);
        var newLines = SplitLines(newer);
        var oldSet = oldLines.ToHashSet(StringComparer.Ordinal);
        var newSet = newLines.ToHashSet(StringComparer.Ordinal);
        var added = newLines.Count(l => !oldSet.Contains(l));
        var removed = oldLines.Count(l => !newSet.Contains(l));

        var summary = $"+{added} / -{removed} lines · {FormatSize(Encoding.UTF8.GetByteCount(older))}";

        string? preview = newLines.FirstOrDefault(l => !oldSet.Contains(l) && !string.IsNullOrWhiteSpace(l));
        preview ??= oldLines.FirstOrDefault(l => !newSet.Contains(l) && !string.IsNullOrWhiteSpace(l));
        if (preview is not null)
        {
            preview = preview.Trim();
            if (preview.Length > 120) preview = preview[..117] + "…";
        }

        return (summary, preview);
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    private static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.#} KB" : $"{bytes / (1024.0 * 1024):0.#} MB";

    public string? ReadSnapshot(Guid noteId, string snapshotId)
    {
        var name = Path.GetFileNameWithoutExtension(snapshotId);
        var path = Path.Combine(NoteDir(noteId), name + ".md");
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    public void Prune(Guid noteId, int maxSnapshots = 50, int maxAgeDays = 30)
    {
        var entries = List(noteId);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        var keep = entries
            .Where(e => e.CreatedUtc >= cutoff)
            .Take(maxSnapshots)
            .Select(e => e.SnapshotId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            if (keep.Contains(e.SnapshotId)) continue;
            try
            {
                File.Delete(Path.Combine(NoteDir(noteId), e.SnapshotId + ".md"));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed pruning history snapshot");
            }
        }
    }

    private string NoteDir(Guid noteId) =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "history", noteId.ToString("D"));

    private static string Hash(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static NoteHistoryEntry? ParseEntry(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var parts = name.Split('_', 2);
        if (parts.Length < 2) return null;
        if (!DateTimeOffset.TryParseExact(parts[0], "yyyyMMddTHHmmssfff", null,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var created))
        {
            created = File.GetCreationTimeUtc(path);
        }

        return new NoteHistoryEntry
        {
            SnapshotId = name,
            CreatedUtc = created,
            ContentHash = parts[1],
            SizeBytes = new FileInfo(path).Length
        };
    }
}
