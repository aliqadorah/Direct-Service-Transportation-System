using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// Outgoing email (INT-03). Every flow calls this; the application's implementation puts the email into the outbox (core.EmailOutbox, DT-251)
/// and the outbox worker sends it with the active mode (development folder or Microsoft 365, DT-250).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>
/// An email: recipient, subject and both bodies (plain text and HTML), its template name and the recipient's language. <paramref name="Content"/>
/// is what the shared layout was drawn from, so the sender can draw it again with the active sender name and reply-to address;
/// <paramref name="HasSecretLink"/> marks a single-use link (never kept after a failure).
/// </summary>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody, string Template, string Language = "ar",
    Notifications.EmailContent? Content = null, bool HasSecretLink = false);

/// <summary>A monthly site rate read under an update lock.</summary>
public sealed record LockedMonthlyRate(long LoadingSiteId, string RateMonth, decimal? OwnFleetRate, decimal? ExternalFleetRate);

/// <summary>
/// Monthly site rates read WITH (UPDLOCK) inside the trip transaction, so clearing rates and saving trips serialise (FR-035,
/// B4 review M2). <paramref name="loadingSiteId"/> null = every site of the months.
/// </summary>
public interface IMonthlyRateLock
{
    Task<IReadOnlyList<LockedMonthlyRate>> ReadAsync(IReadOnlyCollection<string> months, long? loadingSiteId, CancellationToken ct);
}

/// <summary>Refuses activity dated in a closed period (BR-036, FR-133).</summary>
public interface IPeriodGuard
{
    Task EnsureOpenAsync(DateOnly date, CancellationToken ct);
}

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
