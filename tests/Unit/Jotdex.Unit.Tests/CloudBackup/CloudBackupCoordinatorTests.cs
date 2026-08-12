using System.Diagnostics;
using System.Text;
using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;
using Jotdex.Infrastructure.Maintenance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupCoordinatorTests : IDisposable
{
    private readonly string _root;
    private readonly string _fakeStorage;

    public CloudBackupCoordinatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-coord-" + Guid.NewGuid().ToString("N"));
        _fakeStorage = Path.Combine(_root, "fake-cloud");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_fakeStorage);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public async Task Retention_keeps_VersionsToKeep_complete_generations()
    {
        var fake = new FakeCloudBackupProvider(CloudProviderKind.Dropbox, _fakeStorage);
        SeedCompleteGeneration(fake, "20260101T120000Z", "aaaaaaa1");
        SeedCompleteGeneration(fake, "20260201T120000Z", "aaaaaaa2");
        SeedCompleteGeneration(fake, "20260301T120000Z", "aaaaaaa3");
        Assert.Equal(9, fake.RemoteFileNames.Count);

        var harness = CreateHarness(fake, versionsToKeep: 2, includeVaultZip: true);
        var op = await harness.Coordinator.StartRunAsync(CloudBackupRunTrigger.Manual, CloudProviderKind.Dropbox, CancellationToken.None);
        await WaitUntilDone(op);

        Assert.Null(op.Error);
        // VersionsToKeep=2: keep newest complete gens (new run + aaaaaaa3); prune older completes.
        Assert.DoesNotContain(fake.RemoteFileNames, n => n.Contains("aaaaaaa1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fake.RemoteFileNames, n => n.Contains("aaaaaaa2", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(fake.RemoteFileNames, n => n.Contains("aaaaaaa3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, fake.RemoteFileNames.Count(CloudBackupRemoteNaming.IsGenerationManifestName));
    }

    [Fact]
    public async Task Partial_generation_does_not_prune_older_completes()
    {
        var fake = new FakeCloudBackupProvider(CloudProviderKind.Dropbox, _fakeStorage);
        SeedCompleteGeneration(fake, "20260101T120000Z", "bbbbbbb1");
        SeedCompleteGeneration(fake, "20260201T120000Z", "bbbbbbb2");
        fake.FailArtifactTypes.Add(CloudArtifactTypes.VaultZip);

        var harness = CreateHarness(fake, versionsToKeep: 2, includeVaultZip: true);
        var before = fake.RemoteFileNames.ToList();
        var op = await harness.Coordinator.StartRunAsync(CloudBackupRunTrigger.Manual, CloudProviderKind.Dropbox, CancellationToken.None);
        await WaitUntilDone(op);

        foreach (var name in before)
            Assert.Contains(name, fake.RemoteFileNames);

        var health = harness.Coordinator.GetHealth();
        Assert.Equal(CloudBackupHealthLevel.Warning, health.AggregateHealth);
        var dropbox = Assert.Single(health.Providers, p => p.Provider == CloudProviderKind.Dropbox);
        Assert.Equal(CloudBackupHealthLevel.Warning, dropbox.Health);
    }

    [Fact]
    public async Task MoveKit_failure_is_critical_health()
    {
        var fake = new FakeCloudBackupProvider(CloudProviderKind.Dropbox, _fakeStorage);
        fake.FailArtifactTypes.Add(CloudArtifactTypes.MoveKit);

        var harness = CreateHarness(fake, versionsToKeep: 3, includeVaultZip: true);
        var op = await harness.Coordinator.StartRunAsync(CloudBackupRunTrigger.Manual, CloudProviderKind.Dropbox, CancellationToken.None);
        await WaitUntilDone(op);

        var health = harness.Coordinator.GetHealth();
        Assert.Equal(CloudBackupHealthLevel.Error, health.AggregateHealth);
        var dropbox = Assert.Single(health.Providers, p => p.Provider == CloudProviderKind.Dropbox);
        Assert.Equal(CloudBackupHealthLevel.Error, dropbox.Health);

        var state = harness.State.Get().Providers.First(p => p.Provider == CloudProviderKind.Dropbox);
        Assert.Equal(CloudBackupHealthLevel.Error, state.Health);
        Assert.NotEqual(CloudBackupFailureCode.None, state.MoveKit.LastFailureCode);
    }

    [Fact]
    public async Task VaultZip_failure_only_is_amber_partial()
    {
        var fake = new FakeCloudBackupProvider(CloudProviderKind.Dropbox, _fakeStorage);
        fake.FailArtifactTypes.Add(CloudArtifactTypes.VaultZip);

        var harness = CreateHarness(fake, versionsToKeep: 3, includeVaultZip: true);
        var op = await harness.Coordinator.StartRunAsync(CloudBackupRunTrigger.Manual, CloudProviderKind.Dropbox, CancellationToken.None);
        await WaitUntilDone(op);

        var health = harness.Coordinator.GetHealth();
        Assert.Equal(CloudBackupHealthLevel.Warning, health.AggregateHealth);
        var dropbox = Assert.Single(health.Providers, p => p.Provider == CloudProviderKind.Dropbox);
        Assert.Equal(CloudBackupHealthLevel.Warning, dropbox.Health);

        var state = harness.State.Get().Providers.First(p => p.Provider == CloudProviderKind.Dropbox);
        Assert.Equal(CloudBackupHealthLevel.Warning, state.Health);
        Assert.NotNull(state.MoveKit.LastVerifiedUtc);
        Assert.NotEqual(CloudBackupFailureCode.None, state.VaultZip.LastFailureCode);
    }

    private Harness CreateHarness(FakeCloudBackupProvider fake, int versionsToKeep, bool includeVaultZip)
    {
        var data = new TestDataRoot(_root);
        var settings = new CloudBackupSettingsService(data, NullLogger<CloudBackupSettingsService>.Instance);
        settings.Save(new CloudBackupSettings
        {
            BackupSetId = "test-backup-set",
            BackupSetName = "Test",
            IntervalHours = 24,
            VersionsToKeep = versionsToKeep,
            IncludePlainVaultZip = includeVaultZip,
            Providers =
            [
                new CloudProviderSettings { Provider = CloudProviderKind.Dropbox, Enabled = true },
                new CloudProviderSettings { Provider = CloudProviderKind.OneDrive, Enabled = false },
                new CloudProviderSettings { Provider = CloudProviderKind.GoogleDrive, Enabled = false }
            ]
        });

        var state = new CloudBackupStateStore(data, NullLogger<CloudBackupStateStore>.Instance);
        var health = new CloudBackupHealthService();
        var hashes = new CloudBackupHashService();
        var artifacts = new StubArtifactService(_root, hashes, includeVaultZip);
        var snapshot = new StubSnapshotService();
        var crypto = new AlwaysReadyCrypto();

        var coordinator = new CloudBackupCoordinator(
            settings,
            state,
            artifacts,
            health,
            snapshot,
            crypto,
            new ICloudBackupProvider[] { fake },
            hashes,
            NullLogger<CloudBackupCoordinator>.Instance);

        return new Harness(coordinator, state);
    }

    private static void SeedCompleteGeneration(FakeCloudBackupProvider fake, string stamp, string shortId)
    {
        fake.SeedRemoteFile($"jotdex-move-{stamp}-{shortId}.jotdexkit");
        fake.SeedRemoteFile($"jotdex-vault-{stamp}-{shortId}.zip");
        fake.SeedRemoteFile($"jotdex-backup-{stamp}-{shortId}.manifest.json");
    }

    private static async Task WaitUntilDone(CloudBackupOperation op, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (op.Running && sw.ElapsedMilliseconds < timeoutMs)
            await Task.Delay(25);
        Assert.False(op.Running, $"Operation still running after {timeoutMs}ms: phase={op.Phase} error={op.Error}");
    }

    private sealed record Harness(CloudBackupCoordinator Coordinator, ICloudBackupStateStore State);

    private sealed class AlwaysReadyCrypto : IMoveKitCryptoService
    {
        public bool IsPasswordProtectionEnabled => true;
        public bool HasEncryptionKey => true;
        public void OnPasswordSet(string password) { }
        public void OnPasswordCleared() { }
        public void EnsureInitialized(string password) { }
        public string EncryptZipFile(string plainZipPath) => throw new NotSupportedException();
        public void DecryptToZip(string encryptedPath, string password, string outputZipPath) =>
            throw new NotSupportedException();
    }

    private sealed class StubSnapshotService : ICloudBackupSnapshotService
    {
        public Task<CloudBackupSnapshotResult> CreateAsync(string runId, CancellationToken cancellationToken) =>
            Task.FromResult(new CloudBackupSnapshotResult { Success = true, RunId = runId });

        public void CleanOrphanedStaging(TimeSpan olderThan) { }
        public void DeleteRunStaging(string runId) { }
    }

    private sealed class StubArtifactService : ICloudBackupArtifactService
    {
        private readonly string _root;
        private readonly CloudBackupHashService _hashes;
        private readonly bool _includeZip;

        public StubArtifactService(string root, CloudBackupHashService hashes, bool includeZip)
        {
            _root = root;
            _hashes = hashes;
            _includeZip = includeZip;
        }

        public Task<CloudBackupArtifactResult> CreateAsync(
            string runId,
            bool includePlainVaultZip,
            string? passwordForInit,
            CancellationToken cancellationToken)
        {
            var created = DateTimeOffset.UtcNow;
            var dir = Path.Combine(_root, "artifacts", runId);
            Directory.CreateDirectory(dir);

            var moveName = CloudBackupRemoteNaming.MoveKitFileName(created, runId);
            var movePath = Path.Combine(dir, moveName);
            File.WriteAllBytes(movePath, Encoding.UTF8.GetBytes("fake-jotdexkit-" + runId));

            var artifacts = new List<CloudBackupArtifactDescriptor>
            {
                new()
                {
                    Type = CloudArtifactTypes.MoveKit,
                    FileName = moveName,
                    LocalPath = movePath,
                    Encrypted = true,
                    KitFormat = "JDXK2",
                    SizeBytes = new FileInfo(movePath).Length,
                    Sha256 = _hashes.Sha256FileHex(movePath)
                }
            };

            if (includePlainVaultZip && _includeZip)
            {
                var zipName = CloudBackupRemoteNaming.VaultZipFileName(created, runId);
                var zipPath = Path.Combine(dir, zipName);
                File.WriteAllBytes(zipPath, Encoding.UTF8.GetBytes("fake-vault-zip-" + runId));
                artifacts.Add(new CloudBackupArtifactDescriptor
                {
                    Type = CloudArtifactTypes.VaultZip,
                    FileName = zipName,
                    LocalPath = zipPath,
                    Encrypted = false,
                    SizeBytes = new FileInfo(zipPath).Length,
                    Sha256 = _hashes.Sha256FileHex(zipPath)
                });
            }

            var manifestName = CloudBackupRemoteNaming.GenerationManifestFileName(created, runId);
            var manifestPath = Path.Combine(dir, manifestName);
            File.WriteAllText(manifestPath, """{"schemaVersion":1,"kind":"test"}""");

            var generation = new CloudBackupGeneration
            {
                BackupSetId = "test-backup-set",
                RunId = runId,
                CreatedUtc = created,
                JotdexVersion = "test",
                RequiredArtifacts = artifacts.Select(a => a.Type).ToList(),
                Artifacts = artifacts,
                ManifestPath = manifestPath,
                ManifestFileName = manifestName,
                StagingRoot = dir
            };

            return Task.FromResult(new CloudBackupArtifactResult { Success = true, Generation = generation });
        }
    }
}
