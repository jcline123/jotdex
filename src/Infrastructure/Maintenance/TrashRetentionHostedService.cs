using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Maintenance;

/// <summary>
/// Removes trash entries that have been deleted for 30 days. Manual empty-all stays on the admin endpoint.
/// </summary>
public sealed class TrashRetentionHostedService : BackgroundService
{
    public const int RetentionDays = 30;

    private readonly IMaintenanceService _maintenance;
    private readonly ILogger<TrashRetentionHostedService> _logger;

    public TrashRetentionHostedService(IMaintenanceService maintenance, ILogger<TrashRetentionHostedService> logger)
    {
        _maintenance = maintenance;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = _maintenance.EmptyTrash(olderThanDaysOnly: true, olderThanDays: RetentionDays);
                if (result.Success && result.DeletedFiles > 0)
                    _logger.LogInformation("Trash retention removed {Files} files older than {Days} days", result.DeletedFiles, RetentionDays);
                else if (!result.Success)
                    _logger.LogWarning("Trash retention failed: {Error}", result.Error);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Trash retention sweep failed");
            }

            try { await Task.Delay(TimeSpan.FromHours(12), stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }
}
