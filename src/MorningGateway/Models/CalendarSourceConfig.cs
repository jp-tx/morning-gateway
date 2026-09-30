namespace MorningGateway.Models;

public enum CalendarSourceType
{
    IcsUrl,
    Google,
    CalDav,
}

/// <summary>
/// Persisted description of one calendar to merge into the dashboard. Secrets
/// (CalDAV password, Google refresh token) are never stored here directly -
/// they live in platform SecureStorage under a key derived from <see cref="Id"/>.
/// </summary>
public class CalendarSourceConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string DisplayName { get; set; }
    public required CalendarSourceType Type { get; set; }
    public string ColorHex { get; set; } = "#4C8BF5";
    public bool Enabled { get; set; } = true;

    // IcsUrl source
    public string? IcsUrl { get; set; }

    // CalDav source
    public string? CalDavServerUrl { get; set; }
    public string? CalDavUsername { get; set; }

    // Google source
    public string? GoogleAccountEmail { get; set; }
    public string? GoogleCalendarId { get; set; } = "primary";

    public string SecureStorageKey => $"calsource_secret_{Id}";

    public string TypeLabel => Type switch
    {
        CalendarSourceType.IcsUrl => "ICS / webcal",
        CalendarSourceType.Google => "Google Calendar",
        CalendarSourceType.CalDav => "CalDAV",
        _ => Type.ToString(),
    };
}
