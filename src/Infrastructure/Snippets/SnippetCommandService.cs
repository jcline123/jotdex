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

    public SnippetCommandService(INoteCommandService notes, ISnippetIndex index, IVaultPathGuard paths)
    {
        _notes = notes;
        _index = index;
        _paths = paths;
    }

    public SnippetSummary? Create(CreateSnippetRequest request)
    {
        if (!_paths.IsConfigured) return null;
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Code))
            return null;

        var trigger = string.IsNullOrWhiteSpace(request.Trigger)
            ? SnippetNoteParser.Slugify(request.Title)
            : SnippetNoteParser.Slugify(request.Trigger);
        var language = (request.Language ?? "plaintext").Trim().ToLowerInvariant();
        var folder = (request.Folder ?? "").Replace('\\', '/').Trim('/');
        var now = DateTimeOffset.UtcNow.ToString("O");
        var id = Guid.NewGuid();

        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {id:D}");
        sb.AppendLine($"title: {YamlQuote(request.Title.Trim())}");
        sb.AppendLine($"{SnippetConstants.TypeKey}: {SnippetConstants.TypeValue}");
        sb.AppendLine($"{SnippetConstants.LanguageKey}: {language}");
        sb.AppendLine($"{SnippetConstants.TriggerKey}: {trigger}");
        sb.AppendLine($"modified: {now}");
        if (request.Tags is { Count: > 0 })
        {
            sb.AppendLine("tags:");
            foreach (var tag in request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)))
                sb.AppendLine($"  - {tag.Trim()}");
        }
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"# {request.Title.Trim()}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            sb.AppendLine(request.Description.Trim());
            sb.AppendLine();
        }
        sb.AppendLine($"```{language}");
        sb.AppendLine(request.Code.TrimEnd());
        sb.AppendLine("```");

        var note = _notes.Create(folder, request.Title.Trim(), sb.ToString());
        if (note is null) return null;

        _index.RebuildFromVault();
        return SnippetNoteParser.FromNote(note);
    }

    private static string YamlQuote(string value)
    {
        if (value.Contains(':') || value.Contains('#') || value.Contains('"') || value.StartsWith(' ') || value.EndsWith(' '))
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return value;
    }
}
