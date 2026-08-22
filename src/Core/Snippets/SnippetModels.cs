namespace Jotdex.Core.Snippets;

public static class SnippetConstants
{
    public const string TypeKey = "jotdex_type";
    public const string TypeValue = "code-snippet";
    public const string LanguageKey = "jotdex_language";
    public const string TriggerKey = "jotdex_trigger";
}

public sealed record SnippetSummary(
    Guid NoteId,
    string Title,
    string Trigger,
    string Language,
    string FolderPath,
    string RelativePath,
    string? Description,
    string Code,
    IReadOnlyList<string> Tags);

public sealed record CreateSnippetRequest(
    string Title,
    string Trigger,
    string Language,
    string Code,
    string Folder,
    string? Description,
    IReadOnlyList<string>? Tags);

public interface ISnippetIndex
{
    void RebuildFromVault();
    IReadOnlyList<SnippetSummary> List(string? query, string? language, int limit = 50);
}

public interface ISnippetCommandService
{
    SnippetSummary? Create(CreateSnippetRequest request);
}
