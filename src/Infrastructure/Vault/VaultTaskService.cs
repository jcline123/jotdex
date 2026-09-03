using System.Text.RegularExpressions;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

public sealed class VaultTaskDto
{
    public required string Id { get; init; }
    public required Guid NoteId { get; init; }
    public required string NoteTitle { get; init; }
    public required string NoteRelativePath { get; init; }
    public required string Text { get; init; }
    public string? Due { get; init; }
    public string Priority { get; init; } = "normal";
    public string? Remind { get; init; }
    public string? Added { get; init; }
    public int LineIndex { get; init; }
    public bool StandaloneTodosMd { get; init; }
}

public sealed class VaultTaskActionResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public Guid? NoteId { get; init; }
}

public sealed class VaultTaskUpdate
{
    public string? Text { get; init; }
    public string? Priority { get; init; }
    /// <summary>ISO-8601 due instant; empty string clears.</summary>
    public string? Due { get; init; }
    public string? Remind { get; init; }
}

public interface IVaultTaskService
{
    IReadOnlyList<VaultTaskDto> ListOpenTasks();
    VaultTaskActionResult Complete(string taskId);
    VaultTaskActionResult Update(string taskId, VaultTaskUpdate patch);
}

public sealed class VaultTaskService : IVaultTaskService
{
    private static readonly Regex TaskLine = new(
        @"^(\s*)- \[([ xX])\]\s+(.*?)(?:\s*<!--\s*jotdex-(?:task|todo)\s+([^>]*)-->)?\s*$",
        RegexOptions.Compiled);

    private readonly IVaultService _vault;
    private readonly INoteCommandService _commands;
    private readonly ILogger<VaultTaskService> _logger;

    public VaultTaskService(IVaultService vault, INoteCommandService commands, ILogger<VaultTaskService> logger)
    {
        _vault = vault;
        _commands = commands;
        _logger = logger;
    }

    public IReadOnlyList<VaultTaskDto> ListOpenTasks()
    {
        var list = new List<VaultTaskDto>();
        // Include Todos.md so path-based tooling stays consistent; the rail filters standalone out of the mixed list.
        foreach (var summary in _vault.ListNotes("", includeStandaloneTodosMd: true))
        {
            var note = _vault.GetNote(summary.Id);
            if (note is null) continue;
            var standalone = summary.RelativePath.Equals("Todos.md", StringComparison.OrdinalIgnoreCase);
            var lines = note.Markdown.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var m = TaskLine.Match(lines[i]);
                if (!m.Success) continue;
                if (m.Groups[2].Value is "x" or "X") continue;
                var titlePart = m.Groups[3].Value.Trim();
                var commentIdx = titlePart.IndexOf("<!--", StringComparison.Ordinal);
                if (commentIdx >= 0) titlePart = titlePart[..commentIdx].TrimEnd();
                if (string.IsNullOrWhiteSpace(titlePart)) continue;

                var attrs = ParseAttrs(m.Groups[4].Success ? m.Groups[4].Value : "");
                var id = attrs.GetValueOrDefault("id");
                if (string.IsNullOrWhiteSpace(id))
                    id = $"{summary.Id:N}:{i}";

                list.Add(new VaultTaskDto
                {
                    Id = id!,
                    NoteId = summary.Id,
                    NoteTitle = summary.Title,
                    NoteRelativePath = summary.RelativePath,
                    Text = titlePart,
                    Due = attrs.GetValueOrDefault("due"),
                    Priority = attrs.GetValueOrDefault("priority") ?? "normal",
                    Remind = attrs.GetValueOrDefault("remind"),
                    Added = attrs.GetValueOrDefault("added"),
                    LineIndex = i,
                    StandaloneTodosMd = standalone
                });
            }
        }

