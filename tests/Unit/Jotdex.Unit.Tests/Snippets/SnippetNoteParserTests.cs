using Jotdex.Core.Snippets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Snippets;
using Jotdex.Infrastructure.Vault;

namespace Jotdex.Unit.Tests.Snippets;

public class SnippetNoteParserTests
{
    [Fact]
    public void IsSnippet_true_when_type_key_matches()
    {
        var fields = new Dictionary<string, string?> { [SnippetConstants.TypeKey] = SnippetConstants.TypeValue };
        Assert.True(SnippetNoteParser.IsSnippet(fields));
    }

    [Fact]
    public void FromNote_extracts_trigger_language_and_code()
    {
        var markdown = """
            ---
            jotdex_type: code-snippet
            jotdex_trigger: get-date
            jotdex_language: powershell
            ---

            # Get current date

            Quick one-liner for today's date.

            ```powershell
            Get-Date -Format 'yyyy-MM-dd'
            ```
            """;

        var parsed = FrontMatterParser.Parse(markdown);
        var note = new NoteDetail
        {
            Id = Guid.NewGuid(),
            Title = "Get current date",
            Markdown = markdown,
            Html = "",
            ETag = "test",
            FolderPath = "/Snippets",
            RelativePath = "Snippets/Get current date.md",
            FrontMatter = parsed.Fields,
        };

        var snippet = SnippetNoteParser.FromNote(note);

        Assert.NotNull(snippet);
        Assert.Equal("get-date", snippet!.Trigger);
        Assert.Equal("powershell", snippet.Language);
        Assert.Contains("Get-Date", snippet.Code);
        Assert.Contains("Quick one-liner", snippet.Description);
    }

    [Fact]
    public void FromNote_returns_null_when_not_snippet()
    {
        var note = new NoteDetail
        {
            Id = Guid.NewGuid(),
            Title = "Regular note",
            Markdown = "Hello",
            Html = "",
            ETag = "test",
            FolderPath = "",
            RelativePath = "Regular note.md",
        };

        Assert.Null(SnippetNoteParser.FromNote(note));
    }
}
