namespace DirectService.Platform.Contracts.Tenants;

/// <summary>
/// One set of system email settings as the console shows it (DT-250). The client secret is never returned: only whether one is saved, when,
/// and the expiry date staff entered.
/// </summary>
public sealed record SystemEmailSettingsView(string Mode, string? EntraTenantId, string? EntraClientId, bool HasSecret, DateTime? SecretSavedAt, DateOnly? SecretExpiresOn,
    string? SenderMailbox, string DisplayName, string? ReplyTo, DateTime? ActivatedAt, DateTime? LastTestAt, bool? LastTestOk, string? LastTestErrorKind, string? LastTestError,
    string? RowVersion);

/// <summary>
/// Working = the last attempt of the active settings sent (null: nothing sent yet); Waiting = queued or being sent; Failed = after the last
/// attempt (all time and in the last 24 hours); the secret's days left (negative = expired).
/// </summary>
public sealed record SystemEmailStatus(bool? Working, DateTime? LastSuccessAt, int Waiting, int Failed, int FailedLast24Hours, int? SecretDaysLeft, int PerMinute,
    string? LastErrorKind);

/// <summary>The active settings (used now), the pending ones (saved, waiting for a successful test) and the status.</summary>
public sealed record SystemEmailResponse(SystemEmailSettingsView Active, SystemEmailSettingsView? Pending, SystemEmailStatus Status);

/// <summary>
/// Save the settings. A change of mode, mailbox, tenant ID, client ID or secret is saved as pending and used only after a successful test
/// email; the sender name, reply-to address and the secret's expiry date apply at once. <paramref name="ClientSecret"/> is write-only (null or
/// empty = keep the saved one). Every save needs a fresh two-step verification code (<paramref name="Code"/>).
/// </summary>
public sealed record SaveSystemEmailRequest(string? Mode, string? EntraTenantId, string? EntraClientId, string? ClientSecret, DateOnly? SecretExpiresOn,
    string? SenderMailbox, string? DisplayName, string? ReplyTo, string? Code, string? RowVersion);

/// <summary>Send a test email with the pending settings (or the active ones) to <paramref name="Address"/>.</summary>
public sealed record SystemEmailTestRequest(string? Address);

/// <summary>
/// The test: sent or not, the kind of error (worded by the console) and its technical detail, whether the pending settings became active,
/// and warnings (e.g. the app may send as any mailbox of the tenant).
/// </summary>
public sealed record SystemEmailTestResponse(bool Sent, string? ErrorKind, string? Detail, bool Activated, IReadOnlyList<string> Warnings, SystemEmailResponse Settings);

/// <summary>Filters of the delivery log.</summary>
public sealed record EmailLogQuery(string? Status, string? Template, string? Q, int Page = 1, int PageSize = 25);

/// <summary>
/// One email of the delivery log: never its content or link. <paramref name="Resend"/>: "same" (the message is kept and is sent again),
/// "newLink" (a new link is made, the old one stops working), or null with <paramref name="ResendNote"/> saying what to do instead.
/// </summary>
public sealed record EmailLogItem(string Id, string Template, string Language, string Recipient, string Status, int Attempts, DateTime CreatedAt, DateTime? SentAt,
    DateTime? FailedAt, DateTime? NextAttemptAt, string? ErrorKind, string? Error, string? Mode, string? Resend, string? ResendNote, DateTime? ResentAt);

/// <summary>What a resend did: queued the same message again, or asked the flow for a new link.</summary>
public sealed record EmailResendResponse(string How);

/// <summary>For every console page: system email needs attention (emails failed in the last 24 hours, the secret expires within 30 days).</summary>
public sealed record SystemEmailAlertResponse(int FailedLast24Hours, int? SecretDaysLeft, bool Failing);
