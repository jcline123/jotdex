using Jotdex.Core.CloudBackup;
using Jotdex.Infrastructure.CloudBackup.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Jotdex.Infrastructure.CloudBackup;

public static class CloudBackupProviderFactory
{
    public static void AddCloudBackupProviders(IServiceCollection services)
    {
        services.AddHttpClient("cloud-dropbox");
        services.AddHttpClient("cloud-google");
        services.AddHttpClient("cloud-onedrive");
        services.AddSingleton<ICloudOAuthClientConfig, CloudOAuthClientConfig>();
        services.AddSingleton<OneDriveCloudBackupProvider>();
        services.AddSingleton<GoogleDriveCloudBackupProvider>();
        services.AddSingleton<DropboxCloudBackupProvider>();
        services.AddSingleton<IEnumerable<ICloudBackupProvider>>(sp =>
        [
            sp.GetRequiredService<OneDriveCloudBackupProvider>(),
            sp.GetRequiredService<GoogleDriveCloudBackupProvider>(),
            sp.GetRequiredService<DropboxCloudBackupProvider>()
        ]);
    }
}
