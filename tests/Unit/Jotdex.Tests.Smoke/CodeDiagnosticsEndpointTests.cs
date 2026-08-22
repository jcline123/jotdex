using System.Net;
using System.Net.Http.Json;
using Jotdex.Tests.Smoke;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Jotdex.Tests.Smoke;

public class CodeDiagnosticsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CodeDiagnosticsEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task PowerShell_valid_syntax_returns_empty_diagnostics()
    {
        using var client = TestHost.CreateClient(_factory);
        var res = await client.PostAsJsonAsync("/api/code-diagnostics/powershell", new { code = "Get-Date" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<DiagnosticsResponse>();
        Assert.NotNull(body);
        Assert.Empty(body!.Diagnostics);
    }

    [Fact]
    public async Task PowerShell_missing_brace_returns_error()
    {
        using var client = TestHost.CreateClient(_factory);
        var res = await client.PostAsJsonAsync("/api/code-diagnostics/powershell", new { code = "if ($true) {" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<DiagnosticsResponse>();
        Assert.NotNull(body);
        Assert.NotEmpty(body!.Diagnostics);
    }

    [Fact]
    public async Task Missing_code_returns_bad_request()
    {
        using var client = TestHost.CreateClient(_factory);
        var res = await client.PostAsJsonAsync("/api/code-diagnostics/powershell", new { });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    private sealed class DiagnosticsResponse
    {
        public List<DiagnosticItem> Diagnostics { get; set; } = [];

        public sealed class DiagnosticItem
        {
            public string Message { get; set; } = "";
            public string Severity { get; set; } = "";
        }
    }
}
