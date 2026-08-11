namespace Jotdex.Core.Configuration;

/// <summary>Server-side UI prefs shared by every browser that talks to this Jotdex instance.</summary>
public sealed class UiPrefs
{
    public bool IdleLockEnabled { get; set; }
    public int IdleLockMinutes { get; set; } = 15;
    public string ClipDefaultFolder { get; set; } = "Inbox";
    /// <summary>Most-recently-viewed note ids (home screen), newest first.</summary>
    public List<string> RecentNoteIds { get; set; } = [];
}

public interface IUiPrefsService
{
    /// <summary>True when <c>data/config/ui.json</c> exists (not just defaults).</summary>
    bool IsConfigured { get; }
    UiPrefs Get();
    UiPrefs Save(UiPrefs incoming);
    /// <summary>Cookie lifetime matching idle lock when enabled; 7 days when lock is off.</summary>
    TimeSpan CookieTimeout { get; }
    void BindCookieOptions(Action<TimeSpan> applyTimeout);
}
