using System.Text;
using Jotdex.Core.Snippets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Infrastructure.Snippets;

public sealed class SnippetCommandService : ISnippetCommandService
{
    private readonly INoteCommandService _notes;
    private readonly ISnippetIndex _index;
    private readonly IVaultPathGuard _paths;
    private readonly IVaultService _vault;

    public SnippetCommandService(
        INoteCommandService notes,
        ISnippetIndex index,
        IVaultPathGuard paths,
        IVaultService vault)
    {
        _notes = notes;
        _index = index;
        _paths = paths;
        _vault = vault;
    }

    public (SnippetSummary? Snippet, string? Error) Create(CreateSnippetRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Code))
            return (null, "Title and code are required.");

        var trigger = NormalizeTrigger(request.Trigger, request.Title);
        var duplicate = FindDuplicateTrigger(trigger, null);
        if (duplicate is not null)
            return (null, $"A snippet with shortcut \"{duplicate.Trigger}\" already exists ({duplicate.Title}). Delete or rename the existing one first.");

        var language = (request.Language ?? "plaintext").Trim().ToLowerInvariant();
        var folder = SnippetConstants.DefaultFolder;
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var markdown = BuildSnippetMarkdown(
            id,
            request.Title.Trim(),
            trigger,
            language,
            request.Code,
            request.Description,
            request.Tags,
            created: now,
            modified: now);

        var note = _notes.CreateComplete(folder, request.Title.Trim(), markdown);
        if (note is null) return (null, "Could not write snippet file.");

        _index.RebuildFromVault();
        return (SnippetNoteParser.FromNote(note), null);
    }

    public (SnippetSummary? Snippet, string? Error) Update(Guid noteId, UpdateSnippetRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Code))
            return (null, "Title and code are required.");

        var existing = _vault.GetNote(noteId);
        if (existing is null || !SnippetNoteParser.IsSnippet(existing.FrontMatter))
            return (null, "Snippet not found.");

        var trigger = NormalizeTrigger(request.Trigger, request.Title);
        var duplicate = FindDuplicateTrigger(trigger, noteId);
        if (duplicate is not null)
            return (null, $"Shortcut \"{duplicate.Trigger}\" is already used by \"{duplicate.Title}\".");

        var language = (request.Language ?? "plaintext").Trim().ToLowerInvariant();
        var created = existing.Created ?? existing.Modified ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var markdown = BuildSnippetMarkdown(
            noteId,
            request.Title.Trim(),
            trigger,
            language,
            request.Code,
            request.Description,
            request.Tags,
            created: created,
            modified: now);

        var save = _notes.Save(noteId, markdown, request.ExpectedETag);
        if (save.Conflict)
            return (null, "Snippet changed elsewhere — reload and try again.");
        if (!save.Success || save.Note is null)
            return (null, save.Error ?? "Could not save snippet.");

        _index.RebuildFromVault();
        return (SnippetNoteParser.FromNote(save.Note), null);
    }

    private SnippetSummary? FindDuplicateTrigger(string trigger, Guid? excludeNoteId)
    {
        foreach (var s in _index.List(null, null, 500))
        {
            if (excludeNoteId.HasValue && s.NoteId == excludeNoteId.Value) continue;
            if (string.Equals(s.Trigger, trigger, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }

    private static string NormalizeTrigger(string trigger, string title) =>
        string.IsNullOrWhiteSpace(trigger)
            ? SnippetNoteParser.Slugify(title)
            : SnippetNoteParser.Slugify(trigger);

    private static string BuildSnippetMarkdown(
        Guid id,
        string title,
        string trigger,
        string language,
        string code,
        string? description,
        IReadOnlyList<string>? tags,
        DateTimeOffset created,
        DateTimeOffset modified)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {id:D}");
        sb.AppendLine($"title: {YamlQuote(title)}");
        sb.AppendLine($"{SnippetConstants.TypeKey}: {SnippetConstants.TypeValue}");
        sb.AppendLine($"{SnippetConstants.LanguageKey}: {language}");
        sb.AppendLine($"{SnippetConstants.TriggerKey}: {trigger}");
        sb.AppendLine($"created: {created:O}");
        sb.AppendLine($"modified: {modified:O}");
        if (tags is { Count: > 0 })
        {
            sb.AppendLine("tags:");
            foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                sb.AppendLine($"  - {tag.Trim()}");
        }
        else
        {
            sb.AppendLine("tags: []");
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(description))
        {
            sb.AppendLine(description.Trim());
            sb.AppendLine();
        }
        sb.AppendLine($"```{language}");
        sb.AppendLine(code.TrimEnd());
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string YamlQuote(string value)
    {
        if (value.Contains(':') || value.Contains('#') || value.Contains('"') || value.StartsWith(' ') || value.EndsWith(' '))
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return value;
    }
}
