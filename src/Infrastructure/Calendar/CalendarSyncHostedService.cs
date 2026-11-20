using System.Diagnostics;
using Jotdex.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Calendar;

/// <summary>
/// Runs calendar sync on a schedule. One timed-out CalDAV call must never escape
/// <see cref="ExecuteAsync"/> — HttpClient timeouts are <see cref="TaskCanceledException"/>,
/// which inherits from <see cref="OperationCanceledException"/> and would otherwise stop the host.
/// </summary>
public sealed class CalendarSyncHostedService : BackgroundService
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultFullInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(90);

    private readonly ICalendarSyncService _sync;
    private readonly ILogger<CalendarSyncHostedService> _logger;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _fullInterval;
    private readonly TimeSpan _syncTimeout;
    private readonly IReadOnlyList<TimeSpan> _backoff;

    [ActivatorUtilitiesConstructor]
    public CalendarSyncHostedService(ICalendarSyncService sync, ILogger<CalendarSyncHostedService> logger)
        : this(sync, logger, DefaultPollInterval, DefaultFullInterval, DefaultSyncTimeout, HostedLoopGuard.DefaultBackoff)
    {
    }

    /// <summary>Constructor with injectable intervals (used by unit tests).</summary>
    public CalendarSyncHostedService(
        ICalendarSyncService sync,
        ILogger<CalendarSyncHostedService> logger,
        TimeSpan pollInterval,
        TimeSpan fullInterval,
        TimeSpan syncTimeout,
        IReadOnlyList<TimeSpan> backoff)
    {
        _sync = sync;
        _logger = logger;
        _pollInterval = pollInterval;
        _fullInterval = fullInterval;
        _syncTimeout = syncTimeout;
        _backoff = backoff;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastFull = DateTimeOffset.MinValue;
        var failureCount = 0;
        var backoffUntil = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            await HostedLoopGuard.DelayUnlessStoppingAsync(_pollInterval, stoppingToken).ConfigureAwait(false);
            if (stoppingToken.IsCancellationRequested) break;

            if (DateTimeOffset.UtcNow < backoffUntil)
                continue;

            var due = _sync.ConsumeSyncRequest() || DateTimeOffset.UtcNow - lastFull > _fullInterval;
            if (!due || !_sync.GetStatus().Enabled) continue;

            var host = TryHost(_sync.GetStatus().ServerUrl);
            var sw = Stopwatch.StartNew();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(_syncTimeout);

            try
            {
                // Per-sync timeout is separate from the host stopping token so we can tell them apart.
                var (ok, error, _, _) = await _sync.SyncNowAsync(timeoutCts.Token).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested) break;

                if (ok)
                {
                    lastFull = DateTimeOffset.UtcNow;
                    failureCount = 0;
                    backoffUntil = DateTimeOffset.MinValue;
                }
                else
                {
                    failureCount++;
                    var wait = HostedLoopGuard.NextBackoff(failureCount, _backoff);
                    backoffUntil = DateTimeOffset.UtcNow.Add(wait);
                    _logger.LogWarning(
                        "Calendar sync failed talking to {Host} after {ElapsedMs}ms: {Error}. Retrying after {BackoffMinutes} minute(s) (failure #{FailureCount})",
                        host ?? "(unknown)",
                        (int)sw.Elapsed.TotalMilliseconds,
                        error ?? "unknown error",
                        Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)),
                        failureCount);
                }
            }
            catch (Exception ex) when (HostedLoopGuard.IsHostStopping(ex, stoppingToken))
            {
                break;
            }
            catch (Exception ex)
            {
                HostedLoopGuard.LogCycleFailure(_logger, ex, "Calendar sync", host, sw.Elapsed);
                failureCount++;
                var wait = HostedLoopGuard.NextBackoff(failureCount, _backoff);
                backoffUntil = DateTimeOffset.UtcNow.Add(wait);
                _logger.LogWarning(
                    "Calendar sync will retry after {BackoffMinutes} minute(s) (failure #{FailureCount})",
                    Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes)),
                    failureCount);
            }
        }
    }

    private static string? TryHost(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return null;
        return Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) ? uri.Host : serverUrl;
    }
}
