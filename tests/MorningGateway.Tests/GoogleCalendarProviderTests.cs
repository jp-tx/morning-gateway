using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using MorningGateway.Models;
using MorningGateway.Services.Calendar;
using MorningGateway.Services.Display;

namespace MorningGateway.Tests;

// README "Adding a Google Calendar": OAuth 2.0 + PKCE, iOS-type client (no secret),
// custom-scheme redirect, refresh token kept in SecureStorage, Calendar REST API v3.
[Collection("MauiStatics")]
public class GoogleCalendarProviderTests : MauiStaticsTestBase
{
    // Derived from the compiled-in scheme so the "client id matches this build" check passes.
    static readonly string ClientId = GoogleOAuthConfig.CallbackScheme["com.googleusercontent.apps.".Length..] + ".apps.googleusercontent.com";
    static readonly string Redirect = GoogleOAuthConfig.CallbackScheme + ":/oauth2redirect";
    static readonly DateTimeOffset RangeStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset RangeEnd = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    readonly SettingsStore _settings = new();
    readonly CalendarSourceConfig _source = new() { DisplayName = "G", Type = CalendarSourceType.Google };

    // Set here, not in a field initializer: those run before the base ctor clears the shared statics.
    public GoogleCalendarProviderTests()
    {
        _settings.GoogleOAuthClientId = ClientId;
    }

    static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Plays Google: records the consent URL, hands back a code, answers token/userinfo calls.</summary>
    (GoogleCalendarProvider Provider, StubHandler Http, Func<Uri?> AuthUrl) SignInHarness(string tokenJson, string? userinfoJson = "{\"email\":\"me@example.com\"}")
    {
        Uri? authUrl = null;
        WebAuthenticator.Default.Handler = (url, _) =>
        {
            authUrl = url;
            var r = new WebAuthenticatorResult();
            r.Properties["code"] = "auth-code-1";
            return r;
        };
        var http = new StubHandler((req, _) => req.RequestUri!.Host switch
        {
            "oauth2.googleapis.com" => StubHandler.Json(tokenJson),
            _ => userinfoJson is null ? StubHandler.Text("nope", HttpStatusCode.Unauthorized) : StubHandler.Json(userinfoJson),
        });
        return (new GoogleCalendarProvider(http.Client(), _settings), http, () => authUrl);
    }

