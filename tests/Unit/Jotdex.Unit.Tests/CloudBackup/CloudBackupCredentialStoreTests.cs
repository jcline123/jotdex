using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;
using Jotdex.Infrastructure.Secrets;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupCredentialStoreTests : IDisposable
{
    private readonly string _root;

    public CloudBackupCredentialStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-creds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Fact]
    public void Save_and_load_round_trips_cloud_credentials()
    {
        var data = new TestDataRoot(_root);
        var store = new DpapiCloudCredentialStore(data, NullLogger<DpapiCloudCredentialStore>.Instance);
        store.Set(CloudProviderKind.Dropbox, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.Dropbox,
            AccountId = "dbx-1",
            AccountDisplayName = "Josh",
            AccountEmail = "josh@example.com",
            ProtectedPayload = "refresh-token-secret-value"
        });

        Assert.True(store.Has(CloudProviderKind.Dropbox));
        Assert.True(store.TryGet(CloudProviderKind.Dropbox, out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal("dbx-1", loaded!.AccountId);
        Assert.Equal("josh@example.com", loaded.AccountEmail);
        Assert.Equal("refresh-token-secret-value", loaded.ProtectedPayload);
        Assert.True(File.Exists(Path.Combine(_root, "secrets", "cloud-backup.json")));
    }

    [Fact]
    public void ExportPortable_does_not_contain_cloud_backup_tokens()
    {
        var data = new TestDataRoot(_root);
        var secrets = new DpapiSecretStore(data, NullLogger<DpapiSecretStore>.Instance);
        secrets.Set("moveKit.aesKey", Convert.ToBase64String(new byte[32]));
        secrets.Set("notify.webhook", "https://example.invalid/hook");

        var cloud = new DpapiCloudCredentialStore(data, NullLogger<DpapiCloudCredentialStore>.Instance);
        const string token = "cloud-oauth-refresh-TOKEN-should-never-export";
        cloud.Set(CloudProviderKind.OneDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.OneDrive,
            AccountId = "od-1",
            ProtectedPayload = token
        });
        cloud.Set(CloudProviderKind.GoogleDrive, new CloudCredentialEnvelope
        {
            Provider = CloudProviderKind.GoogleDrive,
            AccountId = "gd-1",
            ProtectedPayload = token + "-google"
        });

        var portable = secrets.ExportPortable();
        Assert.Contains("moveKit.aesKey", portable.Keys);
        Assert.Contains("notify.webhook", portable.Keys);
        Assert.DoesNotContain(portable.Keys, k => k.Contains("cloud", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(portable.Values, v =>
            v.Contains(token, StringComparison.Ordinal) ||
            v.Contains("oauth", StringComparison.OrdinalIgnoreCase));

        // Cloud store still has credentials independently of ISecretStore.
        Assert.True(cloud.TryGet(CloudProviderKind.OneDrive, out var od) && od!.ProtectedPayload == token);
        Assert.True(cloud.Has(CloudProviderKind.GoogleDrive));
    }
}
