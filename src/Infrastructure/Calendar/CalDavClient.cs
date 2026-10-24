using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace Jotdex.Infrastructure.Calendar;

public sealed class CalDavCalendarInfo
{
    public required string Url { get; init; }
    public required string Name { get; init; }
}

public sealed class CalDavResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<CalDavCalendarInfo> Calendars { get; init; } = [];
}

public sealed class CalendarRemoteEvent
{
    public required string Href { get; init; }
    public required string Uid { get; init; }
    public required string Summary { get; init; }
    public DateOnly? Start { get; init; }
    public bool AllDay { get; init; }
}

public sealed class CalDavEventsResult
{
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<CalendarRemoteEvent> Events { get; init; } = [];
}

public sealed class CalDavClient
{
    private const string UserAgent = "Jotdex";

    public async Task<CalDavResult> ListCalendarsAsync(string serverUrl, string username, string password, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Fail("Apple ID and an app-specific password are required.");
        if (!TryNormalizeServer(serverUrl, out var server, out var error))
            return Fail(error!);

        try
        {
            var principalBody = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:propfind xmlns:d="DAV:">
                  <d:prop><d:current-user-principal/></d:prop>
                </d:propfind>
                """;
            var principal = await PropfindAsync(server.ToString(), username, password, principalBody, "0", ct).ConfigureAwait(false);
            if (!principal.Success) return Fail(principal.Error ?? "Could not read the calendar account.");
            var principalHref = FirstHref(principal.Xml, "current-user-principal");
            if (principalHref is null)
                return Fail("The calendar server did not return an account principal. Check the Apple ID and app password.");

            var homeBody = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
                  <d:prop><c:calendar-home-set/></d:prop>
                </d:propfind>
                """;
            var home = await PropfindAsync(Absolute(server, principalHref), username, password, homeBody, "0", ct).ConfigureAwait(false);
            if (!home.Success) return Fail(home.Error ?? "Could not read the calendar home.");
            var homeHref = FirstHref(home.Xml, "calendar-home-set");
            if (homeHref is null)
                return Fail("The account has no calendar home. Confirm CalDAV is available for this Apple ID.");

            var listBody = """
                <?xml version="1.0" encoding="utf-8"?>
                <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
                  <d:prop>
                    <d:resourcetype/>
                    <d:displayname/>
                  </d:prop>
                </d:propfind>
                """;
            var homeUrl = Absolute(server, homeHref);
            var list = await PropfindAsync(homeUrl, username, password, listBody, "1", ct).ConfigureAwait(false);
            if (!list.Success) return Fail(list.Error ?? "Could not list calendars.");

            var homeUri = new Uri(homeUrl);
            var calendars = new List<CalDavCalendarInfo>();
            foreach (var response in list.Xml!.Descendants().Where(e => e.Name.LocalName == "response"))
            {
                var href = response.Elements().FirstOrDefault(e => e.Name.LocalName == "href")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(href)) continue;
                var types = response.Descendants().Where(e => e.Parent?.Name.LocalName == "resourcetype").Select(e => e.Name.LocalName).ToList();
                if (!types.Any(t => t.Equals("calendar", StringComparison.OrdinalIgnoreCase))) continue;
                var name = response.Descendants().FirstOrDefault(e => e.Name.LocalName == "displayname")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = href.Trim('/').Split('/').LastOrDefault() ?? "Calendar";
                calendars.Add(new CalDavCalendarInfo
                {
                    Url = Absolute(homeUri, href),
                    Name = name
                });
            }

            calendars = calendars
                .GroupBy(c => c.Url, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (calendars.Count == 0)
                return Fail("No calendars were found on that account.");

            return new CalDavResult { Success = true, Calendars = calendars };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(Friendly(ex));
        }
    }

