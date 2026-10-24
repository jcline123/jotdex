using System.Text.Json;
using System.Text.Json.Serialization;
using Jotdex.Core.Configuration;
using Jotdex.Core.Secrets;
using Jotdex.Core.Vault;
using Jotdex.Infrastructure.Vault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jotdex.Infrastructure.Calendar;

public sealed class CalendarSettings
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "https://caldav.icloud.com/";
    public string Username { get; set; } = "";
    public string CalendarUrl { get; set; } = "";
    public string CalendarName { get; set; } = "";
    /// <summary>https origin used for note links on calendar events, such as https://jotdex.example.com.</summary>
    public string PublicBaseUrl { get; set; } = "";
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? LastError { get; set; }
    public int LastUpserted { get; set; }
    public int LastRemoved { get; set; }
    /// <summary>Task id to CalDAV event URL. Kept in config so a move kit can still delete events that leave the todo list.</summary>
    public Dictionary<string, string> Events { get; set; } = new(StringComparer.Ordinal);
}

public sealed class CalendarSyncStatus
{
    public bool Enabled { get; init; }
    public string ServerUrl { get; init; } = "";
    public string Username { get; init; } = "";
    public string CalendarUrl { get; init; } = "";
    public string CalendarName { get; init; } = "";
    public string PublicBaseUrl { get; init; } = "";
    public bool HasPassword { get; init; }
    public DateTimeOffset? LastSyncUtc { get; init; }
    public string? LastError { get; init; }
    public int LastUpserted { get; init; }
    public int LastRemoved { get; init; }
    public string Hint { get; init; } =
        "Open todos are all-day events titled todo: on the calendar you choose. Todos without a future due date stay on today and move forward each day until you complete or delete them. An all-day event titled todo: on that calendar is added to the todo list.";
}

public interface ICalendarSyncService : IVaultRescanObserver
{
    CalendarSyncStatus GetStatus();
    (bool Success, string? Error) Save(CalendarSettings incoming, string? password);
    Task<CalDavResult> DiscoverAsync(string? serverUrl, string? username, string? password, CancellationToken ct = default);
    Task<(bool Success, string? Error, int Upserted, int Removed)> SyncNowAsync(CancellationToken ct = default);
    void RequestSync();
    bool ConsumeSyncRequest();
}

public sealed class CalendarSyncService : ICalendarSyncService
{
    public const string PasswordKey = "calendar.caldav.appPassword";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDataRootResolver _dataRoot;
    private readonly ISecretStore _secrets;
    private readonly IServiceProvider _services;
    private readonly CalDavClient _caldav;
    private readonly ILogger<CalendarSyncService> _logger;
    private readonly object _gate = new();
    private CalendarSettings _settings;
    private bool _syncRequested = true;
    private DateTimeOffset _syncAfter = DateTimeOffset.UtcNow.AddSeconds(20);

    public CalendarSyncService(
        IDataRootResolver dataRoot,
        ISecretStore secrets,
        IServiceProvider services,
        CalDavClient caldav,
        ILogger<CalendarSyncService> logger)
    {
        _dataRoot = dataRoot;
        _secrets = secrets;
        _services = services;
        _caldav = caldav;
        _logger = logger;
        _settings = Load() ?? new CalendarSettings();
    }

    public CalendarSyncStatus GetStatus()
    {
        lock (_gate)
        {
            return ToStatus(_settings);
        }
    }

