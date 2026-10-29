namespace DirectService.Platform.Application.Notifications;

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
    EmailContent? Content = null, bool HasSecretLink = false);
