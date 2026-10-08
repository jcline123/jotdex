using System.Text.Json;
using Jotdex.Core.Integrations;
using Jotdex.Core.Vault;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Integrations;

public sealed class VaultIdentityService : IVaultIdentity
{
    private readonly IVaultPathGuard _paths;
    private readonly ILogger<VaultIdentityService> _logger;

    public VaultIdentityService(IVaultPathGuard paths, ILogger<VaultIdentityService> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public string? GetVaultId()
    {
        if (!_paths.IsConfigured) return null;
        try
        {
            var root = _paths.VaultRoot;
            if (string.IsNullOrWhiteSpace(root)) return null;
            var marker = Path.Combine(root, ".notes-vault.json");
            if (!File.Exists(marker)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(marker));
            if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                var s = id.GetString();
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read vault id");
        }
        return null;
    }
}
