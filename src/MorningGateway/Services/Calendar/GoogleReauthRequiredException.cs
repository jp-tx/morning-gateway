namespace MorningGateway.Services.Calendar;

/// <summary>Google rejected the stored refresh token (revoked, expired, or password changed); the user must sign in again.</summary>
public class GoogleReauthRequiredException : Exception
{
    public GoogleReauthRequiredException()
        : base("Google access expired or was revoked - reconnect the account in Settings.")
    {
    }
}
