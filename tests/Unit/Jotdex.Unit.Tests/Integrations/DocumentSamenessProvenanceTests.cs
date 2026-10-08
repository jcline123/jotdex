using Jotdex.Core.Markdown;

namespace Jotdex.Unit.Tests.Integrations;

public class DocumentSamenessProvenanceTests
{
    [Fact]
    public void Ignores_server_owned_provenance_and_modified()
    {
        const string a = """
            ---
            id: 11111111-1111-1111-1111-111111111111
            title: Hi
            modified: 2026-01-01T00:00:00Z
            jotdex_updated_via: ui
            jotdex_updated_by: Josh
            jotdex_provenance_hash: abc
            ---

            Body
            """;
        const string b = """
            ---
            id: 11111111-1111-1111-1111-111111111111
            title: Hi
            modified: 2026-10-08T12:00:00Z
            jotdex_updated_via: api
            jotdex_updated_by: Grok Bot
            jotdex_provenance_hash: def
            ---

            Body
            """;
        Assert.True(DocumentSameness.EqualsExactSave(a, b));
    }

    [Fact]
    public void Body_change_is_not_same()
    {
        const string a = "---\ntitle: Hi\n---\n\nA\n";
        const string b = "---\ntitle: Hi\n---\n\nB\n";
        Assert.False(DocumentSameness.EqualsExactSave(a, b));
    }
}
