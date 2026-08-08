namespace Jotdex.Core.Secrets;

/// <summary>DPAPI-wrapped secrets at rest; portable JSON for move-kit transfer.</summary>
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
