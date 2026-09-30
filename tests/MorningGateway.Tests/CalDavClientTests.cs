using MorningGateway.Services.Calendar;

namespace MorningGateway.Tests;

// README "Adding an iCloud calendar via CalDAV": server URL + username + app password -> "Find calendars" -> pick one.
public class CalDavClientTests
{
    const string Ns = "xmlns:D=\"DAV:\" xmlns:C=\"urn:ietf:params:xml:ns:caldav\" xmlns:CS=\"http://calendarserver.org/ns/\"";

    static string Multi(string inner) => $"<?xml version=\"1.0\"?><D:multistatus {Ns}>{inner}</D:multistatus>";

    static string PropResponse(string href, string prop) =>
        $"<D:response><D:href>{href}</D:href><D:propstat><D:prop>{prop}</D:prop></D:propstat></D:response>";

    [Fact]
    public async Task Discovery_walks_principal_then_home_set_then_lists_only_calendars()
    {
        var handler = new StubHandler((req, body) =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (body.Contains("current-user-principal"))
                return StubHandler.Text(Multi(PropResponse("/", "<D:current-user-principal><D:href>/123/principal/</D:href></D:current-user-principal>")), (System.Net.HttpStatusCode)207);
            if (body.Contains("calendar-home-set"))
                return StubHandler.Text(Multi(PropResponse(path, "<C:calendar-home-set><D:href>https://p01-caldav.icloud.com/123/calendars/</D:href></C:calendar-home-set>")), (System.Net.HttpStatusCode)207);
            return StubHandler.Text(Multi(
                PropResponse("/123/calendars/", "<D:resourcetype><D:collection/></D:resourcetype><D:displayname>Root</D:displayname>") +
                PropResponse("/123/calendars/home/", "<D:resourcetype><D:collection/><C:calendar/></D:resourcetype><D:displayname>Home</D:displayname><CS:calendar-color>#FF0000</CS:calendar-color>") +
                PropResponse("/123/calendars/work/", "<D:resourcetype><D:collection/><C:calendar/></D:resourcetype><D:displayname>Work</D:displayname>")), (System.Net.HttpStatusCode)207);
        });

        var found = await new CalDavClient(handler.Client()).DiscoverCalendarsAsync("https://caldav.icloud.com", "me@icloud.com", "app-pw");

        Assert.Equal(new[] { "Home", "Work" }, found.Select(c => c.DisplayName));
        Assert.Equal("https://p01-caldav.icloud.com/123/calendars/home/", found[0].Url);
        Assert.Equal("#FF0000", found[0].ColorHex);
        Assert.All(handler.Calls, c => Assert.Equal("Basic", c.Request.Headers.Authorization!.Scheme));
    }

    [Fact]
    public async Task Wrong_credentials_discover_nothing_rather_than_throwing()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text("unauthorized", System.Net.HttpStatusCode.Unauthorized));
        Assert.Empty(await new CalDavClient(handler.Client()).DiscoverCalendarsAsync("https://caldav.icloud.com", "u", "bad"));
    }

    [Fact]
    public async Task Query_sends_utc_time_range_and_returns_each_calendar_data_body()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(Multi(
            "<D:response><D:propstat><D:prop><C:calendar-data>BEGIN:VCALENDAR\nEND:VCALENDAR</C:calendar-data></D:prop></D:propstat></D:response>" +
            "<D:response><D:propstat><D:prop><C:calendar-data>  </C:calendar-data></D:prop></D:propstat></D:response>"), (System.Net.HttpStatusCode)207));

        var bodies = await new CalDavClient(handler.Client()).QueryEventsAsync("https://x/cal/", "u", "p",
            new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.FromHours(-4)), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Single(bodies);
        var call = Assert.Single(handler.Calls);
        Assert.Equal("REPORT", call.Request.Method.Method);
        Assert.Contains("start=\"20261001T080000Z\"", call.Body);
        Assert.Contains("end=\"20261101T000000Z\"", call.Body);
    }
}
