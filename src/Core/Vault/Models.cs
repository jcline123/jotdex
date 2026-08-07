namespace Jotdex.Core.Vault;

public sealed class VaultInfo
{
    public required string Name { get; init; }
    public required string FormatVersion { get; init; }
    public required int NoteCount { get; init; }
    public required int FolderCount { get; init; }
}

public sealed class FolderNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required IReadOnlyList<FolderNode> Children { get; init; }
}

public sealed class NoteSummary
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderPath { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset? Modified { get; init; }
    public DateTimeOffset? Created { get; init; }
    public bool HasAttachments { get; init; }
}

public sealed class NoteDetail
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string RelativePath { get; init; }
    public required string FolderPath { get; init; }
    public required string Markdown { get; init; }
    public required string Html { get; init; }
    public required string ETag { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public DateTimeOffset? Modified { get; init; }
    public DateTimeOffset? Created { get; init; }
    public IReadOnlyDictionary<string, string?> FrontMatter { get; init; }
        = new Dictionary<string, string?>();
    public IReadOnlyList<AttachmentInfo> Attachments { get; init; } = [];
    public IReadOnlyList<HtmlSidecar> HtmlSidecars { get; init; } = [];
}

public sealed class AttachmentInfo
{
    public required string Id { get; init; }
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }
}

public sealed class HtmlSidecar
{
    public required string FileName { get; init; }
    public required string RelativePath { get; init; }
    public required string AttachmentId { get; init; }
}

public interface IVaultPathGuard
{
    string VaultRoot { get; }
    bool IsConfigured { get; }
    string EnsureInsideVault(string absoluteOrRelativePath);
    string ToRelativePath(string absolutePath);
    /// <summary>Point at a new vault directory (must exist). Returns error message or null.</summary>
    string? TrySetVaultPath(string absolutePath);
}


public interface IVaultService
{
    VaultInfo GetInfo();
    FolderNode GetTree();
    IReadOnlyList<NoteSummary> ListNotes(string? folderRelativePath);
    NoteDetail? GetNote(Guid id);
    AttachmentInfo? GetAttachment(string attachmentId);
    Stream OpenAttachmentStream(string attachmentId);
    void Rescan();
}

public interface IVaultRescanObserver
{
    void AfterVaultRescan();
}

public interface IMarkdownRenderer
{
    string ToHtml(string markdown);
}
