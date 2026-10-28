using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>Hashed, entity-scoped opaque identifiers for URLs (SEC-22, ADR-07).</summary>
public interface IUrlTokenService
{
    /// <summary>22-character token for a record's PublicId, scoped to the entity type.</summary>
    string Encode<TEntity>(Guid publicId);

    /// <summary>The PublicId of a token, or null when the token is malformed or not of this entity type.</summary>
    Guid? Decode<TEntity>(string? token);

    /// <summary>Non-generic forms for code that knows the entity type only at run time.</summary>
    string Encode(Type entityType, Guid publicId);

    Guid? Decode(Type entityType, string? token);
}

/// <summary>Resolves a namespaced entity type (e.g. "TRN.Trip") and a URL token to the internal id, within the current scope.</summary>
public interface IEntityResolver
{
    /// <summary>The CLR type of a namespaced entity name, or null when unknown.</summary>
    Type? TypeOf(string entityType);

    /// <summary>Internal id of the record (respecting tenant/company/soft-delete filters), or null.</summary>
    Task<long?> ResolveIdAsync(string entityType, string token, CancellationToken ct);
}

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

/// <summary>Gap-free numbering inside the caller's transaction (BR-037, BR-038).</summary>
public interface INumberingService
{
    /// <summary>
    /// Takes the next number of a document type for the current company, holding an update lock on
    /// the sequence row until the caller's transaction ends. Throws when no transaction is active.
    /// </summary>
    Task<string> NextAsync(string documentType, CancellationToken ct);

    /// <summary>
    /// Takes the same update lock without issuing a number, so an operation (e.g. closing a period) waits for the
    /// documents being issued and blocks new ones until its transaction ends.
    /// </summary>
    Task LockAsync(string documentType, CancellationToken ct);
}

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

/// <summary>Record locks (BR-039, FR-186).</summary>
public interface IRecordLockGuard
{
    /// <summary>Throws 423 when another user holds an active lock on the record.</summary>
    Task EnsureNotLockedByOthersAsync(string entityType, long entityId, CancellationToken ct);
}

/// <summary>Pushes changes to connected users of the same company (RT-01).</summary>
/// <summary>Actions run once the current database transaction commits (none on rollback); at once when no transaction is open.</summary>
public interface IAfterCommit
{
    void Add(Func<Task> action);
}

public interface IRealtimeNotifier
{
    Task RecordChangedAsync(long companyId, string entityType, string? token, string action, CancellationToken ct);

    /// <summary>A new in-app notification for one user (FR-190): pushed to that user's connections.</summary>
    Task NotifyUserAsync(Guid userPublicId, object notification, CancellationToken ct);
    Task LockChangedAsync(long companyId, string entityType, string token, string? holderName, CancellationToken ct);

    /// <summary>A support window of this staff member ended (subscriber or expiry, DT-220): pushed to the staff member's open pages.</summary>
    Task SupportEndedAsync(Guid staffPublicId, object payload, CancellationToken ct);
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
