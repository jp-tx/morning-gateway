using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MorningGateway.Models;
using MorningGateway.Services.Display;

namespace MorningGateway.Services.Calendar;

/// <summary>
/// Google Calendar via OAuth 2.0 + PKCE using MAUI's WebAuthenticator (opens
/// the system browser for consent, comes back through
/// WebAuthenticationCallbackActivity). Talks to the Calendar REST API v3
/// directly over HttpClient rather than pulling in the full Google.Apis SDK,
/// which is heavier than a mobile app needs.
///
/// Setup (see README "Adding a Google Calendar"): register an OAuth client in
/// Google Cloud Console of type "iOS" (yes, on an Android app - it's the
/// client type Google lets use a custom URL-scheme redirect with no client
/// secret), put its Client ID into Settings, and set
/// <see cref="GoogleOAuthConfig.CallbackScheme"/> to match. The redirect URI is derived.
/// </summary>
public class GoogleCalendarProvider : ICalendarProvider
{
    const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    // "email" is only used to label the connected account (userinfo endpoint).
    const string Scope = "https://www.googleapis.com/auth/calendar.readonly email";

    readonly HttpClient _http;
    readonly SettingsStore _settings;

    public GoogleCalendarProvider(HttpClient http, SettingsStore settings)
    {
        _http = http;
        _settings = settings;
    }

    public CalendarSourceType Type => CalendarSourceType.Google;

    /// <summary>Runs the interactive consent flow and stores the resulting refresh token.</summary>
    public async Task<string> SignInAsync(CalendarSourceConfig source, CancellationToken cancellationToken = default)
    {
        var clientId = _settings.GoogleOAuthClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Set the Google OAuth Client ID in Settings first.");
        }

        if (!GoogleOAuthConfig.IsValidClientId(clientId))
        {
            throw new InvalidOperationException("That doesn't look like a Google OAuth Client ID (it should end in .apps.googleusercontent.com).");
        }

        var scheme = GoogleOAuthConfig.SchemeForClientId(clientId);
        if (!string.Equals(scheme, GoogleOAuthConfig.CallbackScheme, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This build only listens for the redirect scheme \"{GoogleOAuthConfig.CallbackScheme}\", but your Client ID needs \"{scheme}\". "
                + "Set GoogleOAuthConfig.CallbackScheme to that value and rebuild.");
        }

        var redirectUri = GoogleOAuthConfig.RedirectUriForClientId(clientId);

        var (verifier, challenge) = GeneratePkcePair();

        var authUri = new Uri($"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scope)}"
            + "&access_type=offline&prompt=consent"
            + $"&code_challenge={challenge}&code_challenge_method=S256");

        var result = await WebAuthenticator.Default.AuthenticateAsync(authUri, new Uri(redirectUri)).ConfigureAwait(false);
        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Google sign-in did not return an authorization code.");
        }

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        };

        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Empty token response from Google.");

        if (string.IsNullOrWhiteSpace(tokens.RefreshToken))
        {
            throw new InvalidOperationException("Google did not return a refresh token. Revoke prior access at https://myaccount.google.com/permissions and try again.");
        }

        await SecureStorage.Default.SetAsync(source.SecureStorageKey, tokens.RefreshToken).ConfigureAwait(false);

        var email = await FetchAccountEmailAsync(tokens.AccessToken, cancellationToken).ConfigureAwait(false);
        return email;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(CalendarSourceConfig source, DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default)
    {
        var refreshToken = await SecureStorage.Default.GetAsync(source.SecureStorageKey).ConfigureAwait(false);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return Array.Empty<CalendarEvent>();
        }

        var accessToken = await RefreshAccessTokenAsync(refreshToken, cancellationToken).ConfigureAwait(false);
        var calendarId = Uri.EscapeDataString(string.IsNullOrWhiteSpace(source.GoogleCalendarId) ? "primary" : source.GoogleCalendarId);

        var baseUrl = $"https://www.googleapis.com/calendar/v3/calendars/{calendarId}/events"
            + $"?timeMin={Uri.EscapeDataString(rangeStart.ToString("o", CultureInfo.InvariantCulture))}"
            + $"&timeMax={Uri.EscapeDataString(rangeEnd.ToString("o", CultureInfo.InvariantCulture))}"
            + "&singleEvents=true&orderBy=startTime&maxResults=250";

        var results = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var url = pageToken is null ? baseUrl : $"{baseUrl}&pageToken={Uri.EscapeDataString(pageToken)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<EventsListResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var item in body?.Items ?? Enumerable.Empty<GoogleEventDto>())
            {
                try
                {
                    results.Add(ToCalendarEvent(item, source, rangeStart));
                }
                catch (FormatException)
                {
                    // One event with an unparseable date is skipped rather than failing the whole calendar.
                }
            }

            pageToken = body?.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    static CalendarEvent ToCalendarEvent(GoogleEventDto item, CalendarSourceConfig source, DateTimeOffset rangeStart)
    {
        var isAllDay = item.Start?.Date is not null;
        DateTimeOffset start, end;
        if (isAllDay)
        {
            // All-day dates are calendar days, not instants: anchor them at local midnight so they
            // stay on the right day in every time zone, and parse culture-independently.
            var startDate = DateOnly.ParseExact(item.Start!.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var endDate = DateOnly.ParseExact(item.End?.Date ?? item.Start.Date!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            start = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue));
            end = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue));
        }
        else
        {
            start = DateTimeOffset.Parse(item.Start?.DateTime ?? rangeStart.ToString("o", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            end = DateTimeOffset.Parse(item.End?.DateTime ?? start.AddHours(1).ToString("o", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        }

        return new CalendarEvent
        {
            Id = $"{source.Id}:{item.Id}",
            SourceId = source.Id,
            Title = string.IsNullOrWhiteSpace(item.Summary) ? "(untitled)" : item.Summary!,
            Location = item.Location,
            Description = item.Description,
            Start = start,
            End = end,
            IsAllDay = isAllDay,
            ColorHex = source.ColorHex,
        };
    }

    async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var clientId = _settings.GoogleOAuthClientId
            ?? throw new InvalidOperationException("Missing Google OAuth Client ID in Settings.");

        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };

        using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (error.Contains("invalid_grant", StringComparison.Ordinal))
            {
                throw new GoogleReauthRequiredException();
            }

            response.EnsureSuccessStatusCode();
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false);
        return tokens?.AccessToken ?? throw new InvalidOperationException("Google did not return an access token.");
    }

    async Task<string> FetchAccountEmailAsync(string accessToken, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return "Google account";
            }

            var info = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken).ConfigureAwait(false);
            return info.TryGetProperty("email", out var email) ? email.GetString() ?? "Google account" : "Google account";
        }
        catch (Exception)
        {
            return "Google account";
        }
    }

    static (string Verifier, string Challenge) GeneratePkcePair()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64UrlEncode(bytes);
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(challengeBytes);
        return (verifier, challenge);
    }

    static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    class EventsListResponse
    {
        [JsonPropertyName("items")] public List<GoogleEventDto>? Items { get; set; }
        [JsonPropertyName("nextPageToken")] public string? NextPageToken { get; set; }
    }

    class GoogleEventDto
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("start")] public GoogleEventDateTimeDto? Start { get; set; }
        [JsonPropertyName("end")] public GoogleEventDateTimeDto? End { get; set; }
    }

    class GoogleEventDateTimeDto
    {
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("dateTime")] public string? DateTime { get; set; }
    }
}