    public (bool Success, string? Error) Save(CalendarSettings incoming, string? password)
    {
        var next = new CalendarSettings
        {
            Enabled = incoming.Enabled,
            ServerUrl = string.IsNullOrWhiteSpace(incoming.ServerUrl) ? "https://caldav.icloud.com/" : incoming.ServerUrl.Trim(),
            Username = (incoming.Username ?? "").Trim(),
            CalendarUrl = (incoming.CalendarUrl ?? "").Trim(),
            CalendarName = (incoming.CalendarName ?? "").Trim(),
            PublicBaseUrl = incoming.PublicBaseUrl ?? ""
        };

        if (!next.ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return (false, "Calendar server must be an https address.");
        if (!CalendarPlanner.TryNormalizePublicBase(next.PublicBaseUrl, out var publicBase, out var publicError))
            return (false, publicError);
        next.PublicBaseUrl = publicBase;

        var willHavePassword = !string.IsNullOrEmpty(password) || _secrets.Has(PasswordKey);
        if (next.Enabled)
        {
            if (string.IsNullOrWhiteSpace(next.Username))
                return (false, "Apple ID is required.");
            if (!willHavePassword)
                return (false, "An app-specific password is required.");
            if (string.IsNullOrWhiteSpace(next.CalendarUrl))
                return (false, "Choose which calendar receives todos.");
        }

        if (!string.IsNullOrEmpty(password))
            _secrets.Set(PasswordKey, password);

        lock (_gate)
        {
            next.LastSyncUtc = _settings.LastSyncUtc;
            next.LastError = _settings.LastError;
            next.LastUpserted = _settings.LastUpserted;
            next.LastRemoved = _settings.LastRemoved;
            next.Events = new Dictionary<string, string>(_settings.Events ?? [], StringComparer.Ordinal);
            _settings = next;
            Persist(next);
        }
        if (next.Enabled)
            RequestSync();
        return (true, null);
    }

    public async Task<CalDavResult> DiscoverAsync(string? serverUrl, string? username, string? password, CancellationToken ct = default)
    {
        var user = string.IsNullOrWhiteSpace(username) ? _settings.Username : username.Trim();
        var pass = password;
        if (string.IsNullOrEmpty(pass))
            _secrets.TryGet(PasswordKey, out pass);
        var server = string.IsNullOrWhiteSpace(serverUrl) ? _settings.ServerUrl : serverUrl;
        return await _caldav.ListCalendarsAsync(server, user, pass ?? "", ct).ConfigureAwait(false);
    }

    public async Task<(bool Success, string? Error, int Upserted, int Removed)> SyncNowAsync(CancellationToken ct = default)
    {
        CalendarSettings settings;
        lock (_gate) settings = Clone(_settings);
        if (!settings.Enabled)
            return (true, null, 0, 0);
        if (!_secrets.TryGet(PasswordKey, out var password) || string.IsNullOrWhiteSpace(password))
            return Fail("App password is not saved.");
        if (string.IsNullOrWhiteSpace(settings.CalendarUrl) || string.IsNullOrWhiteSpace(settings.Username))
            return Fail("Calendar account is incomplete.");

        try
        {
            var tasks = _services.GetRequiredService<IVaultTaskService>();
            var today = DateOnly.FromDateTime(DateTime.Now);
            var listed = await _caldav.ListEventsAsync(
                settings.CalendarUrl,
                settings.Username,
                password,
                today.AddDays(-2),
                today.AddDays(400),
                ct).ConfigureAwait(false);
            var remoteByUid = new Dictionary<string, CalendarRemoteEvent>(StringComparer.Ordinal);
            string? error = listed.Success ? null : listed.Error;
            if (listed.Success)
            {
                foreach (var ev in listed.Events)
                {
                    if (string.IsNullOrWhiteSpace(ev.Uid) || remoteByUid.ContainsKey(ev.Uid)) continue;
                    remoteByUid[ev.Uid] = ev;
                }

                var change = new Jotdex.Core.Integrations.NoteChangeContext(
                    Jotdex.Core.Integrations.NoteChangeVia.External, "Calendar");
                foreach (var ev in remoteByUid.Values)
                {
                    if (CalendarPlanner.IsJotdexUid(ev.Uid) || !ev.AllDay) continue;
                    if (!CalendarPlanner.TryParseImportSummary(ev.Summary, out var title)) continue;
                    var due = ev.Start is DateOnly start && start > today
                        ? new DateTime(start.Year, start.Month, start.Day, 12, 0, 0, DateTimeKind.Local).ToString("O")
                        : null;
                    var imported = tasks.UpsertFromCalendar(ev.Uid, title, due, change);
                    if (!imported.Success)
                        error ??= imported.Error;
                }
            }

            var open = tasks.ListOpenTasks();
            var previousEvents = settings.Events ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var keep = new Dictionary<string, string>(StringComparer.Ordinal);
            var upserted = 0;

            foreach (var task in open)
            {
                ct.ThrowIfCancellationRequested();
                if (task.FromCalendar && !string.IsNullOrWhiteSpace(task.CalendarUid))
                {
                    if (!listed.Success)
                    {
                        if (previousEvents.TryGetValue(task.Id, out var previous))
                            keep[task.Id] = previous;
                        continue;
                    }
                    if (!remoteByUid.TryGetValue(task.CalendarUid, out var remote))
                    {
                        tasks.Complete(task.Id);
                        continue;
                    }
                    var day = CalendarPlanner.TargetDay(task.Due, today);
                    var wanted = CalendarPlanner.FormatSummary(task.Text);
                    if (remote.Start != day || !string.Equals(remote.Summary.Trim(), wanted, StringComparison.Ordinal))
                    {
                        var ics = CalendarPlanner.BuildIcs(task.Id, task.Text, day, noteUrl: null, uidOverride: remote.Uid);
                        var put = await _caldav.PutEventAsync(remote.Href, settings.Username, password, ics, ct).ConfigureAwait(false);
                        if (!put.Success)
                        {
                            error ??= put.Error;
                            if (previousEvents.TryGetValue(task.Id, out var previous))
                                keep[task.Id] = previous;
                            else
                                keep[task.Id] = remote.Href;
                            continue;
                        }
                    }
                    keep[task.Id] = remote.Href;
                    upserted++;
                    continue;
                }

                var href = CalendarPlanner.EventHref(settings.CalendarUrl, task.Id);
                var noteUrl = task.StandaloneTodosMd
                    ? null
                    : CalendarPlanner.NoteLink(settings.PublicBaseUrl, task.NoteId);
                var owned = CalendarPlanner.BuildIcs(
                    task.Id,
                    task.Text,
                    CalendarPlanner.TargetDay(task.Due, today),
                    noteUrl);
                var ownedPut = await _caldav.PutEventAsync(href, settings.Username, password, owned, ct).ConfigureAwait(false);
                if (!ownedPut.Success)
                {
                    error ??= ownedPut.Error;
                    if (previousEvents.TryGetValue(task.Id, out var previous))
                        keep[task.Id] = previous;
                    continue;
                }
                if (previousEvents.TryGetValue(task.Id, out var oldHref)
                    && !string.Equals(oldHref, href, StringComparison.OrdinalIgnoreCase))
                {
                    await _caldav.DeleteEventAsync(oldHref, settings.Username, password, ct).ConfigureAwait(false);
                }
                keep[task.Id] = href;
                upserted++;
            }

            var removed = 0;
            foreach (var pair in previousEvents)
            {
                if (keep.ContainsKey(pair.Key)) continue;
                var deleted = await _caldav.DeleteEventAsync(pair.Value, settings.Username, password, ct).ConfigureAwait(false);
                if (!deleted.Success)
                {
                    error ??= deleted.Error;
                    keep[pair.Key] = pair.Value;
                }
                else
                    removed++;
            }

            lock (_gate)
            {
                _settings.LastSyncUtc = DateTimeOffset.UtcNow;
                _settings.LastError = error;
                _settings.LastUpserted = upserted;
                _settings.LastRemoved = removed;
                _settings.Events = keep;
                Persist(_settings);
            }
            if (error is null)
                _logger.LogInformation("Calendar sync upserted {Upserted} todos and removed {Removed}", upserted, removed);
            else
                _logger.LogWarning("Calendar sync partial: {Error}", error);
            return (error is null, error, upserted, removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(ex.Message);
        }
    }

    public void RequestSync()
    {
        lock (_gate)
        {
            var quiet = DateTimeOffset.UtcNow.AddSeconds(8);
            if (!_syncRequested || quiet < _syncAfter)
                _syncAfter = quiet;
            _syncRequested = true;
        }
    }

    public bool ConsumeSyncRequest()
    {
        lock (_gate)
        {
            if (!_settings.Enabled || !_syncRequested || DateTimeOffset.UtcNow < _syncAfter)
                return false;
            _syncRequested = false;
            return true;
        }
    }

    public void AfterVaultRescan() => RequestSync();

    private (bool Success, string? Error, int Upserted, int Removed) Fail(string error)
    {
        lock (_gate)
        {
            _settings.LastError = error;
            _settings.LastSyncUtc = DateTimeOffset.UtcNow;
            Persist(_settings);
        }
        _logger.LogWarning("Calendar sync failed: {Error}", error);
        return (false, error, 0, 0);
    }

    private CalendarSyncStatus ToStatus(CalendarSettings s) => new()
    {
        Enabled = s.Enabled,
        ServerUrl = s.ServerUrl,
        Username = s.Username,
        CalendarUrl = s.CalendarUrl,
        CalendarName = s.CalendarName,
        PublicBaseUrl = s.PublicBaseUrl,
        HasPassword = _secrets.Has(PasswordKey),
        LastSyncUtc = s.LastSyncUtc,
        LastError = s.LastError,
        LastUpserted = s.LastUpserted,
        LastRemoved = s.LastRemoved
    };

    private CalendarSettings? Load()
    {
        var path = SettingsPath();
        if (!File.Exists(path)) return null;
        try
        {
            var loaded = JsonSerializer.Deserialize<CalendarSettings>(File.ReadAllText(path), JsonOpts);
            if (loaded is not null)
                loaded.Events ??= new Dictionary<string, string>(StringComparer.Ordinal);
            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read calendar settings");
            return null;
        }
    }

    private void Persist(CalendarSettings settings)
    {
        var path = SettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    private string SettingsPath() => Path.Combine(_dataRoot.ResolveDataRoot(), "config", "calendar.json");

    private static CalendarSettings Clone(CalendarSettings s) => new()
    {
        Enabled = s.Enabled,
        ServerUrl = s.ServerUrl,
        Username = s.Username,
        CalendarUrl = s.CalendarUrl,
        CalendarName = s.CalendarName,
        PublicBaseUrl = s.PublicBaseUrl,
        LastSyncUtc = s.LastSyncUtc,
        LastError = s.LastError,
        LastUpserted = s.LastUpserted,
        LastRemoved = s.LastRemoved,
        Events = new Dictionary<string, string>(s.Events ?? [], StringComparer.Ordinal)
    };
}

public sealed class CalendarSyncHostedService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly ICalendarSyncService _sync;
    private readonly ILogger<CalendarSyncHostedService> _logger;

    public CalendarSyncHostedService(ICalendarSyncService sync, ILogger<CalendarSyncHostedService> logger)
    {
        _sync = sync;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastFull = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var due = _sync.ConsumeSyncRequest() || DateTimeOffset.UtcNow - lastFull > TimeSpan.FromMinutes(10);
            if (!due || !_sync.GetStatus().Enabled) continue;
            try
            {
                await _sync.SyncNowAsync(stoppingToken).ConfigureAwait(false);
                lastFull = DateTimeOffset.UtcNow;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Scheduled calendar sync failed");
            }
        }
    }
}
