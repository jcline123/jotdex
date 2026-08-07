using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Jotdex.Tests.Smoke;

internal static class TestHost
{
    /// <summary>Development host pinned to SampleVault with an isolated data root (avoids live vault.json).</summary>
    public static HttpClient CreateClient(WebApplicationFactory<Program> factory, string? dataRoot = null)
    {
        dataRoot ??= Path.Combine(Path.GetTempPath(), "jotdex-test-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);

        // Resolve SampleVault relative to the test assembly → repo/tools/SampleVault
        var repo = FindRepoRoot();
        var sampleVault = Path.Combine(repo, "tools", "SampleVault");

        return factory.WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Development");
            b.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jotdex:DataRoot"] = dataRoot,
                    ["Jotdex:VaultPath"] = sampleVault,
                    ["Jotdex:PortableMode"] = "true",
                    ["Jotdex:Auth:BypassInDevelopment"] = "true"
                });
            });
        }).CreateClient();
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
        throw new InvalidOperationException("Could not find Jotdex.sln from " + AppContext.BaseDirectory);
    }
}
