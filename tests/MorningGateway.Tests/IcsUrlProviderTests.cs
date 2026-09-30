using MorningGateway.Models;
using MorningGateway.Services.Calendar;

namespace MorningGateway.Tests;

public class IcsUrlProviderTests
{
    const string Feed = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//t//EN\r\nBEGIN:VEVENT\r\nUID:1\r\nDTSTART:20261002T100000Z\r\nDTEND:20261002T110000Z\r\nSUMMARY:Hi\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    static readonly DateTimeOffset S = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset E = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);

    static CalendarSourceConfig Source(string? url) => new() { DisplayName = "t", Type = CalendarSourceType.IcsUrl, IcsUrl = url };

    [Fact]
    public async Task Webcal_scheme_is_fetched_over_https()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(Feed));
        var events = await new IcsUrlCalendarProvider(handler.Client()).GetEventsAsync(Source("webcal://example.com/cal.ics"), S, E);

        Assert.Single(events);
        Assert.Equal("https://example.com/cal.ics", handler.Calls.Single().Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task Blank_url_returns_nothing_without_a_request()
    {
        var handler = new StubHandler((_, _) => StubHandler.Text(Feed));
        Assert.Empty(await new IcsUrlCalendarProvider(handler.Client()).GetEventsAsync(Source(" "), S, E));
        Assert.Empty(handler.Calls);
    }
}
