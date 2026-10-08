namespace DirectService.Transportation.Web;

/// <summary>Rate-limiting policy names (SEC-14, API-07).</summary>
public static class RateLimits
{
    /// <summary>Sign-in, sign-up, password reset and other anonymous account endpoints, per IP.</summary>
    public const string Auth = "auth";
}
