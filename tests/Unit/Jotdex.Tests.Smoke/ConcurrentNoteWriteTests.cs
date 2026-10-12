using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jotdex.Tests.Smoke;

/// <summary>
/// Data-integrity concurrency: stale writers must not silently overwrite newer content.
/// Uses SampleVault via TestHost (isolated data root).
/// </summary>
public class ConcurrentNoteWriteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConcurrentNoteWriteTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Stale_etag_rejected_even_when_vault_cache_lags_disk()
    {
        var client = TestHost.CreateClient(_factory);
        var title = "CacheLag " + Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = $"# {title}\n\nbaseline-line\n"
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        try
        {
            // Mutate the vault file out-of-band (simulates API/other writer before watcher refresh).
            var repo = FindRepoRoot();
            var abs = Path.Combine(repo, "tools", "SampleVault", note!.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(abs), abs);
            var disk = await File.ReadAllTextAsync(abs);
            var external = disk.Replace("baseline-line", "external-writer-won", StringComparison.Ordinal);
            await File.WriteAllTextAsync(abs, external);

            var stale = await client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = disk.Replace("baseline-line", "stale-browser-draft", StringComparison.Ordinal),
                etag = note.Etag
            });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

            var conflict = await stale.Content.ReadFromJsonAsync<SaveDto>();
            Assert.NotNull(conflict);
            Assert.True(conflict!.Conflict);
            Assert.Contains("external-writer-won", conflict.Note?.Markdown ?? "", StringComparison.Ordinal);

            // File on disk must still be the external version.
            var after = await File.ReadAllTextAsync(abs);
            Assert.Contains("external-writer-won", after, StringComparison.Ordinal);
            Assert.DoesNotContain("stale-browser-draft", after, StringComparison.Ordinal);
        }
        finally
        {
            await client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Missing_etag_is_conflict_not_overwrite()
    {
        var client = TestHost.CreateClient(_factory);
        var title = "NoEtag " + Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = $"# {title}\n\nkeep-me\n"
        });
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        try
        {
            var bad = await client.PutAsJsonAsync($"/api/notes/{note!.Id}", new
            {
                markdown = $"# {title}\n\nwiped\n",
                etag = ""
            });
            Assert.Equal(HttpStatusCode.Conflict, bad.StatusCode);
            var body = await client.GetFromJsonAsync<NoteDto>($"/api/notes/{note.Id}");
            Assert.Contains("keep-me", body!.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Two_writers_same_etag_only_one_succeeds()
    {
        var client = TestHost.CreateClient(_factory);
        var title = "Race " + Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = $"# {title}\n\nshared-base\n"
        });
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        Assert.False(string.IsNullOrWhiteSpace(note!.Etag));
        try
        {
            var etag = note.Etag;
            var mdA = WithBody(note.Markdown, $"# {title}\n\nwriter-A\n");
            var mdB = WithBody(note.Markdown, $"# {title}\n\nwriter-B\n");
            // Same host / write coordinator — concurrent HttpClient requests on one client.
            var a = client.PutAsJsonAsync($"/api/notes/{note.Id}", new { markdown = mdA, etag });
            var b = client.PutAsJsonAsync($"/api/notes/{note.Id}", new { markdown = mdB, etag });
            await Task.WhenAll(a, b);
            var ra = await a;
            var rb = await b;
            var okCount = (ra.IsSuccessStatusCode ? 1 : 0) + (rb.IsSuccessStatusCode ? 1 : 0);
            var conflictCount = (ra.StatusCode == HttpStatusCode.Conflict ? 1 : 0)
                              + (rb.StatusCode == HttpStatusCode.Conflict ? 1 : 0);
            Assert.True(
                okCount == 1 && conflictCount == 1,
                $"expected 1 ok + 1 conflict; got A={ra.StatusCode} B={rb.StatusCode} bodies A={await ra.Content.ReadAsStringAsync()} B={await rb.Content.ReadAsStringAsync()}");

            var final = await client.GetFromJsonAsync<NoteDto>($"/api/notes/{note.Id}");
            Assert.NotNull(final);
            var hasA = final!.Markdown.Contains("writer-A", StringComparison.Ordinal);
            var hasB = final.Markdown.Contains("writer-B", StringComparison.Ordinal);
            Assert.True(hasA ^ hasB);
            Assert.DoesNotContain("shared-base", final.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public async Task Activity_log_append_sequence_preserves_all_entries()
    {
        var client = TestHost.CreateClient(_factory);
        var title = "ActLog " + Guid.NewGuid().ToString("N")[..8];
        var seed = new StringBuilder();
        seed.AppendLine($"# {title}");
        seed.AppendLine();
        for (var i = 0; i < 40; i++)
            seed.AppendLine($"- prior-{i:D2}");

        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = seed.ToString()
        });
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        try
        {
            var etag = note!.Etag;
            for (var i = 0; i < 8; i++)
            {
                var marker = $"ENTRY-{i:D2}-{Guid.NewGuid():N}";
                // Full-document replace with current etag (browser-style) growing the log.
                var get = await client.GetFromJsonAsync<NoteDto>($"/api/notes/{note.Id}");
                Assert.NotNull(get);
                etag = get!.Etag;
                var next = get.Markdown.TrimEnd() + "\n\n" + marker + "\n";
                var save = await client.PutAsJsonAsync($"/api/notes/{note.Id}", new { markdown = next, etag });
                Assert.Equal(HttpStatusCode.OK, save.StatusCode);
                var saved = await save.Content.ReadFromJsonAsync<SaveDto>();
                Assert.True(saved!.Success);
                etag = saved.Etag;

                // Stale retry of an earlier shorter body must conflict.
                var stale = await client.PutAsJsonAsync($"/api/notes/{note.Id}", new
                {
                    markdown = seed.ToString(),
                    etag = note.Etag
                });
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            }

            var final = await client.GetFromJsonAsync<NoteDto>($"/api/notes/{note.Id}");
            Assert.NotNull(final);
            for (var i = 0; i < 40; i++)
                Assert.Contains($"prior-{i:D2}", final!.Markdown, StringComparison.Ordinal);
            Assert.Contains("ENTRY-00-", final!.Markdown, StringComparison.Ordinal);
            Assert.Contains("ENTRY-07-", final.Markdown, StringComparison.Ordinal);
        }
        finally
        {
            await client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    [Fact]
    public void Destructive_replace_heuristic_flags_large_shrink()
    {
        var oldBody = new string('x', 5000);
        var tiny = "short";
        Assert.True(Jotdex.Infrastructure.Vault.NoteCommandService.IsUnusuallyDestructiveReplace(oldBody, tiny));
        Assert.False(Jotdex.Infrastructure.Vault.NoteCommandService.IsUnusuallyDestructiveReplace(oldBody, oldBody[..4000]));
        Assert.False(Jotdex.Infrastructure.Vault.NoteCommandService.IsUnusuallyDestructiveReplace("small", ""));
    }

    [Fact]
    public async Task Meta_endpoint_reflects_new_etag_after_save()
    {
        var client = TestHost.CreateClient(_factory);
        var title = "Meta " + Guid.NewGuid().ToString("N")[..8];
        var created = await client.PostAsJsonAsync("/api/notes", new
        {
            title,
            folder = "Technical",
            markdown = $"# {title}\n\nv1\n"
        });
        var note = await created.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(note);
        try
        {
            var meta1 = await client.GetFromJsonAsync<MetaDto>($"/api/notes/{note!.Id}/meta");
            Assert.Equal(note.Etag, meta1!.Etag);

            var save = await client.PutAsJsonAsync($"/api/notes/{note.Id}", new
            {
                markdown = WithBody(note.Markdown, $"# {title}\n\nv2\n"),
                etag = note.Etag
            });
            Assert.Equal(HttpStatusCode.OK, save.StatusCode);
            var saved = await save.Content.ReadFromJsonAsync<SaveDto>();
            Assert.False(string.IsNullOrWhiteSpace(saved!.Etag));

            var meta2 = await client.GetFromJsonAsync<MetaDto>($"/api/notes/{note.Id}/meta");
            Assert.Equal(saved.Etag, meta2!.Etag);
            Assert.NotEqual(note.Etag, meta2.Etag);
        }
        finally
        {
            await client.DeleteAsync($"/api/notes/{note!.Id}");
        }
    }

    /// <summary>Keep YAML front matter; replace only the body so note id survives Rescan.</summary>
    private static string WithBody(string fullMarkdown, string newBody)
    {
        const string sep = "---";
        if (!fullMarkdown.StartsWith(sep, StringComparison.Ordinal))
            return newBody;
        var end = fullMarkdown.IndexOf(sep, 3, StringComparison.Ordinal);
        if (end < 0) return newBody;
        var fm = fullMarkdown[..(end + sep.Length)];
        return fm + "\n\n" + newBody.TrimStart();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Jotdex.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not find Jotdex.sln");
    }

    private sealed class NoteDto
    {
        public Guid Id { get; set; }
        public string Markdown { get; set; } = "";
        public string Etag { get; set; } = "";
        public string RelativePath { get; set; } = "";
    }

    private sealed class SaveDto
    {
        public bool Success { get; set; }
        public bool Conflict { get; set; }
        public string Etag { get; set; } = "";
        public NoteDto? Note { get; set; }
    }

    private sealed class MetaDto
    {
        public string Etag { get; set; } = "";
    }
}
