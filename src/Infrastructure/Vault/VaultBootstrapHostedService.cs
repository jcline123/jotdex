using Jotdex.Core.Vault;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Vault;

/// <summary>Initial vault scan after DI is fully constructed (avoids ctor cycles with search index).</summary>
public sealed class VaultBootstrapHostedService(IVaultService vault, IVaultPathGuard paths, ILogger<VaultBootstrapHostedService> logger)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!paths.IsConfigured)
        {
            logger.LogInformation("No vault configured at startup");
            return Task.CompletedTask;
        }

        vault.Rescan();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
