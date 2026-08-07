using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Jotdex.Tests.Smoke;

public class VaultPathGuardTests
{
    [Fact]
    public void Rejects_path_escape_outside_vault()
    {
        var vault = Path.Combine(Path.GetTempPath(), "jotdex-vault-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(vault);
        try
        {
            var guard = CreateGuard(vault);
            Assert.Throws<UnauthorizedAccessException>(() =>
                guard.EnsureInsideVault(Path.Combine(vault, "..", "..", "Windows")));
        }
        finally
        {
            Directory.Delete(vault, true);
        }
    }

    [Fact]
    public void Allows_path_inside_vault()
    {
        var vault = Path.Combine(Path.GetTempPath(), "jotdex-vault-" + Guid.NewGuid().ToString("N"));
        var note = Path.Combine(vault, "A.md");
        Directory.CreateDirectory(vault);
        File.WriteAllText(note, "# Hi");
        try
        {
            var guard = CreateGuard(vault);
            var full = guard.EnsureInsideVault("A.md");
            Assert.True(File.Exists(full));
            Assert.Equal("A.md", guard.ToRelativePath(full));
        }
        finally
        {
            Directory.Delete(vault, true);
        }
    }

    private static VaultPathGuard CreateGuard(string vaultPath)
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "jotdex-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        var options = Options.Create(new JotdexOptions { VaultPath = vaultPath, PortableMode = true });
        return new VaultPathGuard(
            options,
            new TestHostEnv(Path.GetTempPath()),
            new FixedDataRoot(dataDir),
            NullLogger<VaultPathGuard>.Instance);
    }

    private sealed class FixedDataRoot(string root) : IDataRootResolver
    {
        public string ResolveDataRoot() => root;
        public string? ResolveVaultPathOrNull() => null;
        public bool IsVaultConfigured => true;
    }

    private sealed class TestHostEnv(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRoot);
    }
}
