using System.Reflection;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Support access on the tenant side (5.5, BR-014, tenant.manage): the Owner sees the provider's requests and approves a time-limited window
/// (at most 7 days, default 24 hours from now) or rejects; an approved window can be ended early. Every request made inside a window is
/// written to the tenant's audit log (support session, read-only — D-13).
/// </summary>
public sealed class SupportAccessService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, TimeProvider clock)
{
    public const int MaxWindowDays = 7;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<SupportAccessResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<SupportAccessGrant>().AsNoTracking().OrderByDescending(g => g.Id).Take(100)
            .Select(g => new { G = g, Requester = db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == g.RequestedBy).Select(u => u.FullName).FirstOrDefault(), Decider = db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == g.ApprovedBy).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct).ConfigureAwait(false);
        var tenant = await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == ctx.TenantId).Select(t => t.Name).SingleAsync(ct).ConfigureAwait(false);
        var now = Now;
        return rows.Select(x => new SupportAccessResponse(support.Token<SupportAccessGrant>(x.G.PublicId), tenant, x.Requester ?? string.Empty, x.G.Reason,
            x.G.Status == LifecycleCodes.Approved && x.G.EndsAt <= now ? LifecycleCodes.Expired : x.G.Status,
            x.G.StartsAt is DateTime s ? DateTime.SpecifyKind(s, DateTimeKind.Utc) : null, x.G.EndsAt is DateTime e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null,
            x.Decider, DateTime.SpecifyKind(x.G.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.G.RowVersion))).ToList();
    }

    public async Task<IReadOnlyList<SupportAccessResponse>> ApproveAsync(string token, SupportApprovalRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var g = await support.FindAsync<SupportAccessGrant>(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Requested) throw Fail.Rule(ErrorCodes.SupportNotRequested, g.Reason);
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
        return await ListAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SupportAccessResponse>> RejectAsync(string token, string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var g = await support.FindAsync<SupportAccessGrant>(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Requested) throw Fail.Rule(ErrorCodes.SupportNotRequested, g.Reason);
        g.Status = LifecycleCodes.Rejected;
        g.ApprovedBy = ctx.UserId;
        changes.SetAction(g, "Reject", why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Ends an approved window now (the Owner can withdraw access at any time).</summary>
    public async Task<IReadOnlyList<SupportAccessResponse>> EndAsync(string token, CancellationToken ct)
    {
        var g = await support.FindAsync<SupportAccessGrant>(token, ct).ConfigureAwait(false);
        if (g.Status != LifecycleCodes.Approved || g.EndsAt <= Now) throw Fail.Rule(ErrorCodes.SupportNotRequested, g.Reason);
        g.Status = LifecycleCodes.Expired;
        g.EndsAt = Now > g.StartsAt ? Now : g.StartsAt!.Value.AddSeconds(1);
        changes.SetAction(g, "End");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ListAsync(ct).ConfigureAwait(false);
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

    private async Task<List<(long Id, string Email, string Name)>> OwnersAsync(long tenantId, CancellationToken ct) =>
        (await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId && !x.IsDeleted && x.User.Status == "Active" && x.Role.IsPreset && x.Role.Name == PresetRoles.Owner)
            .Select(x => new { x.UserId, x.User.Email, x.User.FullName }).Distinct().ToListAsync(ct).ConfigureAwait(false))
        .Select(x => (x.UserId, x.Email, x.FullName)).ToList();

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
            foreach (var type in typeof(AuditableEntity).Assembly.GetTypes().Where(x => typeof(AuditableEntity).IsAssignableFrom(x) && x != typeof(Tenant) && !x.IsAbstract && x.IsClass
                         && (typeof(ITenantScoped).IsAssignableFrom(x) || typeof(INullableTenantScoped).IsAssignableFrom(x))))
                await ((Task)SoftDeleteMethod.MakeGenericMethod(type).Invoke(this, [t.Id, system, now, c])!).ConfigureAwait(false);
            t.Status = LifecycleCodes.Deleted;
            changes.SetAction(t, "Delete", RetentionReason);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        var encoded = System.Net.WebUtility.HtmlEncode(tenantName);
        foreach (var (_, address, name) in owners)
        {
            await email.SendAsync(new EmailMessage(address, "Your data has been deleted / تم حذف بياناتكم",
                $"<p>Dear {System.Net.WebUtility.HtmlEncode(name)}, the retention period of {encoded} has ended; its data is no longer accessible.</p><p dir=\"rtl\">انتهت فترة الاحتفاظ ببيانات {encoded} ولم تعد متاحة.</p>",
                $"The retention period of {tenantName} has ended; its data is no longer accessible. / انتهت فترة الاحتفاظ ببيانات {tenantName} ولم تعد متاحة.", "TenantDeleted"), ct).ConfigureAwait(false);
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
                .SetProperty(x => x.NormalizedEmail, x => "DELETED-" + x.PublicId + "@ANONYMISED.INVALID")
                .SetProperty(x => x.PasswordHash, string.Empty).SetProperty(x => x.SecurityStamp, x => "anonymised-" + x.PublicId).SetProperty(x => x.TwoFactorSecret, (byte[]?)null)
                .SetProperty(x => x.Status, "Archived")
                .SetProperty(x => x.UpdatedAt, now), ct).ConfigureAwait(false);
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
