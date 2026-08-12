using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup;
using Jotdex.Infrastructure.CloudBackup.Providers;

namespace Jotdex.Unit.Tests.CloudBackup;

public sealed class CloudOAuthClientConfigTests
{
    [Fact]
    public void Prefers_settings_client_id_over_environment()
    {
        var prev = Environment.GetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv);
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, "env-key");
        try
        {
            var settings = new StubSettings(new CloudBackupSettings
            {
                Providers =
                [
                    new CloudProviderSettings
                    {
                        Provider = CloudProviderKind.Dropbox,
                        OAuthClientId = "settings-key"
                    },
                    new CloudProviderSettings { Provider = CloudProviderKind.GoogleDrive },
                    new CloudProviderSettings { Provider = CloudProviderKind.OneDrive }
                ]
            });
            var cfg = new CloudOAuthClientConfig(settings);
            Assert.Equal("settings-key", cfg.GetClientId(CloudProviderKind.Dropbox));
            Assert.True(cfg.IsConfigured(CloudProviderKind.Dropbox));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, prev);
        }
    }

    [Fact]
    public void Falls_back_to_environment_when_settings_empty()
    {
        var prev = Environment.GetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv);
        Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, "env-only");
        try
        {
            var settings = new StubSettings(new CloudBackupSettings
            {
                Providers =
                [
                    new CloudProviderSettings { Provider = CloudProviderKind.Dropbox },
                    new CloudProviderSettings { Provider = CloudProviderKind.GoogleDrive },
                    new CloudProviderSettings { Provider = CloudProviderKind.OneDrive }
                ]
            });
            var cfg = new CloudOAuthClientConfig(settings);
            Assert.Equal("env-only", cfg.GetClientId(CloudProviderKind.Dropbox));
        }
        finally
        {
            Environment.SetEnvironmentVariable(DropboxCloudBackupProvider.AppKeyEnv, prev);
        }
    }

    [Fact]
    public void Json_roundtrip_uses_oauthClientId_property_name()
    {
        var json = """{"provider":"oneDrive","oauthClientId":"azure-app-id-123"}""";
        var opts = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase) }
        };
        var parsed = System.Text.Json.JsonSerializer.Deserialize<CloudProviderSettings>(json, opts);
        Assert.NotNull(parsed);
        Assert.Equal(CloudProviderKind.OneDrive, parsed!.Provider);
        Assert.Equal("azure-app-id-123", parsed.OAuthClientId);

        var written = System.Text.Json.JsonSerializer.Serialize(parsed, opts);
        Assert.Contains("oauthClientId", written, StringComparison.Ordinal);
        Assert.DoesNotContain("oAuthClientId", written, StringComparison.Ordinal);
    }

    private sealed class StubSettings(CloudBackupSettings current) : ICloudBackupSettingsService
    {
        public CloudBackupSettings Get() => current;
        public CloudBackupSettings Save(CloudBackupSettings incoming) => incoming;
    }
}
