using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Config;

public sealed class UiPrefsService : IUiPrefsService
{
    public const int MinIdleMinutes = 1;
    public const int MaxIdleMinutes = 240;
    public static readonly TimeSpan UnlockedCookieTimeout = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ILogger<UiPrefsService> _logger;
    private readonly object _gate = new();
    private UiPrefs _current;
    private bool _configured;
    private Action<TimeSpan>? _applyCookieTimeout;

    public UiPrefsService(IDataRootResolver dataRoot, ILogger<UiPrefsService> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
        var loaded = Load();
        _configured = loaded is not null;
        _current = Normalize(loaded ?? new UiPrefs());
    }

    public bool IsConfigured
    {
        get { lock (_gate) return _configured; }
    }

    public TimeSpan CookieTimeout
    {
        get
        {
            lock (_gate) return TimeoutFor(_current);
        }
    }

    public UiPrefs Get()
    {
        lock (_gate) return Clone(_current);
    }

    public UiPrefs Save(UiPrefs incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        var next = Normalize(incoming);
        lock (_gate)
        {
            Persist(next);
            _current = next;
            _configured = true;
            _applyCookieTimeout?.Invoke(TimeoutFor(next));
        }

        _logger.LogInformation("UI prefs saved (idle lock {Enabled}, {Minutes} min)", next.IdleLockEnabled, next.IdleLockMinutes);
        return Clone(next);
    }

    public void BindCookieOptions(Action<TimeSpan> applyTimeout)
    {
        ArgumentNullException.ThrowIfNull(applyTimeout);
        lock (_gate)
        {
            _applyCookieTimeout = applyTimeout;
            applyTimeout(TimeoutFor(_current));
        }
    }

    /// <summary>
    /// Login cookie lifetime. Idle lock is enforced in the browser (and signs out on lock);
    /// the cookie must outlast that timer so mouse/keyboard activity alone cannot be
    /// overridden by a short HTTP-only session expiry.
    /// </summary>
    public static TimeSpan TimeoutFor(UiPrefs prefs) => UnlockedCookieTimeout;

    public static UiPrefs Normalize(UiPrefs p)
    {
        var minutes = p.IdleLockMinutes <= 0 ? 15 : Math.Clamp(p.IdleLockMinutes, MinIdleMinutes, MaxIdleMinutes);
        var folder = (p.ClipDefaultFolder ?? "Inbox").Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(folder)) folder = "Inbox";
        var recents = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in p.RecentNoteIds ?? [])
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id.Trim())) continue;
            recents.Add(id.Trim());
            if (recents.Count >= 24) break;
        }
        return new UiPrefs
        {
            IdleLockEnabled = p.IdleLockEnabled,
            IdleLockMinutes = minutes,
            ClipDefaultFolder = folder,
            RecentNoteIds = recents
        };
    }

    private UiPrefs? Load()
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(path), JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read UI prefs");
            return null;
        }
    }

    private void Persist(UiPrefs settings)
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string SettingsPath() =>
        Path.Combine(_dataRoot.ResolveDataRoot(), "config", "ui.json");

    private static UiPrefs Clone(UiPrefs p) => new()
    {
        IdleLockEnabled = p.IdleLockEnabled,
        IdleLockMinutes = p.IdleLockMinutes,
        ClipDefaultFolder = p.ClipDefaultFolder,
        RecentNoteIds = [.. p.RecentNoteIds]
    };
}
