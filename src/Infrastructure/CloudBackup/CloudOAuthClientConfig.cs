using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup.Providers;

namespace Jotdex.Infrastructure.CloudBackup;

/// <summary>
/// Resolves OAuth app client id / secret / redirect URI from Settings (GUI) first, then process env.
/// </summary>
public interface ICloudOAuthClientConfig
{
    string? GetClientId(CloudProviderKind provider);
    string? GetClientSecret(CloudProviderKind provider);
    string GetRedirectUri(CloudProviderKind provider);
    bool IsConfigured(CloudProviderKind provider);
}

public sealed class CloudOAuthClientConfig : ICloudOAuthClientConfig
{
    private readonly ICloudBackupSettingsService _settings;

    public CloudOAuthClientConfig(ICloudBackupSettingsService settings)
    {
        _settings = settings;
    }

    public bool IsConfigured(CloudProviderKind provider) =>
        !string.IsNullOrWhiteSpace(GetClientId(provider));

    public string? GetClientId(CloudProviderKind provider)
    {
        var fromSettings = Provider(provider)?.OAuthClientId;
        if (!string.IsNullOrWhiteSpace(fromSettings))
            return fromSettings.Trim();

        return provider switch
        {
            CloudProviderKind.Dropbox => Env(DropboxCloudBackupProvider.AppKeyEnv),
            CloudProviderKind.GoogleDrive =>
                Env(GoogleDriveCloudBackupProvider.ClientIdEnv)
                ?? Env(GoogleDriveCloudBackupProvider.ClientConfigEnv),
            CloudProviderKind.OneDrive => Env(OneDriveCloudBackupProvider.ClientIdEnv),
            _ => null
        };
    }

    public string? GetClientSecret(CloudProviderKind provider)
    {
        var fromSettings = Provider(provider)?.OAuthClientSecret;
        if (!string.IsNullOrWhiteSpace(fromSettings))
            return fromSettings.Trim();

        return provider switch
        {
            CloudProviderKind.Dropbox => Env(DropboxCloudBackupProvider.AppSecretEnv),
            CloudProviderKind.GoogleDrive => Env(GoogleDriveCloudBackupProvider.ClientSecretEnv),
            _ => null
        };
    }

    public string GetRedirectUri(CloudProviderKind provider)
    {
        var fromSettings = Provider(provider)?.OAuthRedirectUri;
        if (!string.IsNullOrWhiteSpace(fromSettings))
            return fromSettings.Trim();

        var fromEnv = provider switch
        {
            CloudProviderKind.Dropbox => Env(DropboxCloudBackupProvider.RedirectUriEnv),
            CloudProviderKind.GoogleDrive => Env(GoogleDriveCloudBackupProvider.RedirectUriEnv),
            CloudProviderKind.OneDrive => Env(OneDriveCloudBackupProvider.RedirectUriEnv),
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        return provider switch
        {
            CloudProviderKind.Dropbox => "http://127.0.0.1:5180/oauth/dropbox",
            CloudProviderKind.GoogleDrive => "http://127.0.0.1:5180/oauth/google",
            CloudProviderKind.OneDrive => "http://127.0.0.1:5180/oauth/onedrive",
            _ => "http://127.0.0.1:5180/oauth"
        };
    }

    private CloudProviderSettings? Provider(CloudProviderKind kind) =>
        _settings.Get().Providers.FirstOrDefault(p => p.Provider == kind);

    private static string? Env(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }
}
