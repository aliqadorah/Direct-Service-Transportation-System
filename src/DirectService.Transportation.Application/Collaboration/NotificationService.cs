using DirectService.Transportation.Application.Abstractions;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// In-app notifications (FR-190): rejected e-invoices, e-invoicing set up wrongly, finished exports, failed postings, accepted invitations,
/// support-access requests, failed renewals. A notification belongs to one user (and a company when it concerns one). It stores its type
/// and parameters; the title and text are built in the reader's language when shown (<see cref="NotificationTexts"/>, DT-189) and its link
/// is resolved again then (<see cref="NotificationLinks"/>). New ones are pushed to the user's connections (RT-01). Users list their own
/// notifications and mark them read; nobody else can.
/// </summary>
public sealed class NotificationService(IAppDbContext db, IRequestContext ctx, IUrlTokenService tokens, IServiceProvider services, TimeProvider clock, IAfterCommit afterCommit,
    NotificationLinks links)
{
    public const string EInvoiceRejected = "EINV.Rejected";
    public const string EInvoiceSetupError = "EINV.SetupError";
    public const string PostingFailed = "ACC.PostingFailed";
    public const string ExportReady = "CORE.ExportReady";
    public const string ExportFailed = "CORE.ExportFailed";
    public const string InvitationAccepted = "CORE.InvitationAccepted";
    public const string SupportRequested = "CORE.SupportRequested";
    public const string PaymentFailed = "CORE.PaymentFailed";

    public const int MaxMessage = 500;

    private static readonly string[] TenantFilter = ["Tenant"];

    /// <summary>Active users holding <paramref name="permission"/> in the company (recipients of a company event).</summary>
    public async Task<IReadOnlyList<long>> HoldersAsync(long companyId, string permission, CancellationToken ct) =>
        await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters(TenantFilter)
            .Where(r => r.CompanyId == companyId && r.User.Status == "Active" && !r.Role.IsDeleted && db.Set<RolePermission>().IgnoreQueryFilters(TenantFilter)
                .Any(p => p.RoleId == r.RoleId && p.Permission.PermissionKey == permission))
            .Select(r => r.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false);

    /// <summary>The stored message of a notification: its parameters (the text is built when shown).</summary>
    public static string Message(IReadOnlyDictionary<string, string?> parameters) => NotificationTexts.Store(parameters);

    /// <summary>Adds a notification for each user and saves (inside the caller's transaction when there is one), then pushes it.</summary>
    public async Task NotifyAsync(IEnumerable<long> userIds, long? companyId, string type, IReadOnlyDictionary<string, string?> parameters, string? link, CancellationToken ct,
        long? tenant = null)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var tenantId = tenant ?? ctx.TenantId ?? await db.Set<Company>().AsNoTracking().IgnoreQueryFilters().Where(c => c.Id == companyId).Select(c => (long?)c.TenantId)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("A notification needs a tenant.");
        var message = Message(parameters);
        var rows = ids.Select(u => new Notification { TenantId = tenantId, UserId = u, CompanyId = companyId, NotificationType = type, Message = message, Link = link }).ToList();
        db.Set<Notification>().AddRange(rows);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await PushAsync(rows, ct).ConfigureAwait(false);
    }

    private async Task PushAsync(List<Notification> rows, CancellationToken ct)
    {
        var notifier = services.GetService(typeof(IRealtimeNotifier)) as IRealtimeNotifier;
        if (notifier is null || rows.Count == 0) return;
        // Pushed once the caller's transaction commits, so nobody is told about a notification that was rolled back (review M3).
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.PublicId }).ToDictionaryAsync(u => u.Id, u => u.PublicId, ct)
            .ConfigureAwait(false);
        var pushes = rows.Select(n => (User: users[n.UserId], Payload: (object)new { id = tokens.Encode<Notification>(n.PublicId), type = n.NotificationType })).ToList();
        afterCommit.Add(async () =>
        {
            foreach (var (user, payload) in pushes)
            {
                try
                {
                    await notifier.NotifyUserAsync(user, payload, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Pushing is best effort; the notification is stored.
                }
            }
        });
    }

    /// <summary>
    /// JoFotara refused the company's credentials (DT-189): a configuration problem, not a document one, so each recipient keeps ONE open
    /// notification per company ("e-invoicing is not set up correctly", linking to the settings) instead of one per document; a repeated
    /// refusal only refreshes its details. The number of affected documents is counted when it is shown.
    /// </summary>
    public async Task ReportSetupErrorAsync(IEnumerable<long> userIds, long companyId, string? reason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return;
        var message = Message(new Dictionary<string, string?> { ["reason"] = reason });
        var open = await db.Set<Notification>().IgnoreQueryFilters(TenantFilter)
            .Where(n => n.CompanyId == companyId && n.NotificationType == EInvoiceSetupError && ids.Contains(n.UserId)).ToListAsync(ct).ConfigureAwait(false);
        foreach (var n in open) n.Message = message;
        if (open.Count > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var missing = ids.Except(open.Select(n => n.UserId)).ToList();
        await NotifyAsync(missing, companyId, EInvoiceSetupError, new Dictionary<string, string?> { ["reason"] = reason }, "/settings/e-invoicing", ct).ConfigureAwait(false);
    }

    /// <summary>A document was accepted, so the credentials work again: the open set-up notifications of the company are cleared (DT-189).</summary>
    public Task<int> ClearSetupErrorAsync(long companyId, CancellationToken ct) =>
        db.Set<Notification>().IgnoreQueryFilters(TenantFilter).Where(n => n.CompanyId == companyId && n.NotificationType == EInvoiceSetupError)
            .ExecuteDeleteAsync(ct);

    /// <summary>The caller's notifications in the current company (and those without a company), newest first; unread count.</summary>
    public async Task<NotificationPage> ListAsync(bool unreadOnly, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var companyId = ctx.CompanyId;
        if (companyId is long c && !ctx.IsSupportSession) await CollapseCredentialRejectionsAsync(me, c, ct).ConfigureAwait(false);
        var query = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == me && (n.CompanyId == null || n.CompanyId == companyId));
        var unread = await query.CountAsync(n => n.ReadAt == null, ct).ConfigureAwait(false);
        if (unreadOnly) query = query.Where(n => n.ReadAt == null);
        var rows = await query.OrderByDescending(n => n.Id).Take(100)
            .Select(n => new { n.PublicId, n.NotificationType, n.Message, n.Link, n.CompanyId, Company = n.Company != null ? n.Company.Name : null, n.CreatedAt, n.ReadAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var targets = await links.ResolveAsync(rows.Select(n => (n.NotificationType, n.Link)).ToList(), ct).ConfigureAwait(false);
        Dictionary<string, string>? setup = null;
        if (rows.Any(n => n.NotificationType == EInvoiceSetupError))
            setup = new(StringComparer.Ordinal) { ["count"] = NotificationTexts.Number(await CredentialRejectionsAsync(ct).ConfigureAwait(false)) };
        var companyToken = ctx.CompanyPublicId is Guid pid ? tokens.Encode<Company>(pid) : null;
        return new NotificationPage(rows.Select((n, i) =>
        {
            var text = NotificationTexts.Render(n.NotificationType, n.Message, ctx.Language, n.NotificationType == EInvoiceSetupError ? setup : null);
            return new NotificationResponse(tokens.Encode<Notification>(n.PublicId), n.NotificationType, text.Title, text.Message, text.Details, targets[i].Route, targets[i].Problem,
                n.CompanyId is null ? null : companyToken, n.Company, DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc),
                n.ReadAt is DateTime r ? DateTime.SpecifyKind(r, DateTimeKind.Utc) : null);
        }).ToList(), unread);
    }

    /// <summary>Documents of the current company refused because of the credentials and not resubmitted since (invoices, credit notes, AP/AR).</summary>
    private async Task<int> CredentialRejectionsAsync(CancellationToken ct) =>
        await db.Set<EInvoiceSubmission>().CountAsync(s => s.Status == EInvoiceCodes.Rejected && s.LastError!.StartsWith(EInvoiceOutcome.CredentialsRefusedText), ct).ConfigureAwait(false)
        + await db.Set<ApArEInvoiceSubmission>().CountAsync(s => s.Status == EInvoiceCodes.Rejected && s.LastError!.StartsWith(EInvoiceOutcome.CredentialsRefusedText), ct).ConfigureAwait(false);

    /// <summary>
    /// Older versions notified every document refused for the credentials: those notifications of the reader are replaced by the single
    /// set-up notification (unread when any of them was unread), the first time the reader's list is shown (DT-189).
    /// </summary>
    private async Task CollapseCredentialRejectionsAsync(long me, long companyId, CancellationToken ct)
    {
        var old = db.Set<Notification>().Where(n => n.UserId == me && n.CompanyId == companyId && n.NotificationType == EInvoiceRejected
            && n.Message.Contains(EInvoiceOutcome.CredentialsRefusedText));
        var found = await old.Select(n => new { n.ReadAt, n.Message }).OrderByDescending(n => n.ReadAt == null).ToListAsync(ct).ConfigureAwait(false);
        if (found.Count == 0) return;
        await db.InTransactionAsync(async t =>
        {
            await old.ExecuteDeleteAsync(t).ConfigureAwait(false);
            var reason = NotificationTexts.Parameters(EInvoiceRejected, found[0].Message)?.GetValueOrDefault("reason");
            var open = await db.Set<Notification>().AnyAsync(n => n.UserId == me && n.CompanyId == companyId && n.NotificationType == EInvoiceSetupError, t).ConfigureAwait(false);
            if (open) return;
            db.Set<Notification>().Add(new Notification
            {
                TenantId = ctx.TenantId ?? 0, UserId = me, CompanyId = companyId, NotificationType = EInvoiceSetupError, Link = "/settings/e-invoicing",
                Message = Message(new Dictionary<string, string?> { ["reason"] = reason }),
                ReadAt = found.Any(n => n.ReadAt == null) ? null : clock.GetUtcNow().UtcDateTime,
            });
            await db.SaveChangesAsync(t).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public async Task MarkReadAsync(string token, CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var pid = tokens.Decode<Notification>(token) ?? throw Fail.NotFound();
        var n = await db.Set<Notification>().SingleOrDefaultAsync(x => x.PublicId == pid && x.UserId == me, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        n.ReadAt ??= clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var me = ctx.UserId ?? throw Fail.Forbidden();
        var companyId = ctx.CompanyId;
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.Set<Notification>().Where(n => n.UserId == me && n.ReadAt == null && (n.CompanyId == null || n.CompanyId == companyId))
            .ExecuteUpdateAsync(u => u.SetProperty(n => n.ReadAt, now), ct).ConfigureAwait(false);
    }
}
