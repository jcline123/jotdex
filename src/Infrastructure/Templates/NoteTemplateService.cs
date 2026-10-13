using System.Text;
using Jotdex.Core.Templates;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Templates;

public sealed class NoteTemplateService : INoteTemplateService
{
    private readonly INoteCommandService _notes;
    private readonly IVaultPathGuard _paths;
    private readonly IVaultService _vault;
    private readonly ILogger<NoteTemplateService> _logger;

    public NoteTemplateService(
        INoteCommandService notes,
        IVaultPathGuard paths,
        IVaultService vault,
        ILogger<NoteTemplateService> logger)
    {
        _notes = notes;
        _paths = paths;
        _vault = vault;
        _logger = logger;
    }

    public IReadOnlyList<NoteTemplateSummary> List(string? query = null, int limit = 100)
    {
        if (!_paths.IsConfigured) return [];
        var q = (query ?? "").Trim();
        return _vault.ListNotes(null, includeSnippetNotes: true, includeTemplateNotes: true)
            .Where(n => n.IsNoteTemplate)
            .Select(n =>
            {
                var detail = _vault.GetNote(n.Id);
                var desc = detail?.FrontMatter.TryGetValue(TemplateConstants.DescriptionKey, out var d) == true
                    ? d?.Trim()
                    : null;
                return new NoteTemplateSummary(
                    n.Id,
                    n.Title,
                    n.FolderPath,
                    n.RelativePath,
                    string.IsNullOrWhiteSpace(desc) ? null : desc,
                    n.Tags,
                    n.Modified,
                    n.HasAttachments);
            })
            .Where(t =>
                string.IsNullOrEmpty(q) ||
                t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (t.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                t.Tags.Any(tag => tag.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Clamp(limit, 1, 500))
            .ToList();
    }

    public NoteDetail? Get(Guid templateId)
    {
        var note = _vault.GetNote(templateId);
        if (note is null || !IsTemplate(note.FrontMatter)) return null;
        return note;
    }

    public (NoteTemplateSummary? Template, string? Error) Create(CreateTemplateRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");

        var title = request.Title.Trim();
        var body = string.IsNullOrWhiteSpace(request.BodyMarkdown)
            ? $"# {title}\n\n"
            : request.BodyMarkdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var markdown = BuildTemplateMarkdown(id, title, body, request.Description, request.Tags, now, now);
        var note = _notes.CreateComplete(TemplateConstants.DefaultFolder, title, markdown);
        if (note is null) return (null, "Could not write template file.");
        return (ToSummary(note), null);
    }

    public (NoteTemplateSummary? Template, string? Error) Update(Guid templateId, UpdateTemplateRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");

        var existing = Get(templateId);
        if (existing is null) return (null, "Template not found.");

        var title = request.Title.Trim();
        var body = (request.BodyMarkdown ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        var created = existing.Created ?? existing.Modified ?? DateTimeOffset.UtcNow;
        var now = DateTimeOffset.UtcNow;
        var markdown = BuildTemplateMarkdown(
            templateId, title, body, request.Description, request.Tags, created, now);

        var save = _notes.Save(templateId, markdown, request.ExpectedETag);
        if (save.Conflict)
            return (null, "Template changed elsewhere — reload and try again.");
        if (!save.Success || save.Note is null)
            return (null, save.Error ?? "Could not save template.");

        return (ToSummary(save.Note), null);
    }

    public (NoteTemplateSummary? Template, string? Error) SaveFromNote(SaveNoteAsTemplateRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        var source = _vault.GetNote(request.SourceNoteId);
        if (source is null) return (null, "Source note not found.");
        if (IsTemplate(source.FrontMatter))
            return (null, "That note is already a template — open Templates to edit it.");

        try
        {
            var title = string.IsNullOrWhiteSpace(request.Title) ? source.Title : request.Title.Trim();
            var parsed = FrontMatterParser.Parse(source.Markdown);
            var body = parsed.Body;
            var now = DateTimeOffset.UtcNow;
            var id = Guid.NewGuid();
            var markdown = BuildTemplateMarkdown(id, title, body, request.Description, source.Tags, now, now);

            var note = _notes.CreateComplete(TemplateConstants.DefaultFolder, title, markdown);
            if (note is null) return (null, "Could not write template file.");

            // Copy attachments into the new template’s .assets and rewrite stem references.
            CopyAssetsBetweenNotes(source, note);

            var refreshed = _vault.GetNote(note.Id) ?? note;
            return (ToSummary(refreshed), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Save note as template failed");
            return (null, ex.Message);
        }
    }

    public (NoteDetail? Note, string? Error) Instantiate(Guid templateId, InstantiateTemplateRequest request)
    {
        if (!_paths.IsConfigured) return (null, "Vault not configured.");
        var template = Get(templateId);
        if (template is null) return (null, "Template not found.");
        if (string.IsNullOrWhiteSpace(request.Title))
            return (null, "Title is required.");

        try
        {
            var title = request.Title.Trim();
            var folder = (request.FolderRelativePath ?? "").Replace('\\', '/').Trim('/');
            var parsed = FrontMatterParser.Parse(template.Markdown);
            var body = parsed.Body;

            // Create a normal note (not a template) with a fresh identity — no API attribution copy.
            var created = _notes.Create(folder, title, body);
            if (created is null) return (null, "Could not create note from template.");

            CopyAssetsBetweenNotes(template, created);

            var refreshed = _vault.GetNote(created.Id) ?? created;
            return (refreshed, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Instantiate template failed");
            return (null, ex.Message);
        }
    }

    public PrepareInsertResult PrepareInsert(Guid templateId, Guid targetNoteId)
    {
        if (!_paths.IsConfigured)
            return new PrepareInsertResult(false, null, "Vault not configured.", null);
        var template = Get(templateId);
        if (template is null)
            return new PrepareInsertResult(false, null, "Template not found.", null);
        var target = _vault.GetNote(targetNoteId);
        if (target is null)
            return new PrepareInsertResult(false, null, "Target note not found.", null);

        try
        {
            var parsed = FrontMatterParser.Parse(template.Markdown);
            var body = parsed.Body;
            var remapped = CopyAssetsIntoTarget(template, target, body);
            var refreshed = _vault.GetNote(targetNoteId) ?? target;
            return new PrepareInsertResult(true, remapped, null, refreshed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Prepare template insert failed");
            return new PrepareInsertResult(false, null, ex.Message, null);
        }
    }

    private void CopyAssetsBetweenNotes(NoteDetail source, NoteDetail dest)
    {
        var srcAbs = _paths.EnsureInsideVault(source.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var destAbs = _paths.EnsureInsideVault(dest.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var srcStem = Path.GetFileNameWithoutExtension(srcAbs);
        var destStem = Path.GetFileNameWithoutExtension(destAbs);
        var srcAssets = Path.Combine(Path.GetDirectoryName(srcAbs)!, srcStem + ".assets");
        if (!Directory.Exists(srcAssets)) return;

        var destAssets = Path.Combine(Path.GetDirectoryName(destAbs)!, destStem + ".assets");
        Directory.CreateDirectory(destAssets);
        _paths.EnsureInsideVault(destAssets);

        foreach (var file in Directory.EnumerateFiles(srcAssets))
        {
            var name = Path.GetFileName(file);
            var destFile = Path.Combine(destAssets, name);
            var n = 1;
            while (File.Exists(destFile))
            {
                var baseName = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                destFile = Path.Combine(destAssets, $"{baseName}-{n}{ext}");
                n++;
            }
            File.Copy(file, destFile, overwrite: false);
        }

        // Rewrite stem in dest markdown if stems differ.
        if (!string.Equals(srcStem, destStem, StringComparison.Ordinal))
        {
            var rewritten = MarkdownLinkRewriter.RewriteAssetStem(dest.Markdown, srcStem, destStem);
            // Also rewrite any filename renames from collision — rare when dest assets empty; skip map for now.
            if (!string.Equals(rewritten, dest.Markdown, StringComparison.Ordinal))
                _notes.Save(dest.Id, rewritten, dest.ETag, force: true);
        }

        _vault.Rescan();
    }

    /// <summary>
    /// Copy template assets into the target note’s folder with collision-safe names;
    /// return body Markdown with stem + renamed file references remapped.
    /// </summary>
    private string CopyAssetsIntoTarget(NoteDetail template, NoteDetail target, string bodyMarkdown)
    {
        var srcAbs = _paths.EnsureInsideVault(template.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var destAbs = _paths.EnsureInsideVault(target.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var srcStem = Path.GetFileNameWithoutExtension(srcAbs);
        var destStem = Path.GetFileNameWithoutExtension(destAbs);
        var srcAssets = Path.Combine(Path.GetDirectoryName(srcAbs)!, srcStem + ".assets");
        if (!Directory.Exists(srcAssets))
            return MarkdownLinkRewriter.RewriteAssetStem(bodyMarkdown, srcStem, destStem);

        var destAssets = Path.Combine(Path.GetDirectoryName(destAbs)!, destStem + ".assets");
        Directory.CreateDirectory(destAssets);
        _paths.EnsureInsideVault(destAssets);

        var renameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(srcAssets))
        {
            var name = Path.GetFileName(file);
            var destFile = Path.Combine(destAssets, name);
            var finalName = name;
            var n = 1;
            while (File.Exists(destFile))
            {
                var baseName = Path.GetFileNameWithoutExtension(name);
                var ext = Path.GetExtension(name);
                finalName = $"{baseName}-{n}{ext}";
                destFile = Path.Combine(destAssets, finalName);
                n++;
            }
            File.Copy(file, destFile, overwrite: false);
            if (!string.Equals(name, finalName, StringComparison.OrdinalIgnoreCase))
                renameMap[name] = finalName;
        }

        var result = MarkdownLinkRewriter.RewriteAssetStem(bodyMarkdown, srcStem, destStem);
        foreach (var (oldName, newName) in renameMap)
            result = RewriteFileNameInAssets(result, destStem, oldName, newName);

        _vault.Rescan();
        return result;
    }

    private static string RewriteFileNameInAssets(string markdown, string stem, string oldFile, string newFile)
    {
        var forms = new[]
        {
            (stem + ".assets/" + oldFile, stem + ".assets/" + newFile),
            (EncodeSpaces(stem) + ".assets/" + EncodeSpaces(oldFile), EncodeSpaces(stem) + ".assets/" + EncodeSpaces(newFile)),
            (stem + ".assets/" + EncodeSpaces(oldFile), stem + ".assets/" + EncodeSpaces(newFile)),
        };
        var result = markdown;
        foreach (var (oldForm, newForm) in forms)
        {
            if (string.Equals(oldForm, newForm, StringComparison.Ordinal)) continue;
            result = result.Replace(oldForm, newForm, StringComparison.OrdinalIgnoreCase);
        }
        return result;
    }

    private static string EncodeSpaces(string value) => value.Replace(" ", "%20", StringComparison.Ordinal);

    private static string BuildTemplateMarkdown(
        Guid id,
        string title,
        string body,
        string? description,
        IReadOnlyList<string>? tags,
        DateTimeOffset created,
        DateTimeOffset modified)
    {
        var sb = new StringBuilder();
        sb.AppendLine("---");
        sb.AppendLine($"id: {id:D}");
        sb.AppendLine($"title: {EscapeYaml(title)}");
        sb.AppendLine($"{TemplateConstants.TypeKey}: {TemplateConstants.TypeValue}");
        if (!string.IsNullOrWhiteSpace(description))
            sb.AppendLine($"{TemplateConstants.DescriptionKey}: {EscapeYaml(description.Trim())}");
        sb.AppendLine($"created: {created:O}");
        sb.AppendLine($"modified: {modified:O}");
        sb.Append("tags: [");
        if (tags is { Count: > 0 })
            sb.Append(string.Join(", ", tags.Select(t => EscapeYaml(t))));
        sb.AppendLine("]");
        sb.AppendLine("---");
        sb.AppendLine();
        var trimmed = body.TrimStart();
        // Avoid duplicating an H1 that matches the title when body is empty-ish.
        if (string.IsNullOrWhiteSpace(trimmed))
            sb.AppendLine($"# {title}");
        else
            sb.Append(trimmed.EndsWith('\n') ? trimmed : trimmed + "\n");
        return sb.ToString();
    }

    private static NoteTemplateSummary ToSummary(NoteDetail note)
    {
        note.FrontMatter.TryGetValue(TemplateConstants.DescriptionKey, out var desc);
        return new NoteTemplateSummary(
            note.Id,
            note.Title,
            note.FolderPath,
            note.RelativePath,
            string.IsNullOrWhiteSpace(desc) ? null : desc.Trim(),
            note.Tags,
            note.Modified,
            note.Attachments.Count > 0);
    }

    public static bool IsTemplate(IReadOnlyDictionary<string, string?> fields) =>
        fields.TryGetValue(TemplateConstants.TypeKey, out var t) &&
        string.Equals(t?.Trim(), TemplateConstants.TypeValue, StringComparison.OrdinalIgnoreCase);

    private static string EscapeYaml(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        if (value.IndexOfAny([':', '#', '{', '}', '[', ']', ',', '&', '*', '!', '|', '>', '\'', '"', '%', '@', '`']) >= 0 ||
            value.StartsWith(' ') || value.EndsWith(' ') || value.Contains('\n'))
            return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        return value;
    }
}
