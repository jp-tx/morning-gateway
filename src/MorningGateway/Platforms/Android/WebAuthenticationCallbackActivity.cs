using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Maui.Authentication;
using MorningGateway.Services.Calendar;

namespace MorningGateway;

/// <summary>
/// Catches the redirect back from Google's OAuth consent screen
/// (scheme <see cref="GoogleOAuthConfig.CallbackScheme"/>, path /oauth2redirect) and hands it to
/// MAUI's WebAuthenticator, which completes the Google sign-in flow started
/// in GoogleCalendarProvider.
/// </summary>
[Activity(NoHistory = true, LaunchMode = Android.Content.PM.LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = GoogleOAuthConfig.CallbackScheme)]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
