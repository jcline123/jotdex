using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jotdex.Core.Integrations;
using Jotdex.Infrastructure.Export;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jotdex.Tests.Smoke;

public class IntegrationExportAttachApiSmokeTests : IDisposable
{
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    private static readonly byte[] TinyJpeg =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9
    ];
    private static readonly byte[] TinyPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");

    private readonly string _dataRoot;
    private readonly string _vaultRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Guid _noteId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    public IntegrationExportAttachApiSmokeTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "jotdex-int-export-" + Guid.NewGuid().ToString("N"));
        _vaultRoot = Path.Combine(_dataRoot, "vault");
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Inbox"));
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Secret"));
        File.WriteAllText(Path.Combine(_vaultRoot, ".notes-vault.json"), """{"id":"export-vault-1","name":"Export"}""");

        var assets = Path.Combine(_vaultRoot, "Inbox", "Note.assets");
        Directory.CreateDirectory(assets);
        File.WriteAllBytes(Path.Combine(assets, "pixel.png"), TinyPng);

        File.WriteAllText(
            Path.Combine(_vaultRoot, "Inbox", "Note.md"),
            """
            ---
            id: dddddddd-dddd-dddd-dddd-dddddddddddd
            title: Note
            jotdex_updated_via: api
            jotdex_updated_by: Bot
            ---

            # Note

            > [!note] Callout
            > Body

            ```powershell
            Get-Date
            ```

            | A | B |
            |---|---|
            | 1 | 2 |

            - [ ] Task item

            ![pixel](Note.assets/pixel.png)
            """);
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Secret", "Hidden.md"),
            """
            ---
            id: eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee
            title: Hidden
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
                    ["Jotdex:Auth:BypassInDevelopment"] = "false",
                    ["Jotdex:MaxAttachmentBytes"] = "1048576"
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

    private HttpClient Bearer(bool wholeVault, params string[] scopes)
    {
        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var c = cfg.Get();
        c.Enabled = true;
        cfg.Save(c);
        var store = _factory.Services.GetRequiredService<IIntegrationTokenStore>();
        var created = store.Create(new IntegrationTokenCreateRequest
        {
            Name = "ExportBot",
            Scopes = scopes,
            AllowedFolderRoots = wholeVault ? [] : ["Inbox"],
            WholeVault = wholeVault,
            NeverExpires = true
        }, "export-vault-1", TimeProvider.System);
        Assert.True(created.Success, created.Error);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.PlaintextSecret);
        return client;
    }

    [Fact]
    public async Task Export_html_matches_share_service_and_enforces_scope_acl()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });

        using var noScope = Bearer(true, IntegrationScopes.AttachmentsRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await noScope.GetAsync($"/api/integrations/v1/notes/{_noteId}/export")).StatusCode);

        using var scoped = Bearer(false, IntegrationScopes.NotesRead);
        Assert.Equal(HttpStatusCode.NotFound,
            (await scoped.GetAsync("/api/integrations/v1/notes/eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee/export")).StatusCode);

        using var api = Bearer(true, IntegrationScopes.NotesRead);
        var res = await api.GetAsync($"/api/integrations/v1/notes/{_noteId}/export?format=html");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
        Assert.True(res.Content.Headers.ContentDisposition?.DispositionType == "attachment");
        Assert.True(res.Headers.Contains("X-Jotdex-Etag"));
        var html = await res.Content.ReadAsStringAsync();

        var share = _factory.Services.GetRequiredService<INoteShareExportService>();
        var expected = share.ExportSelfContainedHtml(_noteId);
        Assert.True(expected.Success);
        Assert.Equal(expected.Html, html);
        Assert.Contains("data:image/png;base64,", html);
        Assert.Contains("Get-Date", html);
        Assert.Contains("Callout", html);
        Assert.Contains("Task item", html);
        Assert.DoesNotContain("jotdex_updated_via", html);

        var md = await api.GetAsync($"/api/integrations/v1/notes/{_noteId}/export?format=md");
        Assert.Equal(HttpStatusCode.OK, md.StatusCode);
        var mdBody = await md.Content.ReadAsStringAsync();
        Assert.Contains("![pixel]", mdBody);
        Assert.DoesNotContain("jotdex_updated_via", mdBody);
    }

    [Fact]
    public async Task Upload_png_jpg_pdf_idempotency_placement_and_list()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });

        using var noWrite = Bearer(true, IntegrationScopes.NotesRead, IntegrationScopes.AttachmentsRead);
        var denied = await Upload(noWrite, TinyPng, "a.png", "image/png", placement: "none");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var api = Bearer(true, IntegrationScopes.NotesRead, IntegrationScopes.AttachmentsRead, IntegrationScopes.AttachmentsWrite,
            IntegrationScopes.NotesAppend, IntegrationScopes.NotesInsert);

        var key = Guid.NewGuid().ToString("N");
        var r1 = await Upload(api, TinyPng, "dot.png", "image/png", placement: "none", idem: key);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var body1 = await r1.Content.ReadAsStringAsync();
        var j1 = JsonSerializer.Deserialize<JsonElement>(body1);
        Assert.StartsWith("![", j1.GetProperty("markdown").GetString());
        Assert.Contains(".assets/", j1.GetProperty("markdown").GetString());

        var r2 = await Upload(api, TinyPng, "dot.png", "image/png", placement: "none", idem: key);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        Assert.Equal(body1, await r2.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, (await Upload(api, TinyJpeg, "pic.jpg", "image/jpeg")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Upload(api, TinyPdf, "doc.pdf", "application/pdf")).StatusCode);

        // Mismatched: PNG bytes as .jpg
        var bad = await Upload(api, TinyPng, "lie.jpg", "image/jpeg");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Oversized vs configured 1 MiB
        var huge = new byte[1_100_000];
        Buffer.BlockCopy(TinyPng, 0, huge, 0, TinyPng.Length);
        var over = await Upload(api, huge, "big.png", "image/png");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, over.StatusCode);

        var note = await api.GetFromJsonAsync<JsonElement>($"/api/integrations/v1/notes/{_noteId}");
        var etag = note.GetProperty("etag").GetString()!;
        var bodyBefore = note.GetProperty("bodyMarkdown").GetString()!;

        var missing = await Upload(api, TinyPng, "place.png", "image/png", placement: "append");
        Assert.Equal((HttpStatusCode)428, missing.StatusCode);

        var stale = await Upload(api, TinyPng, "place.png", "image/png", placement: "append", ifMatch: "\"deadbeef\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var placed = await Upload(api, TinyPng, "place.png", "image/png", placement: "append", ifMatch: etag);
        Assert.Equal(HttpStatusCode.OK, placed.StatusCode);
        var after = await api.GetFromJsonAsync<JsonElement>($"/api/integrations/v1/notes/{_noteId}");
        var bodyAfter = after.GetProperty("bodyMarkdown").GetString()!;
        Assert.Contains("place.png", bodyAfter);
        Assert.Contains("Task item", bodyAfter);
        Assert.Contains("![pixel]", bodyAfter);
        Assert.Contains("Get-Date", bodyAfter);
        Assert.Contains(bodyBefore.Replace("\r\n", "\n").Trim(), bodyAfter.Replace("\r\n", "\n"));

        var list = await api.GetFromJsonAsync<JsonElement>($"/api/integrations/v1/notes/{_noteId}/attachments");
        var names = list.GetProperty("attachments").EnumerateArray().Select(a => a.GetProperty("filename").GetString()!).ToList();
        Assert.Contains(names, n => n.Contains("place", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(names, n => n.Contains("pixel", StringComparison.OrdinalIgnoreCase) || n.Contains("dot", StringComparison.OrdinalIgnoreCase));
        Assert.True(names.Count >= 2);

        var assetsDir = Path.Combine(_vaultRoot, "Inbox", "Note.assets");
        Assert.Contains(Directory.EnumerateFiles(assetsDir), f =>
            Path.GetFileName(f).Contains("place", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(f).Contains("dot", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<HttpResponseMessage> Upload(
        HttpClient api, byte[] bytes, string name, string contentType,
        string placement = "none", string? ifMatch = null, string? idem = null)
    {
        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", name);
        content.Add(new StringContent(placement), "placement");
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/integrations/v1/notes/dddddddd-dddd-dddd-dddd-dddddddddddd/attachments")
        {
            Content = content
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", idem ?? Guid.NewGuid().ToString("N"));
        if (ifMatch is not null)
            req.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await api.SendAsync(req);
    }
}
