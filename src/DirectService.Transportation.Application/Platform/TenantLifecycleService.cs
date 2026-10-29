using System.Reflection;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Common;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DirectService.Platform.Application;
using DirectService.Platform.Application.Notifications;
using DirectService.Platform.Application.Email;
using DirectService.Platform.Application.Billing;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Support access on the tenant side (5.5, BR-014, tenant.manage): the Owner sees the provider's requests and approves a time-limited window
/// (at most 7 days, default 24 hours from now) or rejects; an approved window can be ended early. Every request made inside a window is
/// written to the tenant's audit log (support session, read-only — D-13). The staff member who asked is emailed the decision (DT-215).
/// Every query here names the signed-in tenant explicitly besides the tenant filter (owner testing 19 Oct 2026, DT-213: a nested
/// IgnoreQueryFilters had switched the filter off for the whole list query, so another tenant's requests were listed).
/// </summary>
public sealed partial class SupportAccessService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, TimeProvider clock,
    IEmailSender email, IOptions<AppOptions> app, Microsoft.Extensions.Logging.ILogger<SupportAccessService> log, IRealtimeNotifier live, DataScope scope)
{
    public const int MaxWindowDays = 7;

    /// <summary>Audit action of a refusal; its reason is the audit row's "_reason".</summary>
    public const string RejectAction = "Reject";

    public const string GrantEntity = "CORE.SupportAccessGrant";

    /// <summary>Audit actions of a window that closed: ended early by the subscriber, or expired at its end time (DT-220).</summary>
    public const string EndAction = "End";

    public const string ExpireAction = "Expire";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private long TenantId => ctx.TenantId ?? throw Fail.Forbidden();

    /// <summary>The signed-in tenant's requests only — never a query with ignored filters (see the class remarks).</summary>
    private IQueryable<SupportAccessGrant> Mine => db.Set<SupportAccessGrant>().Where(g => g.TenantId == TenantId);

    public async Task<IReadOnlyList<SupportAccessResponse>> ListAsync(CancellationToken ct)
    {
        var tenantId = TenantId;
        var grants = await db.Set<SupportAccessGrant>().AsNoTracking().Where(g => g.TenantId == tenantId).OrderByDescending(g => g.Id).Take(100)
            .ToListAsync(ct).ConfigureAwait(false);
        // Staff and Owner names: a separate query by id (staff users have no tenant, so the user filter is lifted — only here).
        var userIds = grants.Select(g => g.RequestedBy).Concat(grants.Where(g => g.ApprovedBy != null).Select(g => g.ApprovedBy!.Value)).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
        var refusals = await RefusalReasonsAsync(db, tenantId, grants.Where(g => g.Status == LifecycleCodes.Rejected).Select(g => g.Id).ToList(), ct).ConfigureAwait(false);
        var ends = await EndsAsync(db, tenantId, grants.Where(g => g.Status == LifecycleCodes.Expired).Select(g => g.Id).ToList(), ct).ConfigureAwait(false);
        var tenant = await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == tenantId).Select(t => t.Name).SingleAsync(ct).ConfigureAwait(false);
        var now = Now;
        return grants.Select(g => new SupportAccessResponse(support.Token<SupportAccessGrant>(g.PublicId), tenant, names.GetValueOrDefault(g.RequestedBy) ?? string.Empty, g.Reason,
            g.Status == LifecycleCodes.Approved && g.EndsAt <= now ? LifecycleCodes.Expired : g.Status,
            g.StartsAt is DateTime s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null, g.EndsAt is DateTime e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
            g.ApprovedBy is long by ? names.GetValueOrDefault(by) : null, DateTime.SpecifyKind(g.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(g.RowVersion),
            refusals.GetValueOrDefault(g.Id), EndedBy: ends.GetValueOrDefault(g.Id))).ToList();
    }

    /// <summary>The Owner's reason of each refused request (from its "Reject" audit row; no schema change), by grant id.</summary>
    internal static async Task<Dictionary<long, string>> RefusalReasonsAsync(IAppDbContext db, long? tenantId, IReadOnlyCollection<long> grantIds, CancellationToken ct)
    {
        if (grantIds.Count == 0) return [];
        var rows = await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.EntityType == GrantEntity && a.Action == RejectAction && a.EntityId != null && grantIds.Contains(a.EntityId.Value)
                && (tenantId == null || a.TenantId == tenantId))
            .OrderBy(a => a.Id).Select(a => new { Id = a.EntityId!.Value, Reason = SqlJson.Value(a.NewValues, "$._reason") })
            .ToListAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<long, string>();
        foreach (var r in rows.Where(r => !string.IsNullOrEmpty(r.Reason))) result[r.Id] = r.Reason!;
        return result;
    }

    /// <summary>
    /// The request named by the token, in the signed-in tenant only. A token of another tenant's request, a malformed or an unknown token all
    /// answer the same specific 404 (SEC-22: nothing tells whether it exists elsewhere).
    /// </summary>
    private async Task<SupportAccessGrant> FindAsync(string token, CancellationToken ct)
    {
        var tenantId = TenantId;
        var publicId = support.Decode<SupportAccessGrant>(token);
        return (publicId is Guid pid ? await Mine.SingleOrDefaultAsync(g => g.PublicId == pid && g.TenantId == tenantId, ct).ConfigureAwait(false) : null)
            ?? throw new AppException(ErrorCodes.SupportRequestNotFound, 404);
    }

    public async Task<IReadOnlyList<SupportAccessResponse>> ApproveAsync(string token, SupportApprovalRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var g = await FindAsync(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Requested) throw Fail.Rule(ErrorCodes.SupportNotRequested);
        var starts = (r.StartsAt ?? Now).ToUniversalTime();
        // A length in hours (DT-157) is measured on the server's clock, never the browser's.
        var ends = (r.EndsAt ?? starts.AddHours(r.Hours is int h and > 0 ? h : 24)).ToUniversalTime();
        if (ends <= starts || ends - starts > TimeSpan.FromDays(MaxWindowDays) || ends <= Now) throw Fail.Rule(ErrorCodes.SupportWindow);
        g.Status = LifecycleCodes.Approved;
        g.ApprovedBy = ctx.UserId;
        g.StartsAt = starts;
        g.EndsAt = ends;
        changes.SetAction(g, "Approve", TextNormalizer.CleanText(r.Reason));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await TellStaffAsync(g, null, ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SupportAccessResponse>> RejectAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var g = await FindAsync(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Requested) throw Fail.Rule(ErrorCodes.SupportNotRequested);
        g.Status = LifecycleCodes.Rejected;
        g.ApprovedBy = ctx.UserId;
        changes.SetAction(g, RejectAction, why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await TellStaffAsync(g, why, ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Emails the decision to the staff member who asked (staff have no in-app notifications: the console has no bell). The decision is already
    /// saved; a failed email is logged and never undoes or hides it.
    /// </summary>
    private async Task TellStaffAsync(SupportAccessGrant g, string? refusal, CancellationToken ct)
    {
        try
        {
            var staff = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => u.Id == g.RequestedBy && u.IsPlatformStaff && !u.IsDeleted && u.Email != null)
                .Select(u => new { u.Email, u.FullName, u.LanguageCode }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (staff is null) return;
            var tenant = await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == g.TenantId).Select(t => t.Name).SingleAsync(ct).ConfigureAwait(false);
            var link = $"{app.Value.PublicBaseUrl.TrimEnd('/')}/console/support-access";
            await email.SendAsync(EmailTemplates.SupportDecision(staff.Email!, staff.FullName, tenant, ctx.UserName ?? string.Empty,
                g.StartsAt, g.EndsAt, refusal, link, staff.LanguageCode), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEmailFailed(log, g.Id, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The decision on support request {GrantId} was saved, but the email to the staff member failed.")]
    private static partial void LogEmailFailed(ILogger logger, long grantId, Exception exception);

    /// <summary>Ends an approved window now (the Owner can withdraw access at any time).</summary>
    public async Task<IReadOnlyList<SupportAccessResponse>> EndAsync(string token, CancellationToken ct)
    {
        var g = await FindAsync(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Approved || g.EndsAt <= Now) throw Fail.Rule(ErrorCodes.SupportNotOpen);
        g.Status = LifecycleCodes.Expired;
        g.EndsAt = Now > g.StartsAt ? Now : g.StartsAt!.Value.AddSeconds(1);
        changes.SetAction(g, EndAction);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await AnnounceEndAsync(g, ctx.UserName ?? string.Empty, ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes every approved window whose end time has passed (support window job, DT-220): status Expired, an "Expire" row in the tenant's
    /// audit log (system account), and the staff member's open pages and mailbox are told. Returns the number closed.
    /// </summary>
    public async Task<int> ExpireDueAsync(CancellationToken ct)
    {
        var now = Now;
        var due = await db.Set<SupportAccessGrant>().IgnoreQueryFilters().Where(g => !g.IsDeleted && g.Status == LifecycleCodes.Approved && g.EndsAt <= now)
            .OrderBy(g => g.Id).Take(100).ToListAsync(ct).ConfigureAwait(false);
        var system = await db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == TenantLifecycleService.SystemEmail).Select(u => (long?)u.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        foreach (var g in due)
        {
            using (scope.Use(g.TenantId, null, system))
            {
                g.Status = LifecycleCodes.Expired;
                changes.SetAction(g, ExpireAction);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            await AnnounceEndAsync(g, null, ct).ConfigureAwait(false);
        }
        return due.Count;
    }

    /// <summary>
    /// Tells the staff member a window ended: a live message to every open page (with the tenant's company tokens, so only pages of that
    /// tenant leave) and an email. The end is already saved; a failure to tell is logged and never undoes it.
    /// </summary>
    private async Task AnnounceEndAsync(SupportAccessGrant g, string? endedBy, CancellationToken ct)
    {
        try
        {
            var staff = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => u.Id == g.RequestedBy && u.IsPlatformStaff && !u.IsDeleted)
                .Select(u => new { u.PublicId, u.Email, u.FullName, u.LanguageCode }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (staff is null) return;
            var companies = await db.Set<Company>().AsNoTracking().IgnoreQueryFilters().Where(c => c.TenantId == g.TenantId && !c.IsDeleted)
                .Select(c => c.PublicId).ToListAsync(ct).ConfigureAwait(false);
            var at = DateTime.SpecifyKind(g.EndsAt ?? Now, DateTimeKind.Utc);
            var reason = endedBy is null ? ErrorCodes.SupportWindowExpired : ErrorCodes.SupportEndedBySubscriber;
            await live.SupportEndedAsync(staff.PublicId, new { reason, endedAt = at, endedBy, companies = companies.Select(c => support.Token<Company>(c)).ToList() }, ct)
                .ConfigureAwait(false);
            if (staff.Email is null) return;
            var tenant = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => t.Id == g.TenantId).Select(t => t.Name).SingleAsync(ct).ConfigureAwait(false);
            await email.SendAsync(EmailTemplates.SupportEnded(staff.Email, staff.FullName, tenant, endedBy, at,
                $"{app.Value.PublicBaseUrl.TrimEnd('/')}/console/support-access", staff.LanguageCode), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogEmailFailed(log, g.Id, ex);
        }
    }

    /// <summary>Why a staff member's support session for a tenant is closed (the request pipeline answers 403 with it), or null when there is no
    /// grant at all for that tenant (then the company stays "not found", nothing is revealed).</summary>
    public sealed record ClosedWindow(string Code, DateTime? At, string? By);

    public static async Task<ClosedWindow?> WhyClosedAsync(IAppDbContext db, long tenantId, long staffId, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var g = await db.Set<SupportAccessGrant>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && x.RequestedBy == staffId && !x.IsDeleted).OrderByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Status, x.StartsAt, x.EndsAt }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (g is null) return null;
        if (g.Status is LifecycleCodes.Approved or LifecycleCodes.Expired && g.EndsAt is DateTime ends && ends <= now)
        {
            var ended = (await EndsAsync(db, tenantId, [g.Id], ct).ConfigureAwait(false)).GetValueOrDefault(g.Id);
            var at = DateTime.SpecifyKind(ends, DateTimeKind.Utc);
            return ended is not null ? new ClosedWindow(ErrorCodes.SupportEndedBySubscriber, at, ended) : new ClosedWindow(ErrorCodes.SupportWindowExpired, at, null);
        }
        return new ClosedWindow(ErrorCodes.SupportNotApproved, null, null);
    }

    /// <summary>Who ended each window early (from its "End" audit row; a window that simply expired has none), by grant id.</summary>
    internal static async Task<Dictionary<long, string>> EndsAsync(IAppDbContext db, long? tenantId, IReadOnlyCollection<long> grantIds, CancellationToken ct)
    {
        if (grantIds.Count == 0) return [];
        var rows = await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.EntityType == GrantEntity && a.Action == EndAction && a.EntityId != null && grantIds.Contains(a.EntityId.Value)
                && (tenantId == null || a.TenantId == tenantId))
            .OrderBy(a => a.Id).Select(a => new { Id = a.EntityId!.Value, a.UserId }).ToListAsync(ct).ConfigureAwait(false);
        var users = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => users.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
            .ConfigureAwait(false);
        var result = new Dictionary<long, string>();
        foreach (var r in rows) result[r.Id] = r.UserId is long id ? names.GetValueOrDefault(id) ?? string.Empty : string.Empty;
        return result;
    }

    /// <summary>Read-only permissions of a support session (G-09: all view permissions of the company, nothing that creates or changes).</summary>
    public static readonly IReadOnlySet<string> SessionPermissions = new HashSet<string>(StringComparer.Ordinal)
    {
        PermissionKeys.TripsView, PermissionKeys.InvView, PermissionKeys.DrvView, PermissionKeys.JeView, PermissionKeys.AcctView, PermissionKeys.ReportsView,
    };
}

/// <summary>
/// The tenant lifecycle (5.2, G-07, G-18), run by a daily job as the platform's system account: an unpaid trial ends in Suspended (D-12); an
/// active period that ends is renewed by charging the saved payment method — a failure starts the 7-day grace (PastDue) and tells the Owner;
/// the grace ends in Suspended; 30 days suspended end in Cancelled with 90 days of retention; at the end of retention every row of the tenant is
/// soft-deleted (reason "Retention period ended") and personal data is anonymised in place (SEC-19), the tenant is marked Deleted and the
/// Owner receives a confirmation. The suspension date is kept in GraceEndsAt (DT-125).
/// </summary>
public sealed partial class TenantLifecycleService(IAppDbContext db, DataScope scope, BillingService billing, IPaymentGateway gateway, NotificationService notifications,
    IEmailSender email, IChangeContext changes, IOptions<PlatformOptions> options, TimeProvider clock, IPlatformLock? locks = null,
    Microsoft.Extensions.Logging.ILogger<TenantLifecycleService>? log = null)
{
    public const string SystemEmail = "system@platform.invalid";
    public const string RetentionReason = "Retention period ended";

    private PlatformOptions O => options.Value;

    /// <summary>The platform's system account (DeletedBy of system jobs, open issue B1): created once, disabled, no password.</summary>
    public async Task<long> SystemUserAsync(CancellationToken ct)
    {
        var id = await db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == SystemEmail).Select(u => (long?)u.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        if (id is long existing) return existing;
        var user = new User
        {
            Email = SystemEmail, NormalizedEmail = SystemEmail.ToUpperInvariant(), FullName = "System", IsPlatformStaff = true, Status = "Disabled", PasswordHash = string.Empty,
            SecurityStamp = Guid.NewGuid().ToString("N"), EmailConfirmedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Set<User>().Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return user.Id;
    }

    /// <summary>
    /// One pass over all tenants; returns the number of state changes. One instance runs at a time (platform lock); each tenant is handled on
    /// its own, so a failure (e.g. the gateway is down) is logged and the next tenant still runs (review F5).
    /// </summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var held = locks is null ? null : await locks.TryAcquireAsync("tenant-lifecycle", ct).ConfigureAwait(false);
        if (locks is not null && held is null) return 0;
        var system = await SystemUserAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.Set<Tenant>().IgnoreQueryFilters().Where(t => !t.IsDeleted && (
                (t.Status == LifecycleCodes.Trial && t.TrialEndsAt < now)
                || (t.Status == LifecycleCodes.Active && t.CurrentPeriodEnd < now)
                || (t.Status == LifecycleCodes.PastDue && t.GraceEndsAt < now)
                || (t.Status == LifecycleCodes.Suspended && t.GraceEndsAt < now.AddDays(-O.SuspendedDaysBeforeCancel))
                || (t.Status == LifecycleCodes.Cancelled && t.RetentionEndsAt < now)))
            .Select(t => t.Id).ToListAsync(ct).ConfigureAwait(false);
        var count = 0;
        foreach (var id in due)
        {
            try
            {
                await StepAsync(id, system, now, ct).ConfigureAwait(false);
                count++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (log is not null) LogTenantFailed(log, id, ex);
            }
        }
        return count;
    }

    [Microsoft.Extensions.Logging.LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Tenant lifecycle step failed for tenant {TenantId}.")]
    private static partial void LogTenantFailed(Microsoft.Extensions.Logging.ILogger logger, long tenantId, Exception exception);

    private async Task StepAsync(long id, long system, DateTime now, CancellationToken ct)
    {
        {
            db.ChangeTracker.Clear();
            scope.Set(id, null, system);
            var t = await db.Set<Tenant>().IgnoreQueryFilters().Include(x => x.Plan).SingleAsync(x => x.Id == id, ct).ConfigureAwait(false);
            switch (t.Status)
            {
                case LifecycleCodes.Trial:
                    t.Status = LifecycleCodes.Suspended;
                    t.GraceEndsAt = t.TrialEndsAt;
                    changes.SetAction(t, "TrialEnded");
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    break;
                case LifecycleCodes.Active:
                    await RenewAsync(t, now, ct).ConfigureAwait(false);
                    break;
                case LifecycleCodes.PastDue:
                    t.Status = LifecycleCodes.Suspended;
                    changes.SetAction(t, "GraceEnded");
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    break;
                case LifecycleCodes.Suspended:
                    t.Status = LifecycleCodes.Cancelled;
                    t.CancelledAt = now;
                    t.RetentionEndsAt = now.AddDays(O.RetentionDays);
                    changes.SetAction(t, "Cancel", "Suspended for " + O.SuspendedDaysBeforeCancel + " days");
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    break;
                default:
                    await DeleteTenantAsync(t, system, now, ct).ConfigureAwait(false);
                    break;
            }
        }
    }

    /// <summary>
    /// Renewal: one invoice per period — a run that stopped after creating or charging it reuses it (a renewal invoice has no gateway
    /// reference until its charge is stored; a Due invoice with a reference is a checkout), and the charge is keyed by the invoice number so
    /// the gateway never takes the money twice (review F5, R3). While the Owner's checkout of the last day is open, the renewal waits; an
    /// older unpaid checkout is replaced by the renewal.
    /// </summary>
    private async Task RenewAsync(Tenant t, DateTime now, CancellationToken ct)
    {
        var start = DateOnly.FromDateTime(t.CurrentPeriodEnd!.Value).AddDays(1);
        var end = DateOnly.FromDateTime(BillingService.NextPeriodEnd(t.CurrentPeriodEnd.Value, t.Plan.BillingCycle));
        var open = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters()
            .Where(i => i.TenantId == t.Id && !i.IsDeleted && i.Status == LifecycleCodes.Due && i.GatewayReference != null).ToListAsync(ct).ConfigureAwait(false);
        if (open.Any(i => i.CreatedAt > now.AddDays(-1))) return;
        foreach (var stale in open)
        {
            stale.DeleteReason = "Replaced by the renewal";
            db.Set<SubscriptionInvoice>().Remove(stale);
        }
        if (open.Count > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var invoice = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters()
                .Where(i => i.TenantId == t.Id && !i.IsDeleted && i.PeriodStart == start && i.PeriodEnd == end && i.Status == LifecycleCodes.Due && i.GatewayReference == null)
                .OrderBy(i => i.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? await db.InTransactionAsync(c => billing.CreateInvoiceAsync(t, t.Plan.Price, start, end, c), ct).ConfigureAwait(false);
        var result = invoice.Total == 0
            ? new GatewayEvent("free-" + invoice.Number, true, null, new Dictionary<string, string>(), 0m, O.Currency)
            : await gateway.ChargeAsync(t.PublicId.ToString("N"), invoice.Number, invoice.Total, O.Currency, ct).ConfigureAwait(false);
        if (result.Succeeded)
        {
            await db.InTransactionAsync(c => billing.ApplyPaymentAsync(invoice, result.Reference, null, InvoiceKinds.Period, c), ct).ConfigureAwait(false);
            return;
        }
        invoice.Status = LifecycleCodes.Failed;
        invoice.GatewayReference = result.Reference;
        t.Status = LifecycleCodes.PastDue;
        t.GraceEndsAt = now.AddDays(O.GraceDays);
        changes.SetAction(t, "PaymentFailed", invoice.Number);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var owners = await OwnersAsync(t.Id, ct).ConfigureAwait(false);
        await notifications.NotifyAsync(owners.Select(o => o.Id), null, NotificationService.PaymentFailed,
            new Dictionary<string, string?> { ["days"] = NotificationTexts.Number(O.GraceDays) }, "/tenant/subscription", ct, t.Id).ConfigureAwait(false);
    }

    private async Task<List<(long Id, string? Email, string Name, string Language)>> OwnersAsync(long tenantId, CancellationToken ct) =>
        (await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.User.Status == "Active" && x.Role.IsPreset && x.Role.Name == PresetRoles.Owner)
            .Select(x => new { x.UserId, x.User.Email, x.User.FullName, x.User.LanguageCode }).Distinct().ToListAsync(ct).ConfigureAwait(false))
        .Select(x => (x.UserId, x.Email, x.FullName, x.LanguageCode)).ToList();

    private static readonly MethodInfo SoftDeleteMethod = typeof(TenantLifecycleService).GetMethod(nameof(SoftDeleteAllAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// End of retention (G-18, SEC-19): personal data anonymised, every tenant row soft-deleted, tenant Deleted; the confirmation to the Owners
    /// (addresses read before they are anonymised) is sent only after the deletion has committed (review F8).
    /// </summary>
    private async Task DeleteTenantAsync(Tenant t, long system, DateTime now, CancellationToken ct)
    {
        var owners = await OwnersAsync(t.Id, ct).ConfigureAwait(false);
        var tenantName = t.Name;
        await db.InTransactionAsync(async c =>
        {
            await AnonymiseAsync(t.Id, now, c).ConfigureAwait(false);
            // Every record type of the EF model (all modules; platform design §e, E-2), not an assembly scan.
            foreach (var type in EntityTypeMap.Records(db.Model).Values.Where(x => x != typeof(Tenant)
                         && (typeof(ITenantScoped).IsAssignableFrom(x) || typeof(INullableTenantScoped).IsAssignableFrom(x))))
                await ((Task)SoftDeleteMethod.MakeGenericMethod(type).Invoke(this, [t.Id, system, now, c])!).ConfigureAwait(false);
            t.Status = LifecycleCodes.Deleted;
            changes.SetAction(t, "Delete", RetentionReason);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        foreach (var (_, address, name, language) in owners)
        {
            if (address is null) continue;   // an Owner created with a username and no email (revision 6)
            await email.SendAsync(EmailTemplates.TenantDeleted(address, name, tenantName, language), ct).ConfigureAwait(false);
        }
    }

    private Task<int> SoftDeleteAllAsync<T>(long tenantId, long system, DateTime now, CancellationToken ct) where T : AuditableEntity =>
        db.Set<T>().IgnoreQueryFilters().Where(e => !e.IsDeleted && EF.Property<long?>(e, "TenantId") == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.IsDeleted, true).SetProperty(e => e.DeletedAt, now).SetProperty(e => e.DeletedBy, system)
                .SetProperty(e => e.DeleteReason, RetentionReason).SetProperty(e => e.UpdatedAt, now).SetProperty(e => e.UpdatedBy, system), ct);

    /// <summary>
    /// SEC-19: personal fields cleared in place (record kept): users (name, e-mail made unique and unusable, password, second factor) and their
    /// session addresses, invitation e-mails, drivers (name, national ID, licence, phones, address, bank details, notes, custom fields), contact
    /// details of customers, partners, AP/AR parties and the companies themselves (review F7). The append-only audit log is the documented
    /// exception: the database user cannot change it (FR-189), it is no longer reachable once the tenant is Deleted, and every view of it masks
    /// erased records (<see cref="ErasedRecords"/>).
    /// </summary>
    internal async Task AnonymiseAsync(long tenantId, DateTime now, CancellationToken ct)
    {
        string[] none = ["Tenant", "Company", "SoftDelete"];
        await db.Set<User>().IgnoreQueryFilters(none).Where(u => u.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.FullName, "Deleted user").SetProperty(x => x.Email, x => "deleted-" + x.PublicId + "@anonymised.invalid")
                .SetProperty(x => x.UserName, (string?)null).SetProperty(x => x.NormalizedUserName, (string?)null)
                .SetProperty(x => x.NormalizedEmail, x => "DELETED-" + x.PublicId + "@ANONYMISED.INVALID")
                .SetProperty(x => x.PasswordHash, string.Empty).SetProperty(x => x.SecurityStamp, x => "anonymised-" + x.PublicId).SetProperty(x => x.TwoFactorSecret, (byte[]?)null)
                .SetProperty(x => x.Status, "Archived")
                .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
        // System email (DT-251): the delivery log keeps no address (nor message) of the deleted tenant's emails.
        await db.Set<EmailOutbox>().Where(e => e.TenantId == tenantId && e.Status != EmailStatus.Pending && e.Status != EmailStatus.Sending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Recipient, "(removed)").SetProperty(x => x.PayloadEncrypted, (byte[]?)null), ct).ConfigureAwait(false);
        await db.Set<Driver>().IgnoreQueryFilters(none).Where(d => d.TenantId == tenantId)
            // Placeholders from the public id: internal ids never become data (review R11).
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.FullName, "Deleted driver").SetProperty(x => x.NationalId, x => ("X" + x.PublicId.ToString()).Substring(0, 9))
                .SetProperty(x => x.LicenceNo, x => ("X" + x.PublicId.ToString()).Substring(0, 9)).SetProperty(x => x.Notes, (string?)null).SetProperty(x => x.CustomValues, (string?)null)
                .SetProperty(x => x.Phone1, "0000000000").SetProperty(x => x.Phone2, (string?)null).SetProperty(x => x.Address, (string?)null)
                .SetProperty(x => x.Iban, (string?)null).SetProperty(x => x.CliqAlias, (string?)null).SetProperty(x => x.BankName, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Customer>().IgnoreQueryFilters(none).Where(c => c.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactPerson, (string?)null).SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.Email, (string?)null)
                .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
        await db.Set<Partner>().IgnoreQueryFilters(none).Where(p => p.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ContactPerson, (string?)null).SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Domain.Acc.ApArParty>().IgnoreQueryFilters(none).Where(p => p.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.Address, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Company>().IgnoreQueryFilters(none).Where(c => c.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Phone, (string?)null).SetProperty(x => x.Email, (string?)null).SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<Invitation>().IgnoreQueryFilters(none).Where(i => i.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Email, x => "deleted-" + x.PublicId + "@anonymised.invalid").SetProperty(x => x.UpdatedAt, now), ct)
            .ConfigureAwait(false);
        await db.Set<UserSession>().IgnoreQueryFilters(none).Where(s => s.TenantId == tenantId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IpAddress, (string?)null).SetProperty(x => x.UserAgent, (string?)null), ct)
            .ConfigureAwait(false);
    }
}
