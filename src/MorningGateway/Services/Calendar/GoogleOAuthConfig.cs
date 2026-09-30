namespace MorningGateway.Services.Calendar;

/// <summary>
/// The one place the Google redirect scheme lives. Android intent filters are
/// static, so <see cref="CallbackScheme"/> is baked into the manifest at build
/// time (via WebAuthenticationCallbackActivity); everything else - including
/// the redirect URI - is derived from the Client ID entered in Settings, and
/// sign-in refuses to start if the two disagree.
/// </summary>
public static class GoogleOAuthConfig
{
    const string ClientIdSuffix = ".apps.googleusercontent.com";
    const string SchemePrefix = "com.googleusercontent.apps.";

    /// <summary>
    /// Set to "com.googleusercontent.apps." + your Client ID without ".apps.googleusercontent.com"
    /// (Google Cloud Console shows it as the client's "URL scheme"), then rebuild.
    /// </summary>
    public const string CallbackScheme = "com.googleusercontent.apps.your-client-id-prefix";

    public static bool IsValidClientId(string? clientId) =>
        clientId is not null
        && clientId.EndsWith(ClientIdSuffix, StringComparison.OrdinalIgnoreCase)
        && clientId.Length > ClientIdSuffix.Length;

    public static string SchemeForClientId(string clientId) =>
        SchemePrefix + clientId[..^ClientIdSuffix.Length];

    public static string RedirectUriForClientId(string clientId) =>
        SchemeForClientId(clientId) + ":/oauth2redirect";
}
