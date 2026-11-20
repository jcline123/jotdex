using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Hosting;

/// <summary>
/// Shared helpers so a single timed-out HTTP call cannot tear down the process.
/// HttpClient timeouts surface as <see cref="TaskCanceledException"/> (an
/// <see cref="OperationCanceledException"/>); those must be caught unless the host is stopping.
/// </summary>
public static class HostedLoopGuard
{
    public static readonly TimeSpan[] DefaultBackoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    ];

    public static bool IsHostStopping(Exception ex, CancellationToken stoppingToken) =>
        stoppingToken.IsCancellationRequested && ex is OperationCanceledException;

    public static bool IsRequestTimeout(Exception ex) =>
        ex is TimeoutException ||
        (ex is TaskCanceledException tce &&
         (tce.InnerException is TimeoutException || !tce.CancellationToken.IsCancellationRequested)) ||
        (ex is OperationCanceledException && ex.InnerException is TimeoutException);

    public static TimeSpan NextBackoff(int failureCount, IReadOnlyList<TimeSpan>? steps = null)
    {
        var table = steps ?? DefaultBackoff;
        if (table.Count == 0) return TimeSpan.FromMinutes(10);
        var index = Math.Clamp(failureCount - 1, 0, table.Count - 1);
        return table[index];
    }

    public static void LogCycleFailure(
        ILogger logger,
        Exception ex,
        string cycleName,
        string? targetHost,
        TimeSpan elapsed)
    {
        if (ex is HttpRequestException)
        {
            logger.LogWarning(
                ex,
                "{Cycle} HTTP failure talking to {Host} after {ElapsedMs}ms: {Message}",
                cycleName,
                targetHost ?? "(unknown)",
                (int)elapsed.TotalMilliseconds,
                ex.Message);
            return;
        }

        if (IsRequestTimeout(ex))
        {
            logger.LogWarning(
                "{Cycle} timed out talking to {Host} after {ElapsedMs}ms",
                cycleName,
                targetHost ?? "(unknown)",
                (int)elapsed.TotalMilliseconds);
            return;
        }

        logger.LogError(
            ex,
            "{Cycle} failed unexpectedly after {ElapsedMs}ms: {Message}",
            cycleName,
            (int)elapsed.TotalMilliseconds,
            ex.Message);
    }

    public static async Task DelayUnlessStoppingAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        if (delay <= TimeSpan.Zero) return;
        try
        {
            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
    }
}
