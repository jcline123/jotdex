using Jotdex.Infrastructure.Calendar;
using Jotdex.Infrastructure.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jotdex.Tests.Smoke;

public class CalendarSyncHostedServiceTests
{
    [Fact]
    public void Request_timeout_is_detected_separately_from_host_shutdown()
    {
        var timeout = new TaskCanceledException("timed out", new TimeoutException("The operation was canceled."));
        Assert.True(HostedLoopGuard.IsRequestTimeout(timeout));
        Assert.True(HostedLoopGuard.IsRequestTimeout(new TimeoutException()));
        Assert.False(HostedLoopGuard.IsRequestTimeout(new InvalidOperationException("boom")));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.True(HostedLoopGuard.IsHostStopping(new OperationCanceledException(cts.Token), cts.Token));
        Assert.False(HostedLoopGuard.IsHostStopping(timeout, CancellationToken.None));
    }

    [Fact]
    public void Backoff_steps_up_then_caps()
    {
        var steps = new[]
        {
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(10)
        };
        Assert.Equal(TimeSpan.FromMinutes(1), HostedLoopGuard.NextBackoff(1, steps));
        Assert.Equal(TimeSpan.FromMinutes(2), HostedLoopGuard.NextBackoff(2, steps));
        Assert.Equal(TimeSpan.FromMinutes(5), HostedLoopGuard.NextBackoff(3, steps));
        Assert.Equal(TimeSpan.FromMinutes(10), HostedLoopGuard.NextBackoff(4, steps));
        Assert.Equal(TimeSpan.FromMinutes(10), HostedLoopGuard.NextBackoff(99, steps));
    }

    [Fact]
    public async Task Timeout_during_sync_does_not_stop_the_service_and_next_cycle_runs()
    {
        var sync = new FakeCalendarSync
        {
            Enabled = true,
            ThrowTimeoutTimes = 1
        };
        var backoff = new[] { TimeSpan.FromMilliseconds(20) };
        var service = new CalendarSyncHostedService(
            sync,
            NullLogger<CalendarSyncHostedService>.Instance,
            pollInterval: TimeSpan.FromMilliseconds(20),
            fullInterval: TimeSpan.FromMilliseconds(30),
            syncTimeout: TimeSpan.FromSeconds(2),
            backoff: backoff);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && sync.SyncCalls < 2)
            await Task.Delay(40);

        Assert.True(sync.SyncCalls >= 2, $"Expected at least 2 sync attempts after a timeout, got {sync.SyncCalls}");
        Assert.True(sync.SawTimeout, "First sync should have thrown a timeout");
        Assert.True(sync.SuccessCalls >= 1, "A later cycle should still run after the timeout");

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
        await Task.Delay(50);
    }

    [Fact]
    public async Task Soft_timeout_result_still_allows_a_later_successful_cycle()
    {
        var sync = new FakeCalendarSync
        {
            Enabled = true,
            SoftFailTimes = 1
        };
        var service = new CalendarSyncHostedService(
            sync,
            NullLogger<CalendarSyncHostedService>.Instance,
            pollInterval: TimeSpan.FromMilliseconds(20),
            fullInterval: TimeSpan.FromMilliseconds(30),
            syncTimeout: TimeSpan.FromSeconds(2),
            backoff: [TimeSpan.FromMilliseconds(20)]);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && sync.SuccessCalls < 1)
            await Task.Delay(40);

        Assert.True(sync.SyncCalls >= 2);
        Assert.True(sync.SuccessCalls >= 1);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class FakeCalendarSync : ICalendarSyncService
    {
        public bool Enabled { get; set; }
        public int ThrowTimeoutTimes { get; set; }
        public int SoftFailTimes { get; set; }
        public int SyncCalls { get; private set; }
        public int SuccessCalls { get; private set; }
        public bool SawTimeout { get; private set; }

        public CalendarSyncStatus GetStatus() => new()
        {
            Enabled = Enabled,
            ServerUrl = "https://caldav.example.invalid/"
        };

        public (bool Success, string? Error) Save(CalendarSettings incoming, string? password) => (true, null);

        public Task<CalDavResult> DiscoverAsync(string? serverUrl, string? username, string? password, CancellationToken ct = default) =>
            Task.FromResult(new CalDavResult { Success = true });

        public async Task<(bool Success, string? Error, int Upserted, int Removed)> SyncNowAsync(CancellationToken ct = default)
        {
            SyncCalls++;
            if (ThrowTimeoutTimes > 0)
            {
                ThrowTimeoutTimes--;
                SawTimeout = true;
                await Task.Yield();
                throw new TaskCanceledException("A task was canceled.", new TimeoutException("The operation was canceled."));
            }

            if (SoftFailTimes > 0)
            {
                SoftFailTimes--;
                return (false, "Calendar request timed out.", 0, 0);
            }

            ct.ThrowIfCancellationRequested();
            SuccessCalls++;
            return (true, null, 0, 0);
        }

        public void RequestSync() { }

        public bool ConsumeSyncRequest() => Enabled;

        public void AfterVaultRescan() { }
    }
}
