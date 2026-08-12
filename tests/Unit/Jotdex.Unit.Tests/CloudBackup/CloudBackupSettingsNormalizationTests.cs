using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Unit.Tests.CloudBackup;

public class CloudBackupSettingsNormalizationTests : IDisposable
{
    private readonly string _root;

    public CloudBackupSettingsNormalizationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "jotdex-cb-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(-5, 24)]
    [InlineData(1, 1)]
    [InlineData(48, 48)]
    [InlineData(200, 168)]
    public void IntervalHours_is_clamped(int input, int expected)
    {
        var svc = new CloudBackupSettingsService(new TestDataRoot(_root), NullLogger<CloudBackupSettingsService>.Instance);
        var saved = svc.Save(new CloudBackupSettings
        {
            IntervalHours = input,
            VersionsToKeep = 3,
            BackupSetId = "fixed-id"
        });
        Assert.Equal(expected, saved.IntervalHours);
        Assert.Equal(expected, svc.Get().IntervalHours);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(10, 10)]
    [InlineData(99, 30)]
    public void VersionsToKeep_is_clamped(int input, int expected)
    {
        var svc = new CloudBackupSettingsService(new TestDataRoot(_root), NullLogger<CloudBackupSettingsService>.Instance);
        var saved = svc.Save(new CloudBackupSettings
        {
            IntervalHours = 24,
            VersionsToKeep = input,
            BackupSetId = "fixed-id"
        });
        Assert.Equal(expected, saved.VersionsToKeep);
    }

    [Fact]
    public void Defaults_ensure_all_providers_present()
    {
        var svc = new CloudBackupSettingsService(new TestDataRoot(_root), NullLogger<CloudBackupSettingsService>.Instance);
        var s = svc.Get();
        Assert.Equal(3, s.Providers.Count);
        Assert.Contains(s.Providers, p => p.Provider == CloudProviderKind.OneDrive);
        Assert.Contains(s.Providers, p => p.Provider == CloudProviderKind.GoogleDrive);
        Assert.Contains(s.Providers, p => p.Provider == CloudProviderKind.Dropbox);
        Assert.InRange(s.IntervalHours, 1, 168);
        Assert.InRange(s.VersionsToKeep, 2, 30);
    }
}
