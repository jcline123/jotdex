namespace Jotdex.Infrastructure.Export;

/// <summary>Options for self-contained Share HTML. Defaults match the browser Share HTML download.</summary>
public sealed class NoteShareExportOptions
{
    public string Theme { get; init; } = "light";
    public bool IncludeTitle { get; init; } = true;
}
