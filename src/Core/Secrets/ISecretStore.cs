namespace Jotdex.Core.Secrets;

/// <summary>
/// DPAPI-wrapped app secrets (SMTP/Telegram/TOTP, etc.) at rest; portable JSON for move-kit transfer.
/// Cloud provider OAuth is <em>not</em> in this store — see <c>ICloudCredentialStore</c> /
/// <c>data/secrets/cloud-backup.json</c>, which must never appear in ExportPortable / Move Kits.
/// </summary>
public interface ISecretStore
{
    bool TryGet(string key, out string? value);
    void Set(string key, string value);
    bool Remove(string key);
    bool Has(string key);
    IReadOnlyDictionary<string, string> ExportPortable();
    void ImportPortable(IReadOnlyDictionary<string, string> values, bool overwriteExisting = true);
    /// <summary>If <c>data/secrets-portable.json</c> (or legacy path) exists, import then delete.</summary>
    int ImportPortableFileIfPresent();
}
