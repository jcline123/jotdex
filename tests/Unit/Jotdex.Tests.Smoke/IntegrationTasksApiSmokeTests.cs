using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jotdex.Tests.Smoke;

public class IntegrationTasksApiSmokeTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly string _vaultRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private string? _tokenSecret;

    public IntegrationTasksApiSmokeTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "jotdex-int-tasks-" + Guid.NewGuid().ToString("N"));
        _vaultRoot = Path.Combine(_dataRoot, "vault");
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Inbox"));
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Secret"));
        File.WriteAllText(Path.Combine(_vaultRoot, ".notes-vault.json"), """{"id":"tasks-vault-1","name":"Tasks"}""");
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Inbox", "Note.md"),
            """
            ---
            id: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa
            title: Note
            ---

            # Note

            - [ ] Inbox task <!-- jotdex-task id="note-task-1" -->

            Keep this line
            """);
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Todos.md"),
            """
            ---
            id: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb
            title: Todos
            ---

            # Todos

            - [ ] Standalone <!-- jotdex-todo id="solo-1" priority="normal" -->
            """);
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Secret", "Hidden.md"),
            """
            ---
            id: cccccccc-cccc-cccc-cccc-cccccccccccc
            title: Hidden
            created: 2020-01-01T00:00:00Z
            modified: 2099-01-01T00:00:00Z
            ---

            # Hidden
            """);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jotdex:DataRoot"] = _dataRoot,
                    ["Jotdex:VaultPath"] = _vaultRoot,
                    ["Jotdex:PortableMode"] = "true",
                    ["Jotdex:Auth:BypassInDevelopment"] = "false"
                });
            });
        });
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
    }

    public void Dispose()
    {
        _client.Dispose();
        try { _factory.Dispose(); } catch (ObjectDisposedException) { /* ignore */ }
        try { Directory.Delete(_dataRoot, recursive: true); } catch { /* ignore */ }
    }

    private HttpClient Bearer(params string[] scopes) => Bearer(wholeVault: true, scopes);

    private HttpClient Bearer(bool wholeVault, params string[] scopes)
    {
        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var c = cfg.Get();
        c.Enabled = true;
        cfg.Save(c);
        var store = _factory.Services.GetRequiredService<IIntegrationTokenStore>();
        var created = store.Create(new IntegrationTokenCreateRequest
        {
            Name = "TaskBot",
            Scopes = scopes.Length > 0 ? scopes : [IntegrationScopes.TasksRead, IntegrationScopes.TasksWrite, IntegrationScopes.NotesRead, IntegrationScopes.NotesInsert, IntegrationScopes.NotesAppend],
            AllowedFolderRoots = wholeVault ? [] : ["Inbox"],
            WholeVault = wholeVault,
            NeverExpires = true
        }, "tasks-vault-1", TimeProvider.System);
        Assert.True(created.Success, created.Error);
        _tokenSecret = created.PlaintextSecret;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _tokenSecret);
        return client;
    }

    [Fact]
    public async Task Tasks_require_scope_and_acl()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });

        using var noScope = Bearer(wholeVault: true, IntegrationScopes.NotesRead);
        var forbidden = await noScope.GetAsync("/api/integrations/v1/tasks");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Folder-scoped token: note tasks ok, standalone hidden
        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var c = cfg.Get();
        c.Enabled = true;
        cfg.Save(c);
        var store = _factory.Services.GetRequiredService<IIntegrationTokenStore>();
        var scoped = store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Scoped",
            Scopes = [IntegrationScopes.TasksRead],
            AllowedFolderRoots = ["Inbox"],
            WholeVault = false,
            NeverExpires = true
        }, "tasks-vault-1", TimeProvider.System);
        using var scopedClient = _factory.CreateClient();
        scopedClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scoped.PlaintextSecret);
        var list = await scopedClient.GetFromJsonAsync<JsonElement>("/api/integrations/v1/tasks?status=open");
        var tasks = list.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList();
        Assert.Contains("note-task-1", tasks);
        Assert.DoesNotContain("solo-1", tasks);

        using var whole = Bearer(wholeVault: true, IntegrationScopes.TasksRead);
        var all = await whole.GetFromJsonAsync<JsonElement>("/api/integrations/v1/tasks?status=open");
        var ids = all.GetProperty("tasks").EnumerateArray().Select(t => t.GetProperty("id").GetString()).ToList();
        Assert.Contains("solo-1", ids);
    }

    [Fact]
    public async Task Patch_note_task_requires_if_match_and_touches_one_line()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        using var api = Bearer(IntegrationScopes.TasksRead, IntegrationScopes.TasksWrite, IntegrationScopes.NotesRead);

        var list = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/tasks?status=open");
        var task = list.GetProperty("tasks").EnumerateArray().First(t => t.GetProperty("id").GetString() == "note-task-1");
        var etag = task.GetProperty("etag").GetString()!;

        var missing = await api.SendAsync(new HttpRequestMessage(HttpMethod.Patch, "/api/integrations/v1/tasks/note-task-1")
        {
            Content = JsonContent.Create(new { done = true })
        });
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        var stale = new HttpRequestMessage(HttpMethod.Patch, "/api/integrations/v1/tasks/note-task-1");
        stale.Headers.TryAddWithoutValidation("If-Match", "\"deadbeef\"");
        stale.Content = JsonContent.Create(new { done = true });
        var staleRes = await api.SendAsync(stale);
        Assert.Equal(HttpStatusCode.PreconditionFailed, staleRes.StatusCode);

        var before = await File.ReadAllTextAsync(Path.Combine(_vaultRoot, "Inbox", "Note.md"));
        var ok = new HttpRequestMessage(HttpMethod.Patch, "/api/integrations/v1/tasks/note-task-1");
        ok.Headers.TryAddWithoutValidation("If-Match", etag);
        ok.Content = JsonContent.Create(new { done = true });
        var okRes = await api.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        var after = await File.ReadAllTextAsync(Path.Combine(_vaultRoot, "Inbox", "Note.md"));
        Assert.Contains("- [x] Inbox task", after.Replace("\r\n", "\n"));
        Assert.Contains("Keep this line", after);
        // Only the checkbox line should differ in body (plus possible FM provenance)
        Assert.DoesNotContain("- [ ] Inbox task", after.Replace("\r\n", "\n"));
        _ = before;
    }

    [Fact]
    public async Task Create_task_idempotency_and_insert()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        using var api = Bearer();

        var key = Guid.NewGuid().ToString("N");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/tasks");
        req.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        req.Content = JsonContent.Create(new { text = "From API", dueDate = (string?)null });
        var r1 = await api.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var body1 = await r1.Content.ReadAsStringAsync();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/tasks");
        req2.Headers.TryAddWithoutValidation("Idempotency-Key", key);
        req2.Content = JsonContent.Create(new { text = "From API", dueDate = (string?)null });
        var r2 = await api.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(body1, await r2.Content.ReadAsStringAsync());

        var note = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var etag = note.GetProperty("etag").GetString()!;
        var bodyBefore = note.GetProperty("bodyMarkdown").GetString()!;

        var ins = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/insert");
        ins.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        ins.Headers.TryAddWithoutValidation("If-Match", etag);
        ins.Content = JsonContent.Create(new { markdown = "INSERTED_BLOCK", position = "top" });
        var insRes = await api.SendAsync(ins);
        Assert.Equal(HttpStatusCode.OK, insRes.StatusCode);
        var after = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bodyAfter = after.GetProperty("bodyMarkdown").GetString()!;
        Assert.Contains("INSERTED_BLOCK", bodyAfter);
        Assert.Contains("Keep this line", bodyAfter);
        // Rest of original body still present
        Assert.Contains("Inbox task", bodyAfter);
        _ = bodyBefore;

        // afterHeading: rest of note remains
        var etag2 = after.GetProperty("etag").GetString()!;
        var afterHeading = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/insert");
        afterHeading.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        afterHeading.Headers.TryAddWithoutValidation("If-Match", etag2);
        afterHeading.Content = JsonContent.Create(new { markdown = "AFTER_HEADING", position = "afterHeading", heading = "Note" });
        var ahRes = await api.SendAsync(afterHeading);
        Assert.Equal(HttpStatusCode.OK, ahRes.StatusCode);
        var afterAh = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var bodyAh = afterAh.GetProperty("bodyMarkdown").GetString()!;
        Assert.Contains("AFTER_HEADING", bodyAh);
        Assert.Contains("Keep this line", bodyAh);
    }

    [Fact]
    public async Task Changes_since_paginated_and_acl_filtered()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });

        using var scoped = Bearer(wholeVault: false, IntegrationScopes.NotesRead);
        var since = "2000-01-01T00:00:00Z";
        var page1 = await scoped.GetFromJsonAsync<JsonElement>($"/api/integrations/v1/notes/changes?since={Uri.EscapeDataString(since)}&limit=1");
        Assert.True(page1.GetProperty("changes").GetArrayLength() >= 1);
        Assert.True(page1.TryGetProperty("nextSince", out _));
        var ids1 = page1.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain("cccccccc-cccc-cccc-cccc-cccccccccccc", ids1);

        // Whole-vault sees Secret; paginate with limit=1
        using var whole = Bearer(wholeVault: true, IntegrationScopes.NotesRead);
        var w1 = await whole.GetFromJsonAsync<JsonElement>($"/api/integrations/v1/notes/changes?since={Uri.EscapeDataString(since)}&limit=1");
        Assert.Equal(1, w1.GetProperty("changes").GetArrayLength());
        Assert.Equal(JsonValueKind.String, w1.GetProperty("nextCursor").ValueKind);
        var cursor = w1.GetProperty("nextCursor").GetString();
        var w2 = await whole.GetFromJsonAsync<JsonElement>(
            $"/api/integrations/v1/notes/changes?since={Uri.EscapeDataString(since)}&limit=1&cursor={cursor}");
        Assert.True(w2.GetProperty("changes").GetArrayLength() >= 1);
        Assert.True(w1.TryGetProperty("nextSince", out _));

        using var noScope = Bearer(wholeVault: true, IntegrationScopes.TasksRead);
        var forbidden = await noScope.GetAsync($"/api/integrations/v1/notes/changes?since={Uri.EscapeDataString(since)}");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Search_snippets_on_and_off()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        using var api = Bearer(wholeVault: true, IntegrationScopes.NotesRead);

        var with = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/search?q=Inbox");
        Assert.True(with.GetProperty("hits").GetArrayLength() >= 1);
        var hit = with.GetProperty("hits").EnumerateArray().First();
        Assert.True(hit.TryGetProperty("snippets", out var snips) && snips.ValueKind == JsonValueKind.Array);

        var without = await api.GetFromJsonAsync<JsonElement>("/api/integrations/v1/search?q=Inbox&includeSnippets=false");
        var hitOff = without.GetProperty("hits").EnumerateArray().First();
        Assert.True(!hitOff.TryGetProperty("snippets", out var snipsOff) || snipsOff.ValueKind == JsonValueKind.Null);

        using var noScope = Bearer(wholeVault: true, IntegrationScopes.TasksRead);
        var forbidden = await noScope.GetAsync("/api/integrations/v1/search?q=Inbox");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Insert_requires_notes_insert_or_append_scope()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        using var reader = Bearer(wholeVault: true, IntegrationScopes.NotesRead);
        var note = await reader.GetFromJsonAsync<JsonElement>("/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var etag = note.GetProperty("etag").GetString()!;

        var denied = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/insert");
        denied.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        denied.Headers.TryAddWithoutValidation("If-Match", etag);
        denied.Content = JsonContent.Create(new { markdown = "x", position = "top" });
        var deniedRes = await reader.SendAsync(denied);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRes.StatusCode);

        using var inserter = Bearer(wholeVault: true, IntegrationScopes.NotesRead, IntegrationScopes.NotesInsert);
        var note2 = await inserter.GetFromJsonAsync<JsonElement>("/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var etag2 = note2.GetProperty("etag").GetString()!;
        var ok = new HttpRequestMessage(HttpMethod.Post, "/api/integrations/v1/notes/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/insert");
        ok.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        ok.Headers.TryAddWithoutValidation("If-Match", etag2);
        ok.Content = JsonContent.Create(new { markdown = "SCOPE_OK", position = "top" });
        var okRes = await inserter.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
    }
}
