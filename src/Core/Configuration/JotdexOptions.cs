namespace Jotdex.Core.Configuration;

/// <summary>
/// Application paths. Vault must be on local disk (not iCloud live sync).
/// </summary>
public sealed class JotdexOptions
{
    public const string SectionName = "Jotdex";

    /// <summary>Absolute path to the Markdown vault root. Empty until configured.</summary>
    public string VaultPath { get; set; } = "";

    /// <summary>
    /// Application data root (config, indexes, history, trash, logs, exports).
    /// Empty = use %LOCALAPPDATA%\Jotdex or portable .\data beside the executable.
    /// </summary>
    public string DataRoot { get; set; } = "";

    /// <summary>When true, prefer .\data beside the content root / executable.</summary>
    public bool PortableMode { get; set; } = true;

    /// <summary>Max upload size for note attachments (bytes). Default 100 MB.</summary>
    public long MaxAttachmentBytes { get; set; } = 100L * 1024 * 1024;

    public AuthOptions Auth { get; set; } = new();
}

public sealed class AuthOptions
{
    /// <summary>Cookie idle timeout in minutes.</summary>
    public int IdleTimeoutMinutes { get; set; } = 60;

    /// <summary>When true (default), Development hosts skip auth enforcement so tests/local sample vault work.</summary>
    public bool BypassInDevelopment { get; set; } = true;
}

public interface IDataRootResolver
{
    string ResolveDataRoot();
    string? ResolveVaultPathOrNull();
    bool IsVaultConfigured { get; }
}

public interface IAppVersion
{
    string Version { get; }
}
