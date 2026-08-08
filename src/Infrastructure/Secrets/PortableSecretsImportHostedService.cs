using Jotdex.Core.Secrets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Secrets;

/// <summary>On startup, import move-kit portable secrets and delete the plaintext file.</summary>
public sealed class PortableSecretsImportHostedService : IHostedService
{
    private readonly ISecretStore _secrets;
    private readonly ILogger<PortableSecretsImportHostedService> _logger;

    public PortableSecretsImportHostedService(ISecretStore secrets, ILogger<PortableSecretsImportHostedService> logger)
    {
        _secrets = secrets;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var n = _secrets.ImportPortableFileIfPresent();
            if (n > 0)
                _logger.LogInformation("Imported {Count} secrets from portable move-kit file", n);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Portable secrets import failed");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
