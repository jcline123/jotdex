using System.IO.Compression;
using Jotdex.Infrastructure.CloudBackup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupVaultSnapshotZipTests : IDisposable
{
    private readonly string _root;

    public CloudBackupVaultSnapshotZipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task CreateAsync_includes_vault_manifest_and_readme()
    {
        var data = new TestDataRoot(_root);
        var staging = CloudBackupPaths.StagingRoot(_root);
        var runId = Guid.NewGuid().ToString("N");
        var vaultSnap = Path.Combine(staging, runId, "snapshot", "vault");
        Directory.CreateDirectory(vaultSnap);
        File.WriteAllText(Path.Combine(vaultSnap, "Note.md"), "# Hello");
        var assets = Path.Combine(vaultSnap, "Note.assets");
        Directory.CreateDirectory(assets);
        File.WriteAllText(Path.Combine(assets, "pic.txt"), "img");

        var outDir = Path.Combine(staging, runId, "artifacts");
        var svc = new VaultSnapshotZipService(
            data,
            new CloudBackupHashService(),
            new FixedAppVersion("9.9.9"),
            NullLogger<VaultSnapshotZipService>.Instance);

        var result = await svc.CreateAsync(
            vaultSnap,
            Path.Combine(staging, runId),
            outDir,
            runId,
            "backup-set-1",
            "jotdex-vault-test.zip",
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(result.ZipPath));
        using var zip = ZipFile.OpenRead(result.ZipPath!);
        var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("VAULT-MANIFEST.json", names);
        Assert.Contains("README-VAULT-BACKUP.txt", names);
        Assert.Contains(names, n => n.StartsWith("vault/", StringComparison.OrdinalIgnoreCase) && n.EndsWith("Note.md", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.NoteCount >= 1);
    }

    [Fact]
    public async Task CreateAsync_rejects_path_outside_staging()
    {
        var data = new TestDataRoot(_root);
        var outside = Path.Combine(_root, "not-staging", "vault");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "A.md"), "x");

        var svc = new VaultSnapshotZipService(
            data,
            new CloudBackupHashService(),
            new FixedAppVersion(),
            NullLogger<VaultSnapshotZipService>.Instance);

        var result = await svc.CreateAsync(
            outside,
            outside,
            Path.Combine(_root, "out"),
            "run1",
            "set1",
            "bad.zip",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("staging", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
