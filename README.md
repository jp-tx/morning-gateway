# Morning Gateway

A wall-mounted Android dashboard built with .NET MAUI: an always-on calendar
(month + day views, from Google, Apple/iCloud, or any ICS feed) with a
weather forecast sidebar, dark/light theming, and OLED/LCD burn-in
protection.

## Features

- **Keep screen on** - toggle in Settings; the tablet never sleeps while the app is foregrounded.
- **Burn-in protection** - the whole dashboard slowly orbits a few pixels on a timer, plus a near-invisible luminance pulse, so no pixel sits lit at one exact spot/brightness for hours.
- **Calendars** - mix and match three source types, all read-only:
  - Plain ICS/webcal URL (Google's "secret address in iCal format", iCloud public calendar links, any `.ics` feed).
  - Google Calendar via OAuth (Calendar REST API v3).
  - CalDAV (iCloud with an app-specific password, Fastmail, Nextcloud, etc.).
- **Month and day views**, tap a day in month view to jump to its day view.
- **Weather sidebar** on the right - current conditions, next 12 hours, 7-day forecast, via [Open-Meteo](https://open-meteo.com) (free, no API key).
- **Dark / light / system theme.**
- **Boots into the app** after a power cut (Android `BOOT_COMPLETED` receiver).

## First launch: demo calendar

A fresh install seeds one demo calendar source automatically (a small public
ICS feed with daily recurring events - stand-up, lunch, evening wrap-up, gym,
trash day) so the month and day views are populated instead of empty on first
run. This is seeded exactly once; remove it in Settings whenever you like
(tap **Remove** next to "Demo Calendar") and it won't come back. Add your own
real calendars the same way you would otherwise.

## Project layout

```
src/MorningGateway/
  Platforms/Android/     MainActivity, manifest, boot receiver, OAuth callback activity
  Models/                 Plain data types (CalendarEvent, WeatherSnapshot, ...)
  Services/Calendar/      ICS parsing, CalDAV client, Google OAuth, aggregator
  Services/Weather/       Open-Meteo client, geocoding, device location
  Services/Display/       Keep-awake, burn-in protection, theme, settings storage
  ViewModels/              DashboardViewModel, SettingsViewModel (CommunityToolkit.Mvvm)
  Views/                  DashboardPage, MonthCalendarView, DayCalendarView, WeatherSidebarView, SettingsPage
```

## Building

Requires the .NET 10 SDK and the MAUI Android workload:

```bash
dotnet workload install maui-android
cd src/MorningGateway
dotnet build -f net10.0-android
```

To deploy to a connected/ADB-visible tablet:

```bash
dotnet build -t:Run -f net10.0-android
```

Or build a release APK to sideload:

```bash
dotnet publish -f net10.0-android -c Release
```

## Tests

```bash
dotnet test tests/MorningGateway.Tests
TZ=America/New_York dotnet test tests/MorningGateway.Tests   # also worth running: catches time-zone bugs
```

The test project is plain `net10.0`: it links the MAUI-free source files directly and stands in
for `Preferences`, `SecureStorage` and `WebAuthenticator` with in-memory shims
(`tests/MorningGateway.Tests/Shims`). It doesn't cover the views or view models.

`scripts/setup-android-sdk.sh` is an interactive helper that installs a user-local Android SDK
and runs a Debug build.

## First run

Open the gear icon (top-right) to reach Settings, then:

1. **Add a calendar** - pick whichever tab matches your account (ICS, Google, or CalDAV) and follow the on-screen fields.
2. **Set your weather location** - search by city name, or tap "Use device location" once and then search to give it a friendly name.
3. **Pick a theme**, confirm "Keep screen on" is enabled, and leave burn-in protection on for an always-mounted display.

### Adding a Google Calendar

Google's OAuth requires an app registration:

1. In [Google Cloud Console](https://console.cloud.google.com/), create a project (or reuse one) and enable the **Google Calendar API**.
2. Under **APIs & Services -> Credentials**, create an **OAuth client ID**. Choose client type **iOS** - yes, on an Android app. This is intentional: it's the client type Google lets use a custom URL-scheme redirect with PKCE and no client secret, which is exactly what a native mobile app needs and what `WebAuthenticationCallbackActivity` is wired for. Enter `com.morninggateway.dashboard` as the Bundle ID field (it isn't actually used for iOS on this app, it just needs to be non-empty).
3. Google will show a Client ID like `1234567890-abc.apps.googleusercontent.com` and a matching **URL scheme** like `com.googleusercontent.apps.1234567890-abc`.
4. Open `Services/Calendar/GoogleOAuthConfig.cs` and set `CallbackScheme` to that URL scheme, then rebuild. Android intent filters are static, so this one constant has to be set at compile time; it's the only place the scheme lives. Sign-in refuses to start (with the exact fix in the message) if your Client ID doesn't match the built scheme.
5. In the app's Settings screen, paste the **Client ID**. The redirect URI (`<scheme>:/oauth2redirect`) is derived automatically.
6. Tap **Connect Google account** in Settings and sign in.
7. **Publish the OAuth consent screen** ("In production" under Google Auth Platform -> Audience). While it's in "Testing", Google expires refresh tokens after 7 days, which will silently break an always-on display. If a token does expire or is revoked, the dashboard shows "<account>: reconnect in Settings" in the top bar. Google will show an "unverified app" warning at sign-in for the calendar scope; that's expected for a personal app.

### Adding an iCloud calendar via CalDAV

1. Generate an app-specific password at [appleid.apple.com](https://appleid.apple.com) -> Sign-In and Security -> App-Specific Passwords.
2. In Settings -> "Add a CalDAV calendar": Server URL `https://caldav.icloud.com`, Username = your full Apple ID email, Password = the app-specific password.
3. Tap **Find calendars** and pick one from the discovered list.

### Adding a Google Calendar the simple way (no OAuth)

If OAuth setup is more than you want, add it as an **ICS calendar** instead: Google Calendar settings -> your calendar -> "Integrate calendar" -> **Secret address in iCal format**. Paste that URL into the ICS tab. Read-only, no Google Cloud project needed - the trade-off is it's a single fixed calendar rather than an account-wide connection, and Google refreshes that feed every few hours rather than in real time.

## Notes on the burn-in protection approach

Every ~45 seconds (configurable) the whole page translates a few pixels
around a slow 12-step orbit, and a near-invisible black overlay pulses
between roughly 0.5-3.5% opacity. Neither is meant to be consciously
noticeable - the goal is that no single pixel is ever lit at exactly the same
position and brightness for hours at a stretch, which is what causes OLED/LCD
burn-in on always-on displays. Turn it off in Settings if you'd rather have a
perfectly static layout (e.g. on a panel with no burn-in risk).
