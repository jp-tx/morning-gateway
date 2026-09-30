using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using MorningGateway.Models;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Minimal hand-rolled CalDAV (RFC 4791) client: just enough PROPFIND/REPORT
/// support to discover a user's calendars and pull events out of one, against
/// iCloud, Fastmail, Nextcloud or any other standards-compliant CalDAV server.
/// Google's CalDAV endpoint requires OAuth rather than basic auth, so Google
/// accounts should use <see cref="GoogleCalendarProvider"/> instead.
/// </summary>
public class CalDavClient
{
    static readonly XNamespace Dav = "DAV:";
    static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    static readonly XNamespace CalendarServer = "http://calendarserver.org/ns/";

    readonly HttpClient _http;

    public CalDavClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Walks principal -> calendar-home-set -> child calendars, per RFC 4791/6764.</summary>
    public async Task<IReadOnlyList<DiscoveredCalendar>> DiscoverCalendarsAsync(string serverBaseUrl, string username, string password, CancellationToken cancellationToken = default)
    {
        var principalUrl = await FindHrefPropertyAsync(serverBaseUrl, username, password, Dav + "current-user-principal", cancellationToken).ConfigureAwait(false)
            ?? serverBaseUrl;

        var homeSetUrl = await FindHrefPropertyAsync(principalUrl, username, password, CalDav + "calendar-home-set", cancellationToken).ConfigureAwait(false)
            ?? principalUrl;

        var body = new XElement(Dav + "propfind",
            new XAttribute(XNamespace.Xmlns + "D", Dav),
            new XAttribute(XNamespace.Xmlns + "C", CalDav),
            new XAttribute(XNamespace.Xmlns + "CS", CalendarServer),
            new XElement(Dav + "prop",
                new XElement(Dav + "resourcetype"),
                new XElement(Dav + "displayname"),
                new XElement(CalendarServer + "calendar-color")));

        var doc = await SendXmlRequestAsync(homeSetUrl, "PROPFIND", username, password, body, depth: "1", cancellationToken).ConfigureAwait(false);
        var results = new List<DiscoveredCalendar>();
        if (doc is null)
        {
            return results;
        }

        foreach (var response in doc.Descendants(Dav + "response"))
        {
            var resourceType = response.Descendants(Dav + "resourcetype").FirstOrDefault();
            var isCalendar = resourceType?.Element(CalDav + "calendar") is not null;
            if (!isCalendar)
            {
                continue;
            }

            var href = response.Element(Dav + "href")?.Value;
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var displayName = response.Descendants(Dav + "displayname").FirstOrDefault()?.Value ?? href;
            var color = response.Descendants(CalendarServer + "calendar-color").FirstOrDefault()?.Value;
            results.Add(new DiscoveredCalendar(ResolveUrl(homeSetUrl, href), displayName, color));
        }

        return results;
    }

    /// <summary>REPORT calendar-query for VEVENTs overlapping the given UTC time range.</summary>
    public async Task<IReadOnlyList<string>> QueryEventsAsync(string calendarUrl, string username, string password, DateTimeOffset rangeStartUtc, DateTimeOffset rangeEndUtc, CancellationToken cancellationToken = default)
    {
        var start = rangeStartUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = rangeEndUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");

        var body = new XElement(CalDav + "calendar-query",
            new XAttribute(XNamespace.Xmlns + "D", Dav),
            new XAttribute(XNamespace.Xmlns + "C", CalDav),
            new XElement(Dav + "prop",
                new XElement(Dav + "getetag"),
                new XElement(CalDav + "calendar-data")),
            new XElement(CalDav + "filter",
                new XElement(CalDav + "comp-filter", new XAttribute("name", "VCALENDAR"),
                    new XElement(CalDav + "comp-filter", new XAttribute("name", "VEVENT"),
                        new XElement(CalDav + "time-range", new XAttribute("start", start), new XAttribute("end", end))))));

        var doc = await SendXmlRequestAsync(calendarUrl, "REPORT", username, password, body, depth: "1", cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return Array.Empty<string>();
        }

        return doc.Descendants(CalDav + "calendar-data")
            .Select(e => e.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();
    }

    async Task<string?> FindHrefPropertyAsync(string url, string username, string password, XName propertyName, CancellationToken cancellationToken)
    {
        var body = new XElement(Dav + "propfind",
            new XAttribute(XNamespace.Xmlns + "D", Dav),
            new XAttribute(XNamespace.Xmlns + "C", CalDav),
            new XElement(Dav + "prop", new XElement(propertyName)));

        var doc = await SendXmlRequestAsync(url, "PROPFIND", username, password, body, depth: "0", cancellationToken).ConfigureAwait(false);
        var href = doc?.Descendants(propertyName).Descendants(Dav + "href").FirstOrDefault()?.Value;
        return string.IsNullOrWhiteSpace(href) ? null : ResolveUrl(url, href);
    }

    async Task<XDocument?> SendXmlRequestAsync(string url, string method, string username, string password, XElement body, string depth, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = new StringContent(new XDocument(body).ToString(), Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", depth);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return XDocument.Parse(text);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string ResolveUrl(string baseUrl, string href)
    {
        // On Linux/Android "/path" parses as an absolute file:// URI, so only accept real http(s) URLs here.
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var baseUri = new Uri(baseUrl);
        return new Uri(new Uri($"{baseUri.Scheme}://{baseUri.Authority}"), href).ToString();
    }
}
