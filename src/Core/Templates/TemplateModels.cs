using Jotdex.Core.Vault;

namespace Jotdex.Core.Templates;

public static class TemplateConstants
{
    public const string TypeKey = "jotdex_type";
    public const string TypeValue = "note-template";
    public const string DescriptionKey = "jotdex_description";
    /// <summary>Reserved vault folder for note templates (hidden from the notes UI).</summary>
    public const string DefaultFolder = "Templates";
}

public sealed record NoteTemplateSummary(
    Guid NoteId,
    string Title,
    string FolderPath,
    string RelativePath,
    string? Description,
    IReadOnlyList<string> Tags,
    DateTimeOffset? Modified,
    bool HasAttachments);

public sealed record CreateTemplateRequest(
    string Title,
    string BodyMarkdown,
    string? Description,
    IReadOnlyList<string>? Tags);

public sealed record UpdateTemplateRequest(
    string Title,
    string BodyMarkdown,
    string? Description,
    IReadOnlyList<string>? Tags,
    string ExpectedETag);

public sealed record SaveNoteAsTemplateRequest(
    Guid SourceNoteId,
    string? Title,
    string? Description);

public sealed record InstantiateTemplateRequest(
    string Title,
    string FolderRelativePath);

public sealed record PrepareInsertResult(
    bool Success,
    string? Markdown,
    string? Error,
    NoteDetail? TargetNote);

public interface INoteTemplateService
{
    IReadOnlyList<NoteTemplateSummary> List(string? query = null, int limit = 100);
    NoteDetail? Get(Guid templateId);
    (NoteTemplateSummary? Template, string? Error) Create(CreateTemplateRequest request);
    (NoteTemplateSummary? Template, string? Error) Update(Guid templateId, UpdateTemplateRequest request);
    (NoteTemplateSummary? Template, string? Error) SaveFromNote(SaveNoteAsTemplateRequest request);
    (NoteDetail? Note, string? Error) Instantiate(Guid templateId, InstantiateTemplateRequest request);
    /// <summary>
    /// Copy template attachments into the target note’s .assets (unique names) and return remapped body Markdown
    /// for client-side insert (undoable). Never replaces the target note body.
    /// </summary>
    PrepareInsertResult PrepareInsert(Guid templateId, Guid targetNoteId);
}
