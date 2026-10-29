namespace DirectService.Platform.Application;

/// <summary>Application settings bound from configuration.</summary>
public sealed class AppOptions
{
    public const string Section = "App";

    /// <summary>Public base URL used in email links, e.g. https://app.example.com.</summary>
    public string PublicBaseUrl { get; set; } = "https://localhost:5001";

    /// <summary>Absolute session lifetime in hours (Q-04: 12).</summary>
    public int AbsoluteSessionHours { get; set; } = 12;

    /// <summary>Idle timeout for provider staff sessions (they have no tenant settings).</summary>
    public int PlatformStaffIdleMinutes { get; set; } = 30;

    /// <summary>Version of the Terms of Service / Privacy Policy accepted at sign-up (G-20).</summary>
    public string TermsVersion { get; set; } = "draft-1";

    /// <summary>Trial length in days (ASM-03).</summary>
    public int TrialDays { get; set; } = 14;

    /// <summary>Invitation link validity in days (ASM-08).</summary>
    public int InvitationDays { get; set; } = 7;
}
