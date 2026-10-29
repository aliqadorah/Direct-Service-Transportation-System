namespace DirectService.Platform.Domain;

/// <summary>
/// core.EmailOutbox — Platform-wide delivery queue and log of every email the platform sends (revision 6, system email, DT-251). A row is
/// written when a flow asks for an email; the outbox worker sends it (retry with back-off, throttled). The message itself (subject, body,
/// links) is kept only encrypted and only while it may still be sent: it is wiped once sent, and once failed when it holds a single-use link.
/// The log keeps recipient, type, language, times, status and the reason of a failure — never a link token. Not written to the audit log
/// (a technical log of its own); changes of the settings and resends by staff are.
/// </summary>
[StoredName("CORE.EmailOutbox")]
[TokenLabel("EmailOutbox")]
public class EmailOutbox : INotAudited
{
    public long Id { get; set; }
    /// <summary>Random public identifier used by the console (resend).</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();
    /// <summary>The tenant of the user whose action caused the email, when known (platform-only log; never shown to tenants).</summary>
    public long? TenantId { get; set; }
    /// <summary>Template name (VerifyEmail, PasswordReset, Invitation, …).</summary>
    public string Template { get; set; } = string.Empty;
    /// <summary>The recipient's language (ar / en).</summary>
    public string LanguageCode { get; set; } = "ar";
    /// <summary>Recipient address.</summary>
    public string Recipient { get; set; } = string.Empty;
    /// <summary>Pending / Sending / Sent / Failed / Suppressed.</summary>
    public string Status { get; set; } = "Pending";
    /// <summary>Send attempts so far.</summary>
    public int Attempts { get; set; }
    /// <summary>When the next attempt is due (Pending).</summary>
    public DateTime NextAttemptAt { get; set; }
    /// <summary>A worker is sending the row until then (Sending); past it the send is treated as interrupted, never repeated on its own.</summary>
    public DateTime? ClaimedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>When it was last tried (the console's working / failing status is the outcome of the latest attempt).</summary>
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? FailedAt { get; set; }
    /// <summary>Kind of the last error (wrongSecret, missingPermission, throttled, …) and its reason in plain words; never a secret or a link.</summary>
    public string? ErrorKind { get; set; }
    public string? LastError { get; set; }
    /// <summary>The mode the email was (last) sent with.</summary>
    public string? Mode { get; set; }
    /// <summary>The email holds a single-use link (verification, password reset, invitation, email change): never kept after a failure.</summary>
    public bool HasSecretLink { get; set; }
    /// <summary>The message (recipient, subject, text, HTML) encrypted with Data Protection; NULL once sent or no longer needed.</summary>
    public byte[]? PayloadEncrypted { get; set; }
    /// <summary>The failed row this one resends, and when a failed row was last resent.</summary>
    public long? ResentFromId { get; set; }
    public DateTime? ResentAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
