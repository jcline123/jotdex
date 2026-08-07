using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jotdex.Tests.Smoke;

public class NoteSaveTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public NoteSaveTests(WebApplicationFactory<Program> factory)
    {
        _client = TestHost.CreateClient(factory);
    }

    [Fact]
    public async Task Save_is_atomic_and_creates_history()
    {
        var title = "Autosave " + Guid.NewGuid().ToString("N")[..6];
        var created = await _client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = $"# {title}\n\noriginal\n"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);

        try
        {
            var save = await _client.PutAsJsonAsync($"/api/notes/{note!.Id}", new
            {
                markdown = note.Markdown.Replace("original", "updated-once", StringComparison.Ordinal),
                etag = note.Etag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<SaveDto>();
            Assert.NotNull(saved);
            Assert.True(saved!.Success);
            Assert.False(string.IsNullOrWhiteSpace(saved.Etag));

            var history = await _client.GetFromJsonAsync<List<HistDto>>($"/api/notes/{note.Id}/history");
            Assert.NotNull(history);
            Assert.NotEmpty(history!);

            // conflict
            var conflict = await _client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = "stale",
                etag = "deadbeef"
            });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        finally
        {
            await _client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    private sealed class NoteDto
    {
        public Guid Id { get; set; }
        public string Markdown { get; set; } = "";
        public string Etag { get; set; } = "";
    }

    private sealed class SaveDto
    {
        public bool Success { get; set; }
        public string Etag { get; set; } = "";
    }

    private sealed class HistDto
    {
        public string SnapshotId { get; set; } = "";
    }
}
