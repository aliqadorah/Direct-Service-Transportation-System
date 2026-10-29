using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Application.Platform;
using DirectService.Platform.Domain;
using DirectService.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DirectService.Platform.Infrastructure.Email;

/// <summary>The client secret and the queued messages, each under its own Data Protection purpose (SEC-09).</summary>
public sealed class DataProtectionEmailProtector(IDataProtectionProvider provider) : IEmailProtector
{
    private readonly IDataProtector _secret = provider.CreateProtector(DataProtectionPurposes.SystemEmailClientSecret);
    private readonly IDataProtector _payload = provider.CreateProtector(DataProtectionPurposes.SystemEmailOutbox);

    public byte[] ProtectSecret(string secret) => _secret.Protect(System.Text.Encoding.UTF8.GetBytes(secret));

    public string UnprotectSecret(byte[] value) => System.Text.Encoding.UTF8.GetString(_secret.Unprotect(value));

    public byte[] ProtectPayload(string payload) => _payload.Protect(System.Text.Encoding.UTF8.GetBytes(payload));

    public string UnprotectPayload(byte[] value) => System.Text.Encoding.UTF8.GetString(_payload.Unprotect(value));
}

/// <summary>In-process wake-up of the outbox worker (a signal, not a queue: the table is the queue).</summary>
public sealed class EmailOutboxSignal : IEmailOutboxSignal, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Set()
    {
        try { _signal.Release(); } catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}

/// <summary>The message kept (encrypted) while it may still be sent.</summary>
internal sealed record OutboxPayload(string To, string Subject, string Html, string Text);

/// <summary>
/// The application's <see cref="IEmailSender"/> (DT-251): every email is written to core.EmailOutbox and sent by the outbox worker. The row is
/// written in its own unit of work at once (as the folder sender wrote its file at once), so a flow's later failure does not lose an email
/// already promised. The layout is drawn again with the active sender name and reply-to address. The same kind of email to the same address
/// is limited per hour (a flood of password-reset requests is logged as Suppressed, not sent).
/// </summary>
public sealed class OutboxEmailSender(IServiceScopeFactory scopes, IRequestContext ctx, IEmailProtector protector, IEmailOutboxSignal signal,
    IOptions<SystemEmailOptions> options, TimeProvider clock) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = clock.GetUtcNow().UtcDateTime;
        var o = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MasterDbContext>();
        var to = message.To.Trim();
        var row = new EmailOutbox
        {
            TenantId = ctx.TenantId, Template = message.Template, LanguageCode = message.Language == "en" ? "en" : "ar", Recipient = to.Length <= 254 ? to : to[..254],
            CreatedAt = now, NextAttemptAt = now, HasSecretLink = message.HasSecretLink || EmailTemplates.SecretLinkTemplates.Contains(message.Template),
        };
        var hourAgo = now.AddHours(-1);
        if (!SystemEmailService.IsAddress(to))
        {
            (row.Status, row.FailedAt, row.ErrorKind, row.LastError) = (EmailStatus.Failed, now, EmailErrorKinds.InvalidAddress, "The address is not a valid email address.");
        }
        else if (await db.Set<EmailOutbox>().CountAsync(e => e.Recipient == to && e.Template == message.Template && e.CreatedAt >= hourAgo, ct).ConfigureAwait(false)
                 >= o.PerRecipientPerHour)
        {
            (row.Status, row.ErrorKind, row.LastError) = (EmailStatus.Suppressed, EmailErrorKinds.Suppressed,
                $"More than {o.PerRecipientPerHour} emails of this kind to this address within an hour.");
        }
        else
        {
            var active = await SystemEmailService.ActiveAsync(db, o, ct).ConfigureAwait(false);
            var (html, text) = message.Content is { } content
                ? EmailLayout.Render(content, new EmailBrand(active.DisplayName, active.ReplyTo))
                : (message.HtmlBody, message.TextBody);
            row.Status = EmailStatus.Pending;
            row.PayloadEncrypted = protector.ProtectPayload(JsonSerializer.Serialize(new OutboxPayload(to, message.Subject, html, text)));
        }
        db.Set<EmailOutbox>().Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (row.Status == EmailStatus.Pending) signal.Set();
    }
}

/// <summary>Kept across runs of the worker: a pause asked by Microsoft (429 / 503 Retry-After) and the last housekeeping.</summary>
public sealed class EmailDispatchState
{
    public DateTime PausedUntil { get; set; }
    public DateTime LastHousekeeping { get; set; }
}

