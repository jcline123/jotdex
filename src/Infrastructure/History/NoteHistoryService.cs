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
}

public interface INoteHistoryService
{
    NoteHistoryEntry? SnapshotIfChanged(Guid noteId, string markdown);
    IReadOnlyList<NoteHistoryEntry> List(Guid noteId);
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