    public async Task<CalDavEventsResult> ListEventsAsync(string calendarUrl, string username, string password, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(calendarUrl))
            return new CalDavEventsResult { Success = false, Error = "Choose which calendar receives todos." };
        var start = from.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "T000000Z";
        var end = to.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "T000000Z";
        var xml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:prop>
                <d:getetag/>
                <c:calendar-data/>
              </d:prop>
              <c:filter>
                <c:comp-filter name="VCALENDAR">
                  <c:comp-filter name="VEVENT">
                    <c:time-range start="{start}" end="{end}"/>
                  </c:comp-filter>
                </c:comp-filter>
              </c:filter>
            </c:calendar-query>
            """;
        try
        {
            var request = new HttpRequestMessage(new HttpMethod("REPORT"), calendarUrl)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            };
            request.Headers.TryAddWithoutValidation("Depth", "1");
            using var response = await SendAsync(request, username, password, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
                return new CalDavEventsResult { Success = false, Error = StatusMessage(response.StatusCode) };
            XDocument doc;
            try
            {
                doc = XDocument.Parse(body);
            }
            catch
            {
                return new CalDavEventsResult { Success = false, Error = "The calendar server returned a response Jotdex could not read." };
            }

            var calendarUri = new Uri(calendarUrl);
            var events = new List<CalendarRemoteEvent>();
            foreach (var node in doc.Descendants().Where(e => e.Name.LocalName == "response"))
            {
                var href = node.Elements().FirstOrDefault(e => e.Name.LocalName == "href")?.Value?.Trim();
                var data = node.Descendants().FirstOrDefault(e => e.Name.LocalName == "calendar-data")?.Value;
                if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(data)) continue;
                var absolute = Absolute(calendarUri, href);
                foreach (var parsed in CalendarPlanner.ParseEvents(data))
                {
                    events.Add(new CalendarRemoteEvent
                    {
                        Href = absolute,
                        Uid = parsed.Uid,
                        Summary = parsed.Summary,
                        Start = parsed.Start,
                        AllDay = parsed.AllDay
                    });
                }
            }
            return new CalDavEventsResult { Success = true, Events = events };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CalDavEventsResult { Success = false, Error = Friendly(ex) };
        }
    }

    public async Task<CalDavResult> PutEventAsync(string eventUrl, string username, string password, string ics, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Put, eventUrl)
            {
                Content = new StringContent(ics, Encoding.UTF8, "text/calendar")
            }, username, password, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.Created)
                return new CalDavResult { Success = true };
            return Fail(StatusMessage(response.StatusCode));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(Friendly(ex));
        }
    }

    public async Task<CalDavResult> DeleteEventAsync(string eventUrl, string username, string password, CancellationToken ct)
    {
        try
        {
            using var response = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, eventUrl), username, password, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent or HttpStatusCode.Gone)
                return new CalDavResult { Success = true };
            return Fail(StatusMessage(response.StatusCode));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(Friendly(ex));
        }
    }

    private async Task<(bool Success, string? Error, XDocument? Xml)> PropfindAsync(
        string url, string username, string password, string xml, string depth, CancellationToken ct)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), url)
        {
            Content = new StringContent(xml, Encoding.UTF8, "application/xml")
        };
        request.Headers.TryAddWithoutValidation("Depth", depth);
        using var response = await SendAsync(request, username, password, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.MultiStatus)
            return (false, StatusMessage(response.StatusCode), null);
        try
        {
            return (true, null, XDocument.Parse(body));
        }
        catch
        {
            return (false, "The calendar server returned a response Jotdex could not read.", null);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string username, string password, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(40) };
        var auth = Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
        for (var hop = 0; hop < 6; hop++)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.ParseAdd(UserAgent);
            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
            {
                var buffered = await BufferAsync(response, ct).ConfigureAwait(false);
                response.Dispose();
                return buffered;
            }
            var location = response.Headers.Location;
            if (location is null)
            {
                var buffered = await BufferAsync(response, ct).ConfigureAwait(false);
                response.Dispose();
                return buffered;
            }
            var next = location.IsAbsoluteUri ? location : new Uri(request.RequestUri!, location);
            if (!string.Equals(next.Scheme, "https", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The calendar server redirected off HTTPS.");
            var method = response.StatusCode == HttpStatusCode.SeeOther ? HttpMethod.Get : request.Method;
            var nextRequest = new HttpRequestMessage(method, next);
            if (request.Content is not null && method != HttpMethod.Get)
            {
                var bytes = await request.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                nextRequest.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                    nextRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Dispose();
            response.Dispose();
            request = nextRequest;
        }
        throw new InvalidOperationException("The calendar server redirected too many times.");
    }

    private static async Task<HttpResponseMessage> BufferAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var bytes = response.Content is null
            ? []
            : await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var copy = new HttpResponseMessage(response.StatusCode) { Content = new ByteArrayContent(bytes) };
        if (response.Content?.Headers.ContentType is not null)
            copy.Content.Headers.ContentType = response.Content.Headers.ContentType;
        return copy;
    }

    private static string? FirstHref(XDocument? doc, string propertyLocalName)
    {
        if (doc is null) return null;
        var prop = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == propertyLocalName);
        var href = prop?.Descendants().FirstOrDefault(e => e.Name.LocalName == "href")?.Value?.Trim();
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    private static string Absolute(Uri baseUri, string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return baseUri.ToString();
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            return absolute.ToString();
        return new Uri(baseUri, href).ToString();
    }

    private static bool TryNormalizeServer(string? serverUrl, out Uri server, out string? error)
    {
        error = null;
        var raw = string.IsNullOrWhiteSpace(serverUrl) ? "https://caldav.icloud.com/" : serverUrl.Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out server!) || server.Scheme != "https")
        {
            server = null!;
            error = "Calendar server must be an https address.";
            return false;
        }
        return true;
    }

    private static CalDavResult Fail(string error) => new() { Success = false, Error = error };

    private static string StatusMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "The calendar server rejected the sign-in. Use an app-specific password, not the Apple ID password.",
        HttpStatusCode.NotFound => "That calendar was not found.",
        _ => "The calendar server returned " + (int)status + "."
    };

    private static string Friendly(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? "Could not reach the calendar server."
            : ex.Message;
}
