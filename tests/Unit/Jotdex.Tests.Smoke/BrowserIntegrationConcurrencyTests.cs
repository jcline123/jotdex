using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jotdex.Core.Integrations;
using Jotdex.Infrastructure.Vault;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jotdex.Tests.Smoke;

/// <summary>
/// Exercises the real cookie-authenticated browser save and bearer-authenticated
/// Integrations append endpoints on one host. No development auth bypass or live vault.
/// </summary>
public class BrowserIntegrationConcurrencyTests : IDisposable
{
    private const string NoteId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "jotdex-browser-api-" + Guid.NewGuid().ToString("N"));
    private readonly string _notePath;
    private readonly WebApplicationFactory<Program> _factory;

    public BrowserIntegrationConcurrencyTests()
    {
        var vaultRoot = Path.Combine(_dataRoot, "vault");
        Directory.CreateDirectory(Path.Combine(vaultRoot, "Inbox"));
        File.WriteAllText(Path.Combine(vaultRoot, ".notes-vault.json"),
            """{"id":"browser-api-vault","name":"Concurrency"}""");
        _notePath = Path.Combine(vaultRoot, "Inbox", "Activity.md");
        File.WriteAllText(_notePath, $"---\nid: {NoteId}\ntitle: Activity\n---\n\n# Activity\n\n" +
            string.Join("\n", Enumerable.Range(0, 40).Select(i => $"- prior-{i:D2}")) + "\n");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jotdex:DataRoot"] = _dataRoot,
                ["Jotdex:VaultPath"] = vaultRoot,
                ["Jotdex:PortableMode"] = "true",
                ["Jotdex:Auth:BypassInDevelopment"] = "false"
            }));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authenticated_bot_append_survives_stale_browser_autosave(bool browserHasUnsavedEdit)
    {
        using var browser = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        using var setup = await browser.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        // Browser opens the note, then retains its original document and ETag.
        var opened = await browser.GetFromJsonAsync<NoteDto>($"/api/notes/{NoteId}");
        Assert.NotNull(opened);
        Assert.False(string.IsNullOrWhiteSpace(opened.Etag));
        const string localEntry = "- browser-unsaved-entry";
        var staleMarkdown = opened.Markdown + (browserHasUnsavedEdit ? "\n" + localEntry + "\n" : "");

        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var config = cfg.Get();
        config.Enabled = true;
        cfg.Save(config);
        var token = _factory.Services.GetRequiredService<IIntegrationTokenStore>().Create(new IntegrationTokenCreateRequest
        {
            Name = "Grok Bot",
            Scopes = [IntegrationScopes.NotesRead, IntegrationScopes.NotesAppend],
            AllowedFolderRoots = ["Inbox"],
            ExpirationDays = 30
        }, "browser-api-vault", TimeProvider.System);
        Assert.True(token.Success, token.Error);
        using var bot = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        bot.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.PlaintextSecret);
        var botRead = await bot.GetFromJsonAsync<NoteDto>($"/api/integrations/v1/notes/{NoteId}");
        Assert.NotNull(botRead);
        Assert.Equal(opened.Etag, botRead.Etag.Trim('"'));

        const string botEntry = "- Grok Bot: recruiting activity added via authenticated API";
        using var append = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/v1/notes/{NoteId}/append")
        {
            Content = JsonContent.Create(new { bodyMarkdown = botEntry })
        };
        append.Headers.TryAddWithoutValidation("If-Match", botRead.Etag);
        append.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        using var appended = await bot.SendAsync(append);
        Assert.Equal(HttpStatusCode.OK, appended.StatusCode);
        var botSaved = await appended.Content.ReadFromJsonAsync<NoteDto>();
        Assert.NotNull(botSaved);
        Assert.NotEqual(opened.Etag, botSaved.Etag.Trim('"'));
        var afterAppend = await File.ReadAllBytesAsync(_notePath);

        // An unchanged stale buffer and a dirty stale draft must both fail closed.
        // Repeating the autosave must not silently adopt the returned ETag and overwrite.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var stale = await browser.PutAsJsonAsync($"/api/notes/{NoteId}",
                new { markdown = staleMarkdown, etag = opened.Etag });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            var conflict = await stale.Content.ReadFromJsonAsync<SaveDto>();
            Assert.NotNull(conflict);
            Assert.False(conflict.Success);
            Assert.True(conflict.Conflict);
            Assert.False(string.IsNullOrWhiteSpace(conflict.Error));
            Assert.Equal(botSaved.Etag.Trim('"'), conflict.Etag);
            Assert.NotNull(conflict.Note);
            Assert.Equal(conflict.Etag, conflict.Note.Etag);
            AssertLogPreserved(conflict.Note.Markdown, botEntry);
            Assert.DoesNotContain(localEntry, conflict.Note.Markdown, StringComparison.Ordinal);
            Assert.Equal(afterAppend, await File.ReadAllBytesAsync(_notePath));
        }

        // Both readers see the same surviving content; recovery requires a fresh read.
        var reloaded = await browser.GetFromJsonAsync<NoteDto>($"/api/notes/{NoteId}");
        var botReloaded = await bot.GetFromJsonAsync<NoteDto>($"/api/integrations/v1/notes/{NoteId}");
        Assert.NotNull(reloaded);
        Assert.NotNull(botReloaded);
        Assert.Equal(FrontMatterParser.Parse(reloaded.Markdown).Body, botReloaded.BodyMarkdown);
        Assert.Equal(reloaded.Etag, botReloaded.Etag);
        AssertLogPreserved(reloaded.Markdown, botEntry);

        if (browserHasUnsavedEdit)
        {
            // Explicitly merge the held local edit onto the freshly read document,
            // rather than retrying the stale whole document with a newer ETag.
            using var merge = await browser.PutAsJsonAsync($"/api/notes/{NoteId}", new
            {
                markdown = reloaded.Markdown + "\n" + localEntry + "\n",
                etag = reloaded.Etag
            });
            Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
            var saved = await merge.Content.ReadFromJsonAsync<SaveDto>();
            Assert.NotNull(saved);
            Assert.True(saved.Success);
            Assert.False(saved.Conflict);
            var final = await browser.GetFromJsonAsync<NoteDto>($"/api/notes/{NoteId}");
            Assert.NotNull(final);
            AssertLogPreserved(final.Markdown, botEntry);
            Assert.Contains(localEntry, final.Markdown, StringComparison.Ordinal);
            Assert.Equal(final.Markdown, await File.ReadAllTextAsync(_notePath));
        }
    }

    private static void AssertLogPreserved(string markdown, string botEntry)
    {
        for (var i = 0; i < 40; i++)
            Assert.Contains($"- prior-{i:D2}", markdown, StringComparison.Ordinal);
        Assert.Equal(1, markdown.Split(botEntry, StringSplitOptions.None).Length - 1);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_dataRoot, recursive: true);
    }

    private sealed class NoteDto
    {
        public string Markdown { get; set; } = "";
        public string BodyMarkdown { get; set; } = "";
        public string Etag { get; set; } = "";
    }

    private sealed class SaveDto
    {
        public bool Success { get; set; }
        public bool Conflict { get; set; }
        public string Etag { get; set; } = "";
        public string? Error { get; set; }
        public NoteDto? Note { get; set; }
    }
}