/// <summary>
/// One run of the outbox worker (DT-251): (1) an email left "Sending" past its claim (the host stopped mid-send) is marked Failed as
/// interrupted — it may have gone out, so it is never sent again on its own (no duplicates; staff can resend); (2) at most PerMinute emails a
/// minute (default 30, Exchange Online's limit), none while Microsoft asked to wait; (3) due rows are claimed with UPDLOCK / READPAST (one
/// worker per row, also with several hosts) and sent with the active settings; (4) a failure is retried with back-off (1 min, 5 min, 15 min,
/// 1 h, 4 h, 12 h) and Retry-After, and after the last attempt is Failed — the message is wiped when it holds a single-use link — and platform
/// staff are emailed (once in 6 hours); (5) staff are warned 30 and 7 days before the client secret expires; (6) addresses older than the
/// retention period are removed from the log.
/// </summary>
public sealed partial class EmailDispatcher(MasterDbContext db, IEnumerable<IEmailTransport> transports, IEmailProtector protector, IEmailSender email, EmailDispatchState state,
    IOptions<SystemEmailOptions> options, IOptions<AppOptions> app, TimeProvider clock, ILogger<EmailDispatcher> log)
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1), TimeSpan.FromHours(4), TimeSpan.FromHours(12)];
    private static readonly TimeSpan Claim = TimeSpan.FromMinutes(5);
    private const string Removed = "(removed)";

    private SystemEmailOptions O => options.Value;

    /// <summary>Sends what is due; returns how many emails were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var failedNow = await RecoverInterruptedAsync(now, ct).ConfigureAwait(false);
        var sent = 0;
        if (state.PausedUntil <= now)
        {
            var active = await SystemEmailService.ActiveAsync(db, O, ct).ConfigureAwait(false);
            var transport = transports.Single(t => t.Mode == active.Mode);
            var settings = SystemEmailService.ToTransport(active, protector, O);
            var minuteAgo = now.AddMinutes(-1);
            var used = await db.Set<EmailOutbox>().CountAsync(e => e.SentAt >= minuteAgo || e.Status == EmailStatus.Sending, ct).ConfigureAwait(false);
            var room = Math.Min(O.PerMinute - used, 10);
            foreach (var id in room > 0 ? await ClaimAsync(room, now, ct).ConfigureAwait(false) : [])
            {
                var (ok, failed) = await SendOneAsync(id, transport, settings, active.Mode, ct).ConfigureAwait(false);
                if (ok) sent++;
                if (failed) failedNow++;
                if (state.PausedUntil > clock.GetUtcNow().UtcDateTime) break;   // Microsoft asked to wait: the rest waits too
            }
        }
        if (failedNow > 0) await NotifyFailuresAsync(ct).ConfigureAwait(false);
        await WarnSecretExpiryAsync(ct).ConfigureAwait(false);
        if (now - state.LastHousekeeping > TimeSpan.FromHours(1)) await HousekeepingAsync(now, ct).ConfigureAwait(false);
        return sent;
    }

    /// <summary>Rows whose sender stopped mid-send: Failed as interrupted, never sent again on their own.</summary>
    private async Task<int> RecoverInterruptedAsync(DateTime now, CancellationToken ct)
    {
        var stale = await db.Set<EmailOutbox>().Where(e => e.Status == EmailStatus.Sending && e.ClaimedUntil < now).ToListAsync(ct).ConfigureAwait(false);
        foreach (var e in stale)
            Fail(e, now, EmailErrorKinds.Interrupted, "The sending was interrupted (the server stopped); the email may or may not have been delivered, so it is not sent again automatically.");
        if (stale.Count > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return stale.Count;
    }

    /// <summary>Claims due rows for this worker (raw SQL: UPDLOCK, READPAST — listed in BUILD_STATUS.md).</summary>
    private Task<List<long>> ClaimAsync(int count, DateTime now, CancellationToken ct) =>
        db.Database.SqlQueryRaw<long>(
            """
            WITH due AS (
                SELECT TOP (@count) Id, Status, ClaimedUntil, Attempts, LastAttemptAt
                FROM core.EmailOutbox WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = 'Pending' AND NextAttemptAt <= @now
                ORDER BY NextAttemptAt, Id)
            UPDATE due SET Status = 'Sending', ClaimedUntil = @until, Attempts = Attempts + 1, LastAttemptAt = @now
            OUTPUT inserted.Id AS Value;
            """,
            new Microsoft.Data.SqlClient.SqlParameter("@count", count), new Microsoft.Data.SqlClient.SqlParameter("@now", now),
            new Microsoft.Data.SqlClient.SqlParameter("@until", now + Claim)).ToListAsync(ct);

    private async Task<(bool Sent, bool Failed)> SendOneAsync(long id, IEmailTransport transport, EmailTransportSettings settings, string mode, CancellationToken ct)
    {
        var row = await db.Set<EmailOutbox>().SingleAsync(e => e.Id == id, ct).ConfigureAwait(false);
        EmailDeliveryResult result;
        try
        {
            if (row.PayloadEncrypted is null) throw new System.Security.Cryptography.CryptographicException("No message.");
            var p = JsonSerializer.Deserialize<OutboxPayload>(protector.UnprotectPayload(row.PayloadEncrypted))!;
            result = await transport.SendAsync(new OutgoingEmail(p.To, p.Subject, p.Html, p.Text, row.Template), settings, ct).ConfigureAwait(false);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            result = EmailDeliveryResult.Fail(EmailErrorKinds.Unknown, "The queued message cannot be read (it was removed, or the data-protection keys changed).");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        row.Mode = mode;
        row.ClaimedUntil = null;
        row.LastAttemptAt = now;
        var failed = false;
        if (result.Sent)
        {
            (row.Status, row.SentAt, row.ErrorKind, row.LastError, row.PayloadEncrypted) = (EmailStatus.Sent, now, null, null, null);
        }
        else
        {
            if (result.RetryAfter is { } wait) state.PausedUntil = now + wait;
            if (row.Attempts >= O.MaxAttempts || EmailErrorKinds.IsFinal(result.ErrorKind!))
            {
                Fail(row, now, result.ErrorKind!, result.Detail);
                failed = true;
            }
            else
            {
                var delay = Backoff[Math.Min(row.Attempts, Backoff.Length) - 1];
                if (result.RetryAfter is { } after && after > delay) delay = after;
                (row.Status, row.NextAttemptAt, row.ErrorKind, row.LastError) = (EmailStatus.Pending, now + delay, result.ErrorKind, result.Detail);
            }
            LogNotSent(log, row.Template, row.Attempts, result.ErrorKind);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (result.Sent, failed && row.Template is not EmailTemplateNames.SystemEmailFailures);
    }

    private static void Fail(EmailOutbox row, DateTime now, string kind, string? detail)
    {
        (row.Status, row.FailedAt, row.ClaimedUntil, row.ErrorKind, row.LastError) = (EmailStatus.Failed, now, null, kind, detail);
        if (row.HasSecretLink) row.PayloadEncrypted = null;   // a single-use link is never kept after a failure
    }

    /// <summary>Platform staff hear of failed emails — at most one email in 6 hours (the console banner shows them at once).</summary>
    private async Task NotifyFailuresAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddHours(-6);
        if (await db.Set<EmailOutbox>().AnyAsync(e => e.Template == EmailTemplateNames.SystemEmailFailures && e.CreatedAt >= since, ct).ConfigureAwait(false)) return;
        var day = now.AddHours(-24);
        var count = await db.Set<EmailOutbox>().CountAsync(e => e.Status == EmailStatus.Failed && e.FailedAt >= day && e.Template != EmailTemplateNames.SystemEmailFailures, ct)
            .ConfigureAwait(false);
        foreach (var s in await StaffAsync(ct).ConfigureAwait(false))
            await email.SendAsync(EmailTemplates.SystemEmailFailures(s.Email!, s.FullName, count, Link(), s.LanguageCode), ct).ConfigureAwait(false);
    }

    /// <summary>The client secret of the active or pending settings expires within 30 (then 7) days: one warning each to every staff member.</summary>
    private async Task WarnSecretExpiryAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = await db.Set<SystemEmailSetting>().AsNoTracking()
            .Where(s => (s.State == EmailSettingStates.Active || s.State == EmailSettingStates.Pending) && s.Mode == EmailModes.Microsoft365 && s.SecretExpiresOn != null)
            .Select(s => new { s.Id, s.SecretExpiresOn, s.ExpiryWarnedDays }).ToListAsync(ct).ConfigureAwait(false);
        foreach (var s in rows)
        {
            var left = s.SecretExpiresOn!.Value.DayNumber - today.DayNumber;
            int? step = left <= 7 ? 7 : left <= 30 ? 30 : null;
            if (step is not int warn || s.ExpiryWarnedDays <= warn) continue;
            foreach (var staff in await StaffAsync(ct).ConfigureAwait(false))
                await email.SendAsync(EmailTemplates.SecretExpiring(staff.Email!, staff.FullName, s.SecretExpiresOn.Value, Link(), staff.LanguageCode), ct).ConfigureAwait(false);
            // Not a change made by anyone: recorded on the row without an audit entry.
            await db.Set<SystemEmailSetting>().Where(x => x.Id == s.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.ExpiryWarnedDays, warn), ct).ConfigureAwait(false);
        }
    }

    /// <summary>Recipient addresses (and any message still kept) of entries older than the retention period are removed.</summary>
    private async Task HousekeepingAsync(DateTime now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-O.LogRetentionDays);
        await db.Set<EmailOutbox>().Where(e => e.CreatedAt < cutoff && e.Recipient != Removed && e.Status != EmailStatus.Pending && e.Status != EmailStatus.Sending)
            .ExecuteUpdateAsync(x => x.SetProperty(e => e.Recipient, Removed).SetProperty(e => e.PayloadEncrypted, (byte[]?)null), ct).ConfigureAwait(false);
        state.LastHousekeeping = now;
    }

    private Task<List<User>> StaffAsync(CancellationToken ct) =>
        db.Set<User>().IgnoreQueryFilters().AsNoTracking().Where(u => u.IsPlatformStaff && !u.IsDeleted && u.Status == "Active" && u.Email != null && u.PasswordHash != null)
            .ToListAsync(ct);

    private string Link() => app.Value.PublicBaseUrl.TrimEnd('/') + "/console/system-email";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email {Template} not sent (attempt {Attempt}): {Kind}.")]
    private static partial void LogNotSent(ILogger logger, string template, int attempt, string? kind);
}
