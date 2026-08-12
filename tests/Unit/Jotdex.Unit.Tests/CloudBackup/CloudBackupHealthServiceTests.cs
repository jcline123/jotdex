using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupHealthServiceTests
{
    [Fact]
    public void MoveKit_failure_is_critical_error()
    {
        var settings = EnabledDropbox(includeZip: true);
        var state = new CloudBackupRuntimeState
        {
            Providers =
            [
                new CloudProviderBackupStatus
                {
                    Provider = CloudProviderKind.Dropbox,
                    ConnectionState = CloudConnectionState.Connected,
                    MoveKit =
                    {
                        LastVerifiedUtc = DateTimeOffset.UtcNow.AddHours(-1),
                        LastAttemptUtc = DateTimeOffset.UtcNow,
                        LastFailureCode = CloudBackupFailureCode.UploadFailed,
                        LastFailureMessage = "Move kit upload failed"
                    }
                }
            ]
        };

        var health = new CloudBackupHealthService().Calculate(settings, state, running: false);
        Assert.Equal(CloudBackupHealthLevel.Error, health.AggregateHealth);
        Assert.Equal(CloudBackupHealthLevel.Error, health.Providers[0].Health);
    }

    [Fact]
    public void VaultZip_failure_only_is_warning_when_MoveKit_ok()
    {
        var settings = EnabledDropbox(includeZip: true);
        var verified = DateTimeOffset.UtcNow.AddMinutes(-30);
        var state = new CloudBackupRuntimeState
        {
            Providers =
            [
                new CloudProviderBackupStatus
                {
                    Provider = CloudProviderKind.Dropbox,
                    ConnectionState = CloudConnectionState.Connected,
                    MoveKit =
                    {
                        LastVerifiedUtc = verified,
                        LastAttemptUtc = verified,
                        LastFailureCode = CloudBackupFailureCode.None
                    },
                    VaultZip =
                    {
                        LastAttemptUtc = DateTimeOffset.UtcNow,
                        LastFailureCode = CloudBackupFailureCode.UploadFailed,
                        LastFailureMessage = "ZIP failed"
                    }
                }
            ]
        };

        var health = new CloudBackupHealthService().Calculate(settings, state, running: false);
        Assert.Equal(CloudBackupHealthLevel.Warning, health.AggregateHealth);
        Assert.Equal(CloudBackupHealthLevel.Warning, health.Providers[0].Health);
    }

    private static CloudBackupSettings EnabledDropbox(bool includeZip) =>
        new()
        {
            IntervalHours = 24,
            VersionsToKeep = 3,
            IncludePlainVaultZip = includeZip,
            Providers = [new CloudProviderSettings { Provider = CloudProviderKind.Dropbox, Enabled = true }]
        };
}
