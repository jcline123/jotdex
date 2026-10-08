using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Jotdex.Core.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Jotdex.Tests.Smoke;

public class IntegrationAuthSmokeTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly string _vaultRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public IntegrationAuthSmokeTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "jotdex-int-auth-" + Guid.NewGuid().ToString("N"));
        _vaultRoot = Path.Combine(_dataRoot, "vault");
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Inbox"));
        Directory.CreateDirectory(Path.Combine(_vaultRoot, "Secret"));
        File.WriteAllText(Path.Combine(_vaultRoot, ".notes-vault.json"), """{"id":"smoke-vault-1","name":"Smoke"}""");
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Inbox", "Hello.md"),
            "---\nid: bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb\ntitle: Hello\n---\n\n# Hello\n");
        File.WriteAllText(
            Path.Combine(_vaultRoot, "Secret", "Hidden.md"),
            "---\nid: cccccccc-cccc-cccc-cccc-cccccccccccc\ntitle: Hidden Secret Title\n---\n\nsecret\n");

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

    [Fact]
    public async Task Integration_path_rejects_cookie_and_notes_reject_bearer()
    {
        var setup = await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        // Cookie alone cannot call integrations API
        var who = await _client.GetAsync("/api/integrations/v1/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, who.StatusCode);

        // Enable + issue token via store (bypass CSRF for unit of auth matrix)
        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var c = cfg.Get();
        c.Enabled = true;
        cfg.Save(c);
        var store = _factory.Services.GetRequiredService<IIntegrationTokenStore>();
        var created = store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Bot",
            Scopes = IntegrationScopes.PresetReadOnly,
            AllowedFolderRoots = ["Inbox"],
            ExpirationDays = 30
        }, "smoke-vault-1", TimeProvider.System);
        Assert.True(created.Success, created.Error);

        using var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", created.PlaintextSecret);

        var whoOk = await bearer.GetAsync("/api/integrations/v1/whoami");
        Assert.Equal(HttpStatusCode.OK, whoOk.StatusCode);

        // Bearer must not unlock browser notes API
        var notes = await bearer.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, notes.StatusCode);

        // Admin integrations rejects bearer
        var admin = await bearer.GetAsync("/api/admin/integrations");
        Assert.Equal(HttpStatusCode.Unauthorized, admin.StatusCode);

        // Dev open-access does not unlock integrations when password removed — feature stays gated
        cfg.Save(new IntegrationConfig { Enabled = false });
        var disabled = await bearer.GetAsync("/api/integrations/v1/whoami");
        Assert.Equal(HttpStatusCode.Unauthorized, disabled.StatusCode);
    }

    [Fact]
    public async Task Out_of_scope_note_is_404_not_title_leak()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });
        var cfg = _factory.Services.GetRequiredService<IIntegrationConfigService>();
        var c = cfg.Get();
        c.Enabled = true;
        cfg.Save(c);
        var store = _factory.Services.GetRequiredService<IIntegrationTokenStore>();

        var created = store.Create(new IntegrationTokenCreateRequest
        {
            Name = "Scoped",
            Scopes = IntegrationScopes.PresetReadOnly,
            AllowedFolderRoots = ["Inbox"],
            ExpirationDays = 30
        }, "smoke-vault-1", TimeProvider.System);

        using var bearer = _factory.CreateClient();
        bearer.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", created.PlaintextSecret);

        var r = await bearer.GetAsync("/api/integrations/v1/notes/cccccccc-cccc-cccc-cccc-cccccccccccc");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        var body = await r.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Hidden Secret Title", body, StringComparison.Ordinal);
    }
}
