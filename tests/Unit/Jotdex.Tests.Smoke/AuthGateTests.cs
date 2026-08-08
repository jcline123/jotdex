using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Jotdex.Tests.Smoke;

public class AuthGateTests : IDisposable
{
    private readonly string _dataRoot;
    private readonly string _vaultRoot;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthGateTests()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "jotdex-authgate-" + Guid.NewGuid().ToString("N"));
        _vaultRoot = Path.Combine(_dataRoot, "vault");
        Directory.CreateDirectory(_vaultRoot);
        File.WriteAllText(Path.Combine(_vaultRoot, "Hello.md"), "---\nid: aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\ntitle: Hello\n---\n\n# Hello\n");

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
        try { _factory.Dispose(); } catch (ObjectDisposedException) { /* host already torn down */ }
        try { Directory.Delete(_dataRoot, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Without_password_notes_api_is_open()
    {
        var status = await _client.GetFromJsonAsync<AuthStatusDto>("/api/auth/status");
        Assert.NotNull(status);
        Assert.False(status!.SetupRequired);
        Assert.False(status.SetupComplete);
        Assert.False(status.AuthRequired);

        var notes = await _client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
    }

    [Fact]
    public async Task After_setup_unauthenticated_cannot_read_notes()
    {
        var setup = await _client.PostAsJsonAsync("/api/auth/setup", new
        {
            password = "correct-horse-battery"
        });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        // New client without cookies
        using var anon = _factory.CreateClient();
        var notes = await anon.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.Unauthorized, notes.StatusCode);

        var health = await anon.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task Login_cookie_allows_notes_access()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new
        {
            password = "correct-horse-battery"
        });
        // setup auto-signs in on this client
        var notes = await _client.GetAsync("/api/notes");
        Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
    }

    [Fact]
    public async Task Remove_password_restores_open_access()
    {
        await _client.PostAsJsonAsync("/api/auth/setup", new { password = "correct-horse-battery" });

        using var anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/notes")).StatusCode);

        var remove = await _client.PostAsJsonAsync("/api/auth/remove-password", new
        {
            currentPassword = "correct-horse-battery"
        });
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await anon.GetAsync("/api/notes")).StatusCode);
        var status = await anon.GetFromJsonAsync<AuthStatusDto>("/api/auth/status");
        Assert.False(status!.SetupComplete);
        Assert.False(status.AuthRequired);
    }

    [Fact]
    public async Task Network_settings_round_trip()
    {
        var put = await _client.PutAsJsonAsync("/api/settings/network", new { bindMode = "lan", port = 5199 });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var get = await _client.GetFromJsonAsync<NetworkDto>("/api/settings/network");
        Assert.NotNull(get);
        Assert.Equal("lan", get!.BindMode);
        Assert.Equal(5199, get.Port);
        Assert.True(get.IsLan);
    }

    private sealed class AuthStatusDto
    {
        public bool SetupComplete { get; set; }
        public bool SetupRequired { get; set; }
        public bool AuthRequired { get; set; }
        public bool DevelopmentBypass { get; set; }
    }

    private sealed class NetworkDto
    {
        public string BindMode { get; set; } = "";
        public int Port { get; set; }
        public bool IsLan { get; set; }
    }
}
