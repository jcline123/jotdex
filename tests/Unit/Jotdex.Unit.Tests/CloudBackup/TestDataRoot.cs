using Jotdex.Core.Configuration;

namespace Jotdex.Unit.Tests.CloudBackup;

internal sealed class TestDataRoot(string root, string? vault = null) : IDataRootResolver
{
    public string ResolveDataRoot() => root;
    public string? ResolveVaultPathOrNull() => vault;
    public bool IsVaultConfigured => !string.IsNullOrWhiteSpace(vault);
}

internal sealed class FixedAppVersion(string version = "test-1.0.0") : IAppVersion
{
    public string Version { get; } = version;
}