    [Fact]
    public async Task SignIn_without_client_id_or_redirect_explains_what_to_set()
    {
        _settings.GoogleOAuthClientId = null;
        var provider = new GoogleCalendarProvider(new StubHandler((_, _) => StubHandler.Text("")).Client(), _settings);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignInAsync(_source));
        Assert.Contains("Client ID", ex.Message);
    }

    [Fact]
    public async Task Client_id_for_a_different_scheme_than_this_build_is_rejected_with_the_fix()
    {
        _settings.GoogleOAuthClientId = "999-other.apps.googleusercontent.com";
        var provider = new GoogleCalendarProvider(new StubHandler((_, _) => StubHandler.Text("")).Client(), _settings);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignInAsync(_source));
        Assert.Contains("CallbackScheme", ex.Message);
        Assert.Contains("com.googleusercontent.apps.999-other", ex.Message);
    }

    [Fact]
    public async Task Something_that_is_not_a_google_client_id_is_rejected()
    {
        _settings.GoogleOAuthClientId = "not-a-client-id";
        var provider = new GoogleCalendarProvider(new StubHandler((_, _) => StubHandler.Text("")).Client(), _settings);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignInAsync(_source));
    }

    [Fact]
    public void Redirect_uri_is_derived_from_the_client_id()
    {
        Assert.Equal("com.googleusercontent.apps.123-abc:/oauth2redirect",
            GoogleOAuthConfig.RedirectUriForClientId("123-abc.apps.googleusercontent.com"));
    }

    [Fact]
    public async Task Consent_url_uses_pkce_s256_offline_access_and_the_configured_client()
    {
        var (provider, _, authUrl) = SignInHarness("{\"access_token\":\"at\",\"refresh_token\":\"rt\"}");
        await provider.SignInAsync(_source);

        var q = HttpUtility.ParseQueryString(authUrl()!.Query);
        Assert.Equal("accounts.google.com", authUrl()!.Host);
        Assert.Equal(ClientId, q["client_id"]);
        Assert.Equal(Redirect, q["redirect_uri"]);
        Assert.Equal("code", q["response_type"]);
        Assert.Equal("S256", q["code_challenge_method"]);
        Assert.Equal("offline", q["access_type"]);
        Assert.Contains("calendar.readonly", q["scope"]);
    }

    [Fact]
    public async Task Token_exchange_sends_the_verifier_matching_the_challenge_and_no_client_secret()
    {
        var (provider, http, authUrl) = SignInHarness("{\"access_token\":\"at\",\"refresh_token\":\"rt\"}");
        await provider.SignInAsync(_source);

        var challenge = HttpUtility.ParseQueryString(authUrl()!.Query)["code_challenge"];
        var form = HttpUtility.ParseQueryString(http.Calls.First(c => c.Request.RequestUri!.Host == "oauth2.googleapis.com").Body);

        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("auth-code-1", form["code"]);
        Assert.Equal(Redirect, form["redirect_uri"]);
        Assert.Null(form["client_secret"]);
        Assert.Equal(challenge, B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]!))));
    }

    [Fact]
    public async Task Refresh_token_is_stored_in_secure_storage_and_email_returned()
    {
        var (provider, _, _) = SignInHarness("{\"access_token\":\"at\",\"refresh_token\":\"rt\"}");
        var email = await provider.SignInAsync(_source);

        Assert.Equal("me@example.com", email);
        Assert.Equal("rt", await SecureStorage.Default.GetAsync(_source.SecureStorageKey));
    }

    [Fact]
    public async Task Missing_refresh_token_tells_the_user_to_revoke_and_retry()
    {
        var (provider, _, _) = SignInHarness("{\"access_token\":\"at\"}");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignInAsync(_source));
        Assert.Contains("myaccount.google.com/permissions", ex.Message);
        Assert.Null(await SecureStorage.Default.GetAsync(_source.SecureStorageKey));
    }

    [Fact]
    public async Task Redirect_without_a_code_fails_cleanly()
    {
        WebAuthenticator.Default.Handler = (_, _) => new WebAuthenticatorResult();
        var provider = new GoogleCalendarProvider(new StubHandler((_, _) => StubHandler.Text("")).Client(), _settings);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.SignInAsync(_source));
    }

    // Bug: scope is calendar.readonly only, but the userinfo endpoint needs openid/email/profile.
    // Real Google answers 401 and the account is shown as the generic "Google account".
    [Fact]
    public async Task Consent_scope_includes_email_so_the_account_can_be_named()
    {
        var (provider, _, authUrl) = SignInHarness("{\"access_token\":\"at\",\"refresh_token\":\"rt\"}");
        await provider.SignInAsync(_source);
        var scopes = HttpUtility.ParseQueryString(authUrl()!.Query)["scope"]!.Split(' ');
        Assert.Contains(scopes, s => s is "email" or "openid" or "https://www.googleapis.com/auth/userinfo.email");
    }

    // ---- GetEventsAsync ----

    StubHandler EventsHandler(params string[] pages)
    {
        var i = 0;
        return new StubHandler((req, _) => req.RequestUri!.Host switch
        {
            "oauth2.googleapis.com" => StubHandler.Json("{\"access_token\":\"fresh-at\"}"),
            _ => StubHandler.Json(pages[Math.Min(i++, pages.Length - 1)]),
        });
    }

    Task Connected() => SecureStorage.Default.SetAsync(_source.SecureStorageKey, "rt");

    [Fact]
    public async Task Not_signed_in_returns_no_events_and_makes_no_requests()
    {
        var http = EventsHandler("{}");
        var events = await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd);
        Assert.Empty(events);
        Assert.Empty(http.Calls);
    }

    [Fact]
    public async Task Access_token_is_refreshed_and_sent_as_bearer_with_the_requested_window()
    {
        await Connected();
        var http = EventsHandler("{\"items\":[]}");
        await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd);

        var refresh = http.Calls[0];
        var form = HttpUtility.ParseQueryString(refresh.Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("rt", form["refresh_token"]);

        var list = http.Calls[1].Request;
        Assert.Equal("Bearer", list.Headers.Authorization!.Scheme);
        Assert.Equal("fresh-at", list.Headers.Authorization.Parameter);
        Assert.Contains("/calendars/primary/events", list.RequestUri!.AbsolutePath);
        var q = HttpUtility.ParseQueryString(list.RequestUri.Query);
        Assert.Equal("true", q["singleEvents"]);
        Assert.Equal(RangeStart, DateTimeOffset.Parse(q["timeMin"]!, CultureInfo.InvariantCulture));
        Assert.Equal(RangeEnd, DateTimeOffset.Parse(q["timeMax"]!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Timed_all_day_and_untitled_events_map_to_calendar_events()
    {
        await Connected();
        var http = EventsHandler("""
        {"items":[
          {"id":"e1","summary":"Lunch","location":"Cafe","start":{"dateTime":"2026-10-02T12:00:00-04:00"},"end":{"dateTime":"2026-10-02T13:00:00-04:00"}},
          {"id":"e2","summary":"Holiday","start":{"date":"2026-10-05"},"end":{"date":"2026-10-06"}},
          {"id":"e3","start":{"dateTime":"2026-10-03T09:00:00Z"},"end":{"dateTime":"2026-10-03T09:30:00Z"}}
        ]}
        """);
        var events = await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd);

        Assert.Equal(3, events.Count);
        Assert.Equal($"{_source.Id}:e1", events[0].Id);
        Assert.Equal("Lunch", events[0].Title);
        Assert.Equal("Cafe", events[0].Location);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 16, 0, 0, TimeSpan.Zero), events[0].Start.ToUniversalTime());
        Assert.True(events[1].IsAllDay);
        Assert.Equal("(untitled)", events[2].Title);
    }

    // Bug: all-day events are built at midnight UTC, so in any zone west of UTC
    // (default weather location is New York) they show on the previous local day.
    // Run with TZ=America/New_York to see it.
    [Fact]
    public async Task All_day_event_appears_on_its_own_date_in_local_time()
    {
        await Connected();
        var http = EventsHandler("""{"items":[{"id":"h","summary":"Holiday","start":{"date":"2026-10-05"},"end":{"date":"2026-10-06"}}]}""");
        var e = Assert.Single(await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd));

        Assert.True(e.OccursOn(new DateOnly(2026, 10, 5)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 4)));
        Assert.False(e.OccursOn(new DateOnly(2026, 10, 6)));
    }

    // Bug: date parsing uses the current culture, so e.g. a Thai-locale tablet reads 2026 as a Buddhist-era year.
    [Fact]
    public async Task All_day_dates_parse_the_same_under_any_device_locale()
    {
        await Connected();
        var http = EventsHandler("""{"items":[{"id":"h","summary":"Holiday","start":{"date":"2026-10-05"},"end":{"date":"2026-10-06"}}]}""");
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var e = Assert.Single(await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd));
            Assert.Equal(2026, e.Start.Year);
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    // Bug: maxResults=250 and nextPageToken is ignored, so a busy month silently truncates.
    [Fact]
    public async Task Follows_next_page_token_until_all_events_are_fetched()
    {
        await Connected();
        var http = EventsHandler(
            """{"nextPageToken":"p2","items":[{"id":"a","summary":"A","start":{"dateTime":"2026-10-02T10:00:00Z"},"end":{"dateTime":"2026-10-02T11:00:00Z"}}]}""",
            """{"items":[{"id":"b","summary":"B","start":{"dateTime":"2026-10-03T10:00:00Z"},"end":{"dateTime":"2026-10-03T11:00:00Z"}}]}""");
        var events = await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd);
        Assert.Equal(new[] { "A", "B" }, events.Select(e => e.Title));
    }

    [Fact]
    public async Task Revoked_refresh_token_raises_reauth_required()
    {
        await Connected();
        var http = new StubHandler((_, _) => StubHandler.Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest));
        await Assert.ThrowsAsync<GoogleReauthRequiredException>(() =>
            new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd));
    }

    [Fact]
    public async Task Other_token_errors_stay_ordinary_http_failures()
    {
        await Connected();
        var http = new StubHandler((_, _) => StubHandler.Text("boom", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd));
    }

    [Fact]
    public async Task Custom_calendar_id_is_used_and_url_escaped()
    {
        await Connected();
        _source.GoogleCalendarId = "team@group.calendar.google.com";
        var http = EventsHandler("{\"items\":[]}");
        await new GoogleCalendarProvider(http.Client(), _settings).GetEventsAsync(_source, RangeStart, RangeEnd);
        Assert.Contains("/calendars/team%40group.calendar.google.com/events", http.Calls[1].Request.RequestUri!.AbsoluteUri);
    }
}