        return list
            .OrderBy(t => PriorityRank(t.Priority))
            .ThenBy(t => t.Due is null ? 1 : 0)
            .ThenBy(t => t.Due)
            .ThenBy(t => t.Text, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public VaultTaskActionResult Complete(string taskId)
    {
        try
        {
            foreach (var summary in _vault.ListNotes("", includeStandaloneTodosMd: true))
            {
                var note = _vault.GetNote(summary.Id);
                if (note is null) continue;
                var lines = note.Markdown.Replace("\r\n", "\n").Split('\n').ToList();
                var standalone = summary.RelativePath.Equals("Todos.md", StringComparison.OrdinalIgnoreCase);

                for (var i = 0; i < lines.Count; i++)
                {
                    var m = TaskLine.Match(lines[i]);
                    if (!m.Success) continue;
                    if (m.Groups[2].Value is "x" or "X") continue;
                    var attrs = ParseAttrs(m.Groups[4].Success ? m.Groups[4].Value : "");
                    var id = attrs.GetValueOrDefault("id");
                    if (string.IsNullOrWhiteSpace(id)) id = $"{summary.Id:N}:{i}";
                    if (!string.Equals(id, taskId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals($"{summary.Id:N}:{i}", taskId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (standalone)
                    {
                        lines.RemoveAt(i);
                    }
                    else
                    {
                        var indent = m.Groups[1].Value;
                        var rest = m.Groups[3].Value;
                        var commentIdx = rest.IndexOf("<!--", StringComparison.Ordinal);
                        if (commentIdx >= 0) rest = rest[..commentIdx].TrimEnd();
                        var comment = BuildTaskComment(MergeAttrs(attrs, id!, null, null, null, null));
                        lines[i] = $"{indent}- [x] {rest.TrimEnd()}{comment}";
                    }

                    var save = SaveLines(summary.Id, note, lines);
                    if (!save.Success)
                        return new VaultTaskActionResult { Success = false, Error = save.Error ?? "Save failed" };
                    return new VaultTaskActionResult { Success = true, NoteId = summary.Id };
                }
            }

            return new VaultTaskActionResult { Success = false, Error = "Task not found" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Complete task failed");
            return new VaultTaskActionResult { Success = false, Error = ex.Message };
        }
    }

    public VaultTaskActionResult Update(string taskId, VaultTaskUpdate patch)
    {
        try
        {
            foreach (var summary in _vault.ListNotes("", includeStandaloneTodosMd: true))
            {
                // Standalone Todos.md is edited via the note PUT / Todos rail, not this path.
                if (summary.RelativePath.Equals("Todos.md", StringComparison.OrdinalIgnoreCase))
                    continue;

                var note = _vault.GetNote(summary.Id);
                if (note is null) continue;
                var lines = note.Markdown.Replace("\r\n", "\n").Split('\n').ToList();

                for (var i = 0; i < lines.Count; i++)
                {
                    var m = TaskLine.Match(lines[i]);
                    if (!m.Success) continue;
                    if (m.Groups[2].Value is "x" or "X") continue;
                    var attrs = ParseAttrs(m.Groups[4].Success ? m.Groups[4].Value : "");
                    var id = attrs.GetValueOrDefault("id");
                    if (string.IsNullOrWhiteSpace(id)) id = $"{summary.Id:N}:{i}";
                    if (!string.Equals(id, taskId, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals($"{summary.Id:N}:{i}", taskId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Promote synthetic line ids to a stable uuid written into the note.
                    if (id!.Contains(':') && Guid.TryParseExact(id.Split(':')[0], "N", out _))
                        id = Guid.NewGuid().ToString("D");

                    var text = patch.Text ?? StripTitleComment(m.Groups[3].Value);
                    if (string.IsNullOrWhiteSpace(text))
                        return new VaultTaskActionResult { Success = false, Error = "Text required" };

                    var merged = MergeAttrs(attrs, id, patch.Priority, patch.Due, patch.Remind, text);
                    var indent = m.Groups[1].Value;
                    var check = m.Groups[2].Value;
                    var comment = BuildTaskComment(merged);
                    lines[i] = $"{indent}- [{check}] {text.Trim()}{comment}";

                    var save = SaveLines(summary.Id, note, lines);
                    if (!save.Success)
                        return new VaultTaskActionResult { Success = false, Error = save.Error ?? "Save failed" };
                    return new VaultTaskActionResult { Success = true, NoteId = summary.Id };
                }
            }

            return new VaultTaskActionResult { Success = false, Error = "Task not found" };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update task failed");
            return new VaultTaskActionResult { Success = false, Error = ex.Message };
        }
    }

    private NoteSaveResult SaveLines(Guid noteId, NoteDetail note, List<string> lines)
    {
        var next = string.Join("\n", lines);
        if (note.Markdown.Contains("\r\n", StringComparison.Ordinal))
            next = next.Replace("\n", "\r\n", StringComparison.Ordinal);
        return _commands.Save(noteId, next, note.ETag);
    }

    private static string StripTitleComment(string raw)
    {
        var titlePart = raw.Trim();
        var commentIdx = titlePart.IndexOf("<!--", StringComparison.Ordinal);
        if (commentIdx >= 0) titlePart = titlePart[..commentIdx].TrimEnd();
        return titlePart;
    }

    private static Dictionary<string, string> MergeAttrs(
        Dictionary<string, string> existing,
        string id,
        string? priority,
        string? due,
        string? remind,
        string? _)
    {
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id
        };
        if (priority is not null)
        {
            var p = priority.Trim().ToLowerInvariant();
            if (p is "low" or "normal" or "high" or "critical")
                merged["priority"] = p;
        }
        else if (!merged.ContainsKey("priority"))
            merged["priority"] = "normal";

        if (due is not null)
        {
            if (string.IsNullOrWhiteSpace(due))
                merged.Remove("due");
            else
                merged["due"] = due.Trim();
        }

        if (remind is not null)
        {
            if (string.IsNullOrWhiteSpace(remind) || remind.Equals("off", StringComparison.OrdinalIgnoreCase))
                merged.Remove("remind");
            else
                merged["remind"] = remind.Trim();
        }

        return merged;
    }

    private static string BuildTaskComment(Dictionary<string, string> attrs)
    {
        var sb = new System.Text.StringBuilder(" <!-- jotdex-task");
        void Append(string key)
        {
            if (!attrs.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) return;
            sb.Append(' ').Append(key).Append("=\"").Append(v.Replace("\"", "", StringComparison.Ordinal)).Append('"');
        }
        Append("id");
        Append("priority");
        Append("due");
        Append("remind");
        Append("added");
        sb.Append(" -->");
        return sb.ToString();
    }

    private static int PriorityRank(string p) => p.ToLowerInvariant() switch
    {
        "critical" => 0,
        "high" => 1,
        "low" => 3,
        _ => 2
    };

    private static Dictionary<string, string> ParseAttrs(string raw)
    {
        var outDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(raw, @"(\w+)=""([^""]*)"""))
            outDict[m.Groups[1].Value] = m.Groups[2].Value;
        return outDict;
    }
}
