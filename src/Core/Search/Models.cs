namespace Jotdex.Core.Search;

public sealed class SearchRequest
{
    public required string RawQuery { get; init; }
    public bool ForceLiteral { get; init; }
    public int Limit { get; init; } = 50;
}

public sealed record SearchHit
{
    public required Guid NoteId { get; init; }
    public required string Title { get; init; }
    public required string FolderPath { get; init; }
    public required string RelativePath { get; init; }
    public string? Snippet { get; init; }
    public string? MatchingHeading { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset? Modified { get; init; }
    public bool HasCodeMatch { get; init; }
    public bool HasAttachmentMatch { get; init; }
    public double Score { get; init; }
}

public sealed class SearchResponse
{
    public required string Mode { get; init; }
    public required string ParsedQuery { get; init; }
    public required IReadOnlyList<SearchHit> Hits { get; init; }
    public string? Warning { get; init; }
}

public sealed class IndexStatus
{
    public required bool Ready { get; init; }
    public required bool Fts5 { get; init; }
    public required bool Trigram { get; init; }
    public required int NoteCount { get; init; }
    public string? LastError { get; init; }
}

public interface ISearchIndex
{
    IndexStatus Probe();
    void RebuildFromVault();
    SearchResponse Search(SearchRequest request);
}
