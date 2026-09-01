using System.Text.Json;
using Jotdex.Core.Markdown;

namespace Jotdex.Unit.Tests.Editor;

public sealed class DocumentSamenessTests
{
    [Fact]
    public void Shared_vectors_match_exact_save_equivalence()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "document-sameness-vectors.json");
        Assert.True(File.Exists(path), path);
        using var stream = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<VectorFile>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        Assert.NotNull(data?.Vectors);
        foreach (var v in data!.Vectors)
        {
            Assert.Equal(v.ExactSaveEqual, DocumentSameness.EqualsExactSave(v.A, v.B));
        }
    }

    private sealed class VectorFile
    {
        public List<Vector> Vectors { get; set; } = [];
    }

    private sealed class Vector
    {
        public string Id { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public bool ExactSaveEqual { get; set; }
    }
}
