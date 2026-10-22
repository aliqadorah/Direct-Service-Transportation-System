using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.Notifications;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Provider console → System email (DT-250…DT-252), platform staff only (PlatformStaffFilter). The settings: Development (save to folder) or
/// Microsoft 365. A change of mode, mailbox, tenant ID, client ID or secret is saved as Pending and becomes Active only when a test email
/// with it has been sent; until then the previous working settings keep sending. Sender name, reply-to and the secret's expiry date apply at
/// once. Every save needs a fresh two-step code; every change is audited (the secret never — "***"). Also the status, the delivery log
/// (no content, no links) and resending a failed email (the same message, or a new link where the flow can make one).
/// </summary>
public sealed partial class SystemEmailService(IAppDbContext db, IRequestContext ctx, IEmailProtector protector, IEnumerable<IEmailTransport> transports, AuthService auth,
    AccountService accounts, StaffService staff, IUrlTokenService urlTokens, AuditTrail audit, IChangeContext changes, IEmailOutboxSignal signal,
    IOptions<SystemEmailOptions> options, IOptions<AppOptions> app, TimeProvider clock)
{
    private const string Entity = "CORE.SystemEmailSetting";
    private SystemEmailOptions O => options.Value;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ================================================================= settings

    /// <summary>The settings in use: the Active row, or — never configured — the development folder.</summary>
    public static async Task<SystemEmailSetting> ActiveAsync(IAppDbContext db, SystemEmailOptions o, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(o);
        return await db.Set<SystemEmailSetting>().AsNoTracking().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Active, ct).ConfigureAwait(false)
            ?? Default(o);
    }

    private static SystemEmailSetting Default(SystemEmailOptions o) => new()
    {
        Id = 0, State = EmailSettingStates.Active, Mode = EmailModes.Development, SenderMailbox = o.From, DisplayName = EmailLayout.ProductEn,
    };

    /// <summary>A settings row as the transport needs it (the secret decrypted, in memory only for the call).</summary>
    public static EmailTransportSettings ToTransport(SystemEmailSetting s, IEmailProtector protector, SystemEmailOptions o)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(protector);
        ArgumentNullException.ThrowIfNull(o);
        return s.Mode == EmailModes.Microsoft365
            ? new EmailTransportSettings(s.Mode, s.SenderMailbox!, s.DisplayName, s.ReplyTo, s.EntraTenantId, s.EntraClientId,
                s.ClientSecretEncrypted is { } secret ? protector.UnprotectSecret(secret) : null)
            : new EmailTransportSettings(s.Mode, o.From, s.DisplayName, s.ReplyTo);
    }

    public async Task<SystemEmailResponse> GetAsync(CancellationToken ct)
    {
        var active = await ActiveAsync(db, O, ct).ConfigureAwait(false);
        var pending = await db.Set<SystemEmailSetting>().AsNoTracking().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Pending, ct).ConfigureAwait(false);
        return new SystemEmailResponse(View(active), pending is null ? null : View(pending), await StatusAsync(active, ct).ConfigureAwait(false));
    }

    private static SystemEmailSettingsView View(SystemEmailSetting s) => new(s.Mode, s.EntraTenantId, s.EntraClientId, s.ClientSecretEncrypted is not null, s.SecretSavedAt,
        s.SecretExpiresOn, s.SenderMailbox, s.DisplayName, s.ReplyTo, s.ActivatedAt, s.LastTestAt, s.LastTestOk, ErrorKindOf(s.LastTestError), DetailOf(s.LastTestError),
        s.Id == 0 || s.RowVersion.Length == 0 ? null : Convert.ToBase64String(s.RowVersion));

    /// <summary>A test's stored error is "kind: detail".</summary>
    private static string? ErrorKindOf(string? stored) => stored?.Split(':', 2)[0];

    private static string? DetailOf(string? stored) => stored?.Split(':', 2) is [_, var d] ? d.Trim() : null;

    private async Task<SystemEmailStatus> StatusAsync(SystemEmailSetting active, CancellationToken ct)
    {
        var rows = db.Set<EmailOutbox>().AsNoTracking();
        var day = Now.AddHours(-24);
        var waiting = await rows.CountAsync(e => e.Status == EmailStatus.Pending || e.Status == EmailStatus.Sending, ct).ConfigureAwait(false);
        var failed = await rows.CountAsync(e => e.Status == EmailStatus.Failed && e.ResentAt == null, ct).ConfigureAwait(false);
        var failedDay = await rows.CountAsync(e => e.Status == EmailStatus.Failed && e.FailedAt >= day, ct).ConfigureAwait(false);
        var lastSuccess = await rows.Where(e => e.SentAt != null && e.Template != nameof(EmailTemplates.TestEmail)).MaxAsync(e => e.SentAt, ct).ConfigureAwait(false);
        // Working / failing: the outcome of the latest attempt with the settings in use (tests of pending settings are not counted).
        var latest = await rows.Where(e => e.LastAttemptAt != null && e.Template != nameof(EmailTemplates.TestEmail)).OrderByDescending(e => e.LastAttemptAt)
            .Select(e => new { e.ErrorKind, e.Status }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        bool? working = latest is null ? null : latest.Status == EmailStatus.Sent;
        return new SystemEmailStatus(working, lastSuccess, waiting, failed, failedDay, DaysLeft(active), O.PerMinute, working == false ? latest!.ErrorKind : null);
    }

    private int? DaysLeft(SystemEmailSetting s) =>
        s.Mode == EmailModes.Microsoft365 && s.SecretExpiresOn is DateOnly d ? d.DayNumber - DateOnly.FromDateTime(Now).DayNumber : null;

    public async Task<SystemEmailResponse> SaveAsync(SaveSystemEmailRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var mode = r.Mode?.Trim();
        var m365 = mode == EmailModes.Microsoft365;
        var tenant = Clean(r.EntraTenantId);
        var client = Clean(r.EntraClientId);
        var mailbox = Clean(r.SenderMailbox)?.ToLowerInvariant();
        var replyTo = Clean(r.ReplyTo)?.ToLowerInvariant();
        var name = Clean(r.DisplayName);
        var secret = string.IsNullOrWhiteSpace(r.ClientSecret) ? null : r.ClientSecret.Trim();

        var active = await db.Set<SystemEmailSetting>().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Active, ct).ConfigureAwait(false);
        var pending = await db.Set<SystemEmailSetting>().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Pending, ct).ConfigureAwait(false);
        var current = active ?? Default(O);
        // The secret saved for the same app (tenant + client) can stay; another app needs its own.
        byte[]? KeptSecret() => new[] { pending, active }.FirstOrDefault(s => s is { Mode: EmailModes.Microsoft365 } && s.EntraTenantId == tenant && s.EntraClientId == client)
            ?.ClientSecretEncrypted;

        var v = new Validator()
            .Check(mode is not null && EmailModes.All.Contains(mode), "mode", mode is null ? ErrorCodes.ValidationRequired : ErrorCodes.ValidationFormat)
            .Require("displayName", name).MaxLength("displayName", name, 100)
            .Check(name is null || !HasControl(name), "displayName")
            .MaxLength("replyTo", replyTo, 254).Check(replyTo is null || IsAddress(replyTo), "replyTo");
        if (m365)
        {
            v.Require("entraTenantId", tenant).Check(tenant is null || TenantIdPattern().IsMatch(tenant), "entraTenantId")
                .Require("entraClientId", client).Check(client is null || Guid.TryParse(client, out _), "entraClientId")
                .Require("senderMailbox", mailbox).MaxLength("senderMailbox", mailbox, 254).Check(mailbox is null || IsAddress(mailbox), "senderMailbox")
                .Check(secret is not null || KeptSecret() is not null, "clientSecret", ErrorCodes.ValidationRequired)
                .MaxLength("clientSecret", secret, 400).Check(secret is null || !HasControl(secret), "clientSecret")
                .Check(r.SecretExpiresOn is not null, "secretExpiresOn", ErrorCodes.ValidationRequired);
        }
        v.ThrowIfInvalid();

        // Every save needs a fresh code from the authenticator app (owner request: secret, mode and sender changes; one rule for the screen).
        var me = await db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Id == ctx.UserId, ct).ConfigureAwait(false);
        await auth.VerifyFreshCodeAsync(me, r.Code, ct).ConfigureAwait(false);

        var gated = mode != current.Mode
            || (m365 && (tenant != current.EntraTenantId || client != current.EntraClientId || !string.Equals(mailbox, current.SenderMailbox, StringComparison.OrdinalIgnoreCase)
                || secret is not null));
        await db.InTransactionAsync(async c =>
        {
            if (gated)
            {
                // Saved as pending: used only after a successful test email (the active settings keep sending until then).
                var row = pending;
                if (row is null)
                {
                    row = new SystemEmailSetting { State = EmailSettingStates.Pending };
                    db.Set<SystemEmailSetting>().Add(row);
                }
                else
                {
                    db.ExpectRowVersion(row, r.RowVersion);
                }
                var keep = KeptSecret();
                row.Mode = mode!;
                row.EntraTenantId = m365 ? tenant : null;
                row.EntraClientId = m365 ? client : null;
                row.SenderMailbox = m365 ? mailbox : null;
                if (!m365)
                {
                    row.ClientSecretEncrypted = null;
                    row.SecretSavedAt = null;
                    row.SecretExpiresOn = null;
                }
                else if (secret is not null)
                {
                    row.ClientSecretEncrypted = protector.ProtectSecret(secret);
                    row.SecretSavedAt = Now;
                    row.ExpiryWarnedDays = null;
                }
                else if (!ReferenceEquals(row.ClientSecretEncrypted, keep))
                {
                    row.ClientSecretEncrypted = keep;
                    row.SecretSavedAt = new[] { pending, active }.First(s => s?.ClientSecretEncrypted == keep)!.SecretSavedAt;
                }
                if (m365 && row.SecretExpiresOn != r.SecretExpiresOn) row.ExpiryWarnedDays = null;
                if (m365) row.SecretExpiresOn = r.SecretExpiresOn;
                row.DisplayName = name!;
                row.ReplyTo = replyTo;
                row.LastTestAt = null;
                row.LastTestOk = null;
                row.LastTestError = null;
            }
            else
            {
                // Only the sender name, reply-to or the secret's expiry date: applied to the settings in use at once; a pending set is dropped
                // (the form was put back to what is active).
                if (active is null)
                {
                    active = new SystemEmailSetting { State = EmailSettingStates.Active, Mode = EmailModes.Development, ActivatedAt = Now };
                    db.Set<SystemEmailSetting>().Add(active);
                }
                else if (pending is null)
                {
                    db.ExpectRowVersion(active, r.RowVersion);
                }
                active.DisplayName = name!;
                active.ReplyTo = replyTo;
                if (m365 && active.SecretExpiresOn != r.SecretExpiresOn)
                {
                    active.SecretExpiresOn = r.SecretExpiresOn;
                    active.ExpiryWarnedDays = null;
                }
                if (pending is not null)
                {
                    pending.State = EmailSettingStates.Retired;
                    changes.SetAction(pending, "Cancel");
                }
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    // ================================================================= test email

    /// <summary>
    /// Sends a test email at once (not through the queue) with the pending settings, or the active ones when nothing is pending, and says
    /// in plain words why it failed. Pending settings that sent it become active. The test is in the delivery log and the audit log.
    /// </summary>
    public async Task<SystemEmailTestResponse> TestAsync(SystemEmailTestRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var address = Clean(r.Address)?.ToLowerInvariant();
        new Validator().Require("testAddress", address).MaxLength("testAddress", address, 254).Check(address is null || IsAddress(address), "testAddress").ThrowIfInvalid();
        // Test emails go out at once (not through the queue and its per-address limit): at most 10 in 10 minutes for the whole console.
        var since = Now.AddMinutes(-10);
        if (await db.Set<EmailOutbox>().CountAsync(e => e.Template == nameof(EmailTemplates.TestEmail) && e.CreatedAt >= since, ct).ConfigureAwait(false) >= 10)
            throw new AppException(ErrorCodes.RateLimited, 429);

        var pending = await db.Set<SystemEmailSetting>().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Pending, ct).ConfigureAwait(false);
        var active = await db.Set<SystemEmailSetting>().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Active, ct).ConfigureAwait(false);
        var target = pending ?? active ?? Default(O);
        var settings = ToTransport(target, protector, O);
        var transport = transports.Single(t => t.Mode == target.Mode);
        var message = EmailTemplates.TestEmail(address!, target.Mode == EmailModes.Microsoft365 ? "Microsoft 365" : "Development", settings.SenderMailbox,
            app.Value.PublicBaseUrl.TrimEnd('/') + "/console/system-email", ctx.Language == "en" ? "en" : "ar");
        var (html, text) = EmailLayout.Render(message.Content!, new EmailBrand(target.DisplayName, target.ReplyTo));
        var result = await transport.SendAsync(new OutgoingEmail(address!, message.Subject, html, text, message.Template), settings, ct).ConfigureAwait(false);

        var now = Now;
        var activated = false;
        await db.InTransactionAsync(async c =>
        {
            db.Set<EmailOutbox>().Add(new EmailOutbox
            {
                Template = message.Template, LanguageCode = message.Language, Recipient = address!, Attempts = 1, CreatedAt = now, NextAttemptAt = now, LastAttemptAt = now,
                Status = result.Sent ? EmailStatus.Sent : EmailStatus.Failed, SentAt = result.Sent ? now : null, FailedAt = result.Sent ? null : now,
                ErrorKind = result.ErrorKind, LastError = result.Detail, Mode = target.Mode,
            });
            if (target.Id != 0)
            {
                target.LastTestAt = now;
                target.LastTestOk = result.Sent;
                target.LastTestError = result.Sent ? null : $"{result.ErrorKind}: {result.Detail}";
                changes.SetAction(target, "EmailTest");
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (result.Sent && pending is not null)
            {
                // The new settings work: they replace the active ones (Retired first — one Active row at a time).
                if (active is not null)
                {
                    active.State = EmailSettingStates.Retired;
                    changes.SetAction(active, "Deactivate");
                    await db.SaveChangesAsync(c).ConfigureAwait(false);
                }
                pending.State = EmailSettingStates.Active;
                pending.ActivatedAt = now;
                changes.SetAction(pending, "Activate");
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                activated = true;
            }
            if (target.Id == 0)
            {
                // Never configured (the development folder): the test still goes into the audit log.
                audit.Record(Entity, 0, "EmailTest", null, new Dictionary<string, object?> { ["Mode"] = target.Mode, ["LastTestOk"] = result.Sent, ["ErrorKind"] = result.ErrorKind });
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
        return new SystemEmailTestResponse(result.Sent, result.ErrorKind, result.Detail, activated, result.Warnings ?? [], await GetAsync(ct).ConfigureAwait(false));
    }

    // ================================================================= delivery log and resend

    /// <summary>Templates whose single-use link a flow can make again (the old link stops working).</summary>
    private static readonly HashSet<string> NewLinkTemplates = [nameof(EmailTemplates.PasswordReset), nameof(EmailTemplates.VerifyEmail), nameof(EmailTemplates.StaffInvitation)];

    public async Task<PagedResult<EmailLogItem>> LogAsync(EmailLogQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var page = Math.Max(1, q.Page);
        var size = Math.Clamp(q.PageSize, 1, 100);
        var rows = db.Set<EmailOutbox>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q.Status) && EmailStatus.All.Contains(q.Status)) rows = rows.Where(e => e.Status == q.Status);
        if (!string.IsNullOrWhiteSpace(q.Template)) rows = rows.Where(e => e.Template == q.Template);
        if (Clean(q.Q) is { } text) rows = rows.Where(e => e.Recipient.Contains(text));
        var total = await rows.CountAsync(ct).ConfigureAwait(false);
        var items = await rows.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Skip((page - 1) * size).Take(size)
            .Select(e => new { e.PublicId, e.Template, e.LanguageCode, e.Recipient, e.Status, e.Attempts, e.CreatedAt, e.SentAt, e.FailedAt, e.NextAttemptAt, e.ErrorKind,
                e.LastError, e.Mode, HasPayload = e.PayloadEncrypted != null, e.ResentAt })
            .ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<EmailLogItem>(items.Select(e =>
        {
            var (how, note) = e.Status != EmailStatus.Failed ? (null, null) : ResendWay(e.Template, e.HasPayload);
            return new EmailLogItem(e.PublicId.ToString("N"), e.Template, e.LanguageCode, e.Recipient, e.Status, e.Attempts, e.CreatedAt, e.SentAt, e.FailedAt,
                e.Status == EmailStatus.Pending ? e.NextAttemptAt : null, e.ErrorKind, e.LastError, e.Mode, how, note, e.ResentAt);
        }).ToList(), page, size, total);
    }

    /// <summary>How a failed email can be sent again: "same" message, a "newLink", or a note saying what to do instead.</summary>
    private static (string? How, string? Note) ResendWay(string template, bool hasPayload) =>
        hasPayload ? ("same", null)
        : NewLinkTemplates.Contains(template) ? ("newLink", null)
        : template switch
        {
            nameof(EmailTemplates.Invitation) => (null, "askCompanyAdmin"),
            nameof(EmailTemplates.ConfirmEmailChange) => (null, "askUser"),
            nameof(EmailTemplates.TestEmail) => (null, "sendTest"),
            _ => (null, "gone"),
        };

    public async Task<EmailResendResponse> ResendAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out var publicId)) throw Fail.NotFound();
        var row = await db.Set<EmailOutbox>().SingleOrDefaultAsync(e => e.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        if (row.Status != EmailStatus.Failed) throw Fail.Rule(ErrorCodes.SystemEmailNotFailed);
        var (how, _) = ResendWay(row.Template, row.PayloadEncrypted is not null);
        if (how is null) throw Fail.Rule(ErrorCodes.SystemEmailNotResendable);
        var now = Now;
        if (how == "same")
        {
            db.Set<EmailOutbox>().Add(new EmailOutbox
            {
                TenantId = row.TenantId, Template = row.Template, LanguageCode = row.LanguageCode, Recipient = row.Recipient, Status = EmailStatus.Pending, CreatedAt = now,
                NextAttemptAt = now, HasSecretLink = row.HasSecretLink, PayloadEncrypted = row.PayloadEncrypted, ResentFromId = row.Id,
            });
            // The failed row keeps its log entry; its copy of the message is no longer needed.
            row.PayloadEncrypted = null;
        }
        else
        {
            // A new single-use link from the flow itself (the same checks as when the person asks: an inactive account gets nothing).
            switch (row.Template)
            {
                case nameof(EmailTemplates.PasswordReset):
                    await accounts.RequestPasswordResetAsync(row.Recipient, ct).ConfigureAwait(false);
                    break;
                case nameof(EmailTemplates.VerifyEmail):
                    await accounts.ResendVerificationAsync(row.Recipient, ct).ConfigureAwait(false);
                    break;
                default:
                    var invited = await db.Set<User>().IgnoreQueryFilters().AsNoTracking()
                        .Where(u => u.Email == row.Recipient && u.IsPlatformStaff && !u.IsDeleted).Select(u => (Guid?)u.PublicId).SingleOrDefaultAsync(ct).ConfigureAwait(false)
                        ?? throw Fail.Rule(ErrorCodes.SystemEmailNotResendable);
                    await staff.ResendAsync(urlTokens.Encode<User>(invited), ct).ConfigureAwait(false);
                    break;
            }
        }
        row.ResentAt = now;
        audit.Record("CORE.EmailOutbox", row.Id, "Resend", null, new Dictionary<string, object?> { ["Template"] = row.Template, ["How"] = how });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        signal.Set();
        return new EmailResendResponse(how);
    }

    /// <summary>Shown on every console page: failures of the last 24 hours, a secret expiring within 30 days, the latest attempt failing.</summary>
    public async Task<SystemEmailAlertResponse> AlertAsync(CancellationToken ct)
    {
        var active = await ActiveAsync(db, O, ct).ConfigureAwait(false);
        var status = await StatusAsync(active, ct).ConfigureAwait(false);
        var pending = await db.Set<SystemEmailSetting>().AsNoTracking().SingleOrDefaultAsync(s => s.State == EmailSettingStates.Pending, ct).ConfigureAwait(false);
        var days = new[] { status.SecretDaysLeft, pending is null ? null : DaysLeft(pending) }.Where(d => d is not null).Min();
        return new SystemEmailAlertResponse(status.FailedLast24Hours, days is <= 30 ? days : null, status.Working == false);
    }

    // ================================================================= helpers

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Line breaks and other control characters are refused in anything that becomes a mail header (header injection).</summary>
    public static bool HasControl(string s) => s.Any(char.IsControl);

    /// <summary>One plain address ("name@example.com"), no display name, no control characters.</summary>
    public static bool IsAddress(string s) =>
        !HasControl(s) && !s.Contains(' ', StringComparison.Ordinal) && MailAddress.TryCreate(s, out var a) && string.Equals(a.Address, s, StringComparison.OrdinalIgnoreCase)
        && a.Host.Contains('.', StringComparison.Ordinal);

    /// <summary>A directory ID (GUID) or a verified domain name of the Microsoft 365 tenant (contoso.onmicrosoft.com).</summary>
    [GeneratedRegex(@"^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)+)$")]
    private static partial Regex TenantIdPattern();

    internal static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
