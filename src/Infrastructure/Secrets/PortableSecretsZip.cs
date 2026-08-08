using System.Text.Json;
using Jotdex.Core.Secrets;

namespace Jotdex.Infrastructure.Secrets;

public static class PortableSecretsZip
{
    public static void AddToZip(System.IO.Compression.ZipArchive zip, ISecretStore secrets, string entryName = "appdata/secrets/secrets-portable.json")
    {
        var portable = secrets.ExportPortable();
        if (portable.Count == 0) return;

        var payload = new
        {
            kind = "jotdex-secrets-portable",
            createdUtc = DateTimeOffset.UtcNow,
            secrets = portable
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        var entry = zip.CreateEntry(entryName);
        using var w = new StreamWriter(entry.Open());
        w.Write(json);
    }
}
