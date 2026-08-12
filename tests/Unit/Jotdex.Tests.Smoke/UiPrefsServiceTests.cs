using Jotdex.Core.Configuration;
using Jotdex.Infrastructure.Config;

namespace Jotdex.Tests.Smoke;

public class UiPrefsServiceTests
{
    [Fact]
    public void Normalize_ClampsMinutesAndFolder()
    {
        var n = UiPrefsService.Normalize(new UiPrefs
        {
            IdleLockEnabled = true,
            IdleLockMinutes = 999,
            ClipDefaultFolder = @"Work\Inbox"
        });
        Assert.Equal(240, n.IdleLockMinutes);
        Assert.Equal("Work/Inbox", n.ClipDefaultFolder);
        Assert.Empty(n.RecentNoteIds);
    }

    [Fact]
    public void Normalize_DedupesAndCapsRecents()
    {
        var ids = Enumerable.Range(0, 30).Select(i => $"id-{i}").ToList();
        ids.Insert(0, "id-1");
        var n = UiPrefsService.Normalize(new UiPrefs { RecentNoteIds = ids });
        Assert.Equal(24, n.RecentNoteIds.Count);
        Assert.Equal("id-1", n.RecentNoteIds[0]);
        Assert.Equal("id-0", n.RecentNoteIds[1]);
        Assert.Equal(1, n.RecentNoteIds.Count(x => x == "id-1"));
    }

    [Fact]
    public void CookieTimeout_StaysLong_RegardlessOfIdleLock()
    {
        // Idle lock is enforced in the browser (and signs out on lock). A short cookie
        // matching idle minutes caused early 401 locks while the user was still active.
        var on = UiPrefsService.TimeoutFor(new UiPrefs { IdleLockEnabled = true, IdleLockMinutes = 60 });
        Assert.Equal(UiPrefsService.UnlockedCookieTimeout, on);

        var off = UiPrefsService.TimeoutFor(new UiPrefs { IdleLockEnabled = false, IdleLockMinutes = 15 });
        Assert.Equal(UiPrefsService.UnlockedCookieTimeout, off);
    }
}
