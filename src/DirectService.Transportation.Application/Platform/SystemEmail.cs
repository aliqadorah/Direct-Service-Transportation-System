namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// System email (owner decision 22 Oct 2026, option A, closes DEF-02 for email; DT-250…DT-253). Every email goes into the outbox
/// (core.EmailOutbox); the outbox worker sends it through the transport of the active mode. A mode is one <see cref="IEmailTransport"/>:
/// another provider (Azure Communication Services, Amazon SES, …) is a new mode with its own transport, nothing else changes.
/// </summary>
public static class EmailModes
{
    public const string Development = "Development";
    public const string Microsoft365 = "Microsoft365";

    public static readonly IReadOnlyList<string> All = [Development, Microsoft365];
}

/// <summary>Why an email was not sent, as a kind the console words in plain language (never a secret or a link).</summary>
public static class EmailErrorKinds
{
    public const string WrongTenant = "wrongTenant";
    public const string WrongClient = "wrongClient";
    public const string WrongSecret = "wrongSecret";
    public const string SecretExpired = "secretExpired";
    public const string MissingPermission = "missingPermission";
    public const string MailboxNotAllowed = "mailboxNotAllowed";
    public const string MailboxNotFound = "mailboxNotFound";
    public const string RecipientRejected = "recipientRejected";
    public const string InvalidAddress = "invalidAddress";
    public const string Throttled = "throttled";
    public const string Unavailable = "unavailable";
    public const string Network = "network";
    public const string Folder = "folder";
    public const string Interrupted = "interrupted";
    public const string Suppressed = "suppressed";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All =
    [
        WrongTenant, WrongClient, WrongSecret, SecretExpired, MissingPermission, MailboxNotAllowed, MailboxNotFound, RecipientRejected, InvalidAddress,
        Throttled, Unavailable, Network, Folder, Interrupted, Suppressed, Unknown,
    ];

    /// <summary>The address itself is refused: trying again cannot help.</summary>
    public static bool IsFinal(string kind) => kind is RecipientRejected or InvalidAddress;
}

/// <summary>Outbox statuses.</summary>
public static class EmailStatus
{
    public const string Pending = "Pending";
    public const string Sending = "Sending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string Suppressed = "Suppressed";

    public static readonly IReadOnlyList<string> All = [Pending, Sending, Sent, Failed, Suppressed];
}

/// <summary>States of a settings row.</summary>
public static class EmailSettingStates
{
    public const string Active = "Active";
    public const string Pending = "Pending";
    public const string Retired = "Retired";
}

/// <summary>A message ready to go: headers and both bodies (already drawn by the layout).</summary>
public sealed record OutgoingEmail(string To, string Subject, string Html, string Text, string Template);

/// <summary>
/// How to send: the mode and its settings; the client secret is in clear text only in memory, for the call. <paramref name="FreshToken"/>
/// (DT-261): ask Microsoft sign-in for a new token instead of one kept from an earlier email — a test email must prove that the app and its
/// secret work now (a secret revoked in Entra would otherwise still "work" until the kept token expires).
/// </summary>
public sealed record EmailTransportSettings(string Mode, string SenderMailbox, string DisplayName, string? ReplyTo, string? EntraTenantId = null,
    string? EntraClientId = null, string? ClientSecret = null, bool FreshToken = false);

/// <summary>
/// The outcome of one attempt: sent, or the kind of error with its technical detail (no secrets), whether trying again later may help and how
/// long the service asked to wait (429 / 503 Retry-After). <paramref name="Warnings"/>: things to fix although it was sent (e.g. a tenant-wide grant).
/// </summary>
public sealed record EmailDeliveryResult(bool Sent, string? ErrorKind = null, string? Detail = null, TimeSpan? RetryAfter = null, IReadOnlyList<string>? Warnings = null)
{
    public static readonly EmailDeliveryResult Ok = new(true);

    public static EmailDeliveryResult Fail(string kind, string? detail, TimeSpan? retryAfter = null) => new(false, kind, Trim(detail), retryAfter);

    private static string? Trim(string? s) => s is null ? null : s.Length <= 380 ? s : s[..380] + "…";
}

/// <summary>One mode's way of delivering an email (development folder, Microsoft 365 through Microsoft Graph, …).</summary>
public interface IEmailTransport
{
    string Mode { get; }

    Task<EmailDeliveryResult> SendAsync(OutgoingEmail email, EmailTransportSettings settings, CancellationToken ct);
}

/// <summary>Encrypts the client secret and the queued messages (Data Protection, a purpose each); they never leave the server in clear text.</summary>
public interface IEmailProtector
{
    byte[] ProtectSecret(string secret);

    string UnprotectSecret(byte[] value);

    byte[] ProtectPayload(string payload);

    string UnprotectPayload(byte[] value);
}

/// <summary>Wakes the outbox worker when an email is queued (a signal, not a queue: the table is the queue).</summary>
public interface IEmailOutboxSignal
{
    void Set();
}

/// <summary>Settings of the "Email" section (appsettings / environment).</summary>
public sealed class SystemEmailOptions
{
    public const string Section = "Email";

    /// <summary>Development mode: the folder the .eml files are written to.</summary>
    public string DropFolder { get; set; } = ".local/mail";

    /// <summary>Development mode: the sender address of the files (Microsoft 365 uses the mailbox of the settings).</summary>
    public string From { get; set; } = "no-reply@directservice.local";

    /// <summary>At most this many emails a minute (Exchange Online allows 30 a minute per mailbox).</summary>
    public int PerMinute { get; set; } = 30;

    /// <summary>Attempts before an email is Failed (back-off 1 min, 5 min, 15 min, 1 h, 4 h, 12 h).</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>The same kind of email to the same address at most this many times an hour (e.g. a flood of password-reset requests).</summary>
    public int PerRecipientPerHour { get; set; } = 5;

    /// <summary>The recipient address in the delivery log is removed after this many days.</summary>
    public int LogRetentionDays { get; set; } = 180;

    /// <summary>Microsoft Graph and the Microsoft identity platform (other clouds or a test double).</summary>
    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0/";

    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com/";
}
