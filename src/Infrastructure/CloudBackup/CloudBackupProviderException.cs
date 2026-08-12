using Jotdex.Core.CloudBackup;

namespace Jotdex.Infrastructure.CloudBackup;

public sealed class CloudBackupProviderException : Exception
{
    public CloudBackupFailureCode Code { get; }

    public CloudBackupProviderException(CloudBackupFailureCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public CloudBackupProviderException(CloudBackupFailureCode code, string message, Exception inner)
        : base(message, inner)
    {
        Code = code;
    }

    public static CloudBackupProviderException ConfigurationMissing(CloudProviderKind kind) =>
        new(CloudBackupFailureCode.ProviderConfigurationMissing,
            $"{kind} is not configured in this build (missing client/app id).");
}
