using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DirectService.Platform.Application.Billing;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// Usage against plan limits (5.3, P-07, BR-013): active non-demo companies (Q-06, G-11), active users, trips saved this calendar month,
/// storage of exports. Creating beyond a limit is refused with an upgrade message; viewing, printing and exporting never are.
/// </summary>
public sealed class PlanLimits(IAppDbContext db, IFileStore files, TimeProvider clock)
{
    private static readonly string[] AllFilters = ["Tenant", "Company"];

    public async Task<UsageResponse> UsageAsync(long tenantId, CancellationToken ct)
    {
        var plan = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Plan).SingleAsync(ct).ConfigureAwait(false);
        var demo = await db.Set<CompanySetting>().AsNoTracking().IgnoreQueryFilters(AllFilters)
            .Where(s => s.TenantId == tenantId && s.SettingKey == SettingKeys.IsDemo && s.SettingValue == "true").Select(s => s.CompanyId).ToListAsync(ct).ConfigureAwait(false);
        var companies = await db.Set<Company>().AsNoTracking().IgnoreQueryFilters(AllFilters).CountAsync(c => c.TenantId == tenantId && c.Status == "Active" && !demo.Contains(c.Id), ct)
            .ConfigureAwait(false);
        var users = await ActiveUsersAsync(tenantId, ct).ConfigureAwait(false);
        var trips = await TripsThisMonthAsync(tenantId, ct).ConfigureAwait(false);
        var bytes = await files.SizeAsync($"{ExportFiles.Root}{tenantId}/", ct).ConfigureAwait(false);
        return new UsageResponse(companies, plan.MaxCompanies, users, plan.MaxActiveUsers, trips, plan.MaxTripsPerMonth, Math.Round(bytes / 1048576m, 2), plan.MaxStorageMb);
    }

    private Task<int> ActiveUsersAsync(long tenantId, CancellationToken ct) =>
        db.Set<User>().AsNoTracking().IgnoreQueryFilters(AllFilters).CountAsync(u => u.TenantId == tenantId && u.Status == "Active" && !u.IsDeleted, ct);

    private Task<int> TripsThisMonthAsync(long tenantId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return CountTripsAsync(tenantId, start, ct);
    }

    /// <summary>
    /// Trips of the demo company count only beyond its sample trips (G-11): practising is free, running real work in it is not (review R7).
    /// </summary>
    private async Task<int> CountTripsAsync(long tenantId, DateTime start, CancellationToken ct)
    {
        var demo = db.Set<CompanySetting>().AsNoTracking().IgnoreQueryFilters(AllFilters)
            .Where(s => s.TenantId == tenantId && s.SettingKey == SettingKeys.IsDemo && s.SettingValue == "true").Select(s => s.CompanyId);
        var trips = db.Set<Trip>().AsNoTracking().IgnoreQueryFilters(AllFilters).Where(t => t.TenantId == tenantId && !t.IsDeleted && t.CreatedAt >= start);
        var real = await trips.CountAsync(t => !demo.Contains(t.CompanyId), ct).ConfigureAwait(false);
        var practice = await trips.CountAsync(t => demo.Contains(t.CompanyId), ct).ConfigureAwait(false);
        return real + Math.Max(0, practice - DemoCompanyService.SampleTrips);
    }

    /// <summary>One more active user (invitation accepted, user enabled) must fit MaxActiveUsers.</summary>
    public async Task EnsureUserAsync(long tenantId, CancellationToken ct)
    {
        var max = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Plan.MaxActiveUsers).SingleAsync(ct).ConfigureAwait(false);
        if (await ActiveUsersAsync(tenantId, ct).ConfigureAwait(false) >= max) throw Fail.Conflict(ErrorCodes.PlanLimitReached, max);
    }

    /// <summary><paramref name="adding"/> more trips this month must fit MaxTripsPerMonth ("trips saved per calendar month").</summary>
    public async Task EnsureTripsAsync(long tenantId, int adding, CancellationToken ct)
    {
        var max = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Plan.MaxTripsPerMonth).SingleAsync(ct).ConfigureAwait(false);
        if (await TripsThisMonthAsync(tenantId, ct).ConfigureAwait(false) + adding > max) throw Fail.Conflict(ErrorCodes.PlanLimitReached, max);
    }

    /// <summary>The limits the current usage exceeds on a target plan (BR-012).</summary>
    public async Task<IReadOnlyList<LimitExceeded>> ExceededAsync(long tenantId, Plan target, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        var u = await UsageAsync(tenantId, ct).ConfigureAwait(false);
        var list = new List<LimitExceeded>();
        if (u.Companies > target.MaxCompanies) list.Add(new("companies", u.Companies, target.MaxCompanies));
        if (u.ActiveUsers > target.MaxActiveUsers) list.Add(new("activeUsers", u.ActiveUsers, target.MaxActiveUsers));
        if (u.TripsThisMonth > target.MaxTripsPerMonth) list.Add(new("tripsPerMonth", u.TripsThisMonth, target.MaxTripsPerMonth));
        if (u.StorageMb > target.MaxStorageMb) list.Add(new("storageMb", u.StorageMb, target.MaxStorageMb));
        return list;
    }
}

/// <summary>
/// Subscription invoices (5.3, P-04, P-05): numbered platform-wide (SUB-), the plan fee tax-inclusive (net and tax derived from it at the
/// provider's rate, owner decision 13 Oct 2026), paid through the
/// gateway; a paid invoice starts or extends the tenant's period and restores full access at once (BR-011).
/// </summary>
public sealed class BillingService(IAppDbContext db, IPlatformNumbers numbers, IPaymentGateway gateway, IOptions<PlatformOptions> options, IChangeContext changes,
    TimeProvider clock)
{
    private PlatformOptions O => options.Value;

    /// <summary>
    /// Plan fees are tax-inclusive (owner decision 13 Oct 2026): the fee is what the tenant pays. Net and tax are derived backwards from
    /// it at <paramref name="rate"/> percent — net = round(gross ÷ (1 + rate)), tax = gross − net — so net + tax = gross exactly.
    /// </summary>
    public static (decimal Net, decimal Tax) Split(decimal gross, decimal rate, int decimals)
    {
        var total = Money.Round(Math.Max(0, gross), decimals);
        var net = Money.Round(total / (1m + rate / 100m), decimals);
        return (net, total - net);
    }

    /// <summary>An invoice whose total is <paramref name="amount"/> (a tax-inclusive fee or pro-rated part of one), with net and tax inside it.</summary>
    public async Task<SubscriptionInvoice> CreateInvoiceAsync(Tenant tenant, decimal amount, DateOnly start, DateOnly end, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var total = Money.Round(Math.Max(0, amount), O.CurrencyDecimals);
        var (net, tax) = Split(total, O.TaxRate, O.CurrencyDecimals);
        return await db.InTransactionAsync(async c =>
        {
            var invoice = new SubscriptionInvoice
            {
                TenantId = tenant.Id, Number = await numbers.NextSubscriptionInvoiceAsync(c).ConfigureAwait(false), PeriodStart = start, PeriodEnd = end < start ? start : end,
                Amount = net, Tax = tax, Total = total, Status = LifecycleCodes.Due,
            };
            db.Set<SubscriptionInvoice>().Add(invoice);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return invoice;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>The gateway's payment page for an invoice; the plan travels as metadata and the reference is kept on the invoice to match the webhook.</summary>
    public async Task<GatewayCheckout> CheckoutAsync(SubscriptionInvoice invoice, Tenant tenant, Plan plan, string kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(plan);
        var checkout = await gateway.CreateCheckoutAsync(invoice.Number, invoice.Total, O.Currency, tenant.Name,
            new Dictionary<string, string> { ["invoice"] = invoice.Number, ["plan"] = plan.PublicId.ToString("N"), ["kind"] = kind }, ct).ConfigureAwait(false);
        invoice.GatewayReference = checkout.Reference;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return checkout;
    }

    /// <summary>The next period after <paramref name="from"/> for a cycle.</summary>
    public static DateTime NextPeriodEnd(DateTime from, string cycle) => cycle == LifecycleCodes.Annual ? from.AddYears(1) : from.AddMonths(1);

    /// <summary>
    /// Marks an invoice paid and applies it: a full-period invoice activates the tenant (Active, period end moved on, grace cleared) on its
    /// plan; an upgrade invoice (pro-rated) switches the plan only while it still belongs to the tenant's current period and the tenant is Active
    /// (review F2). Only Due or Failed invoices are paid (a Credited one never is); a payment for a Cancelled or Deleted tenant, a stale upgrade,
    /// or a tenant suspended by platform staff is recorded as paid but not applied ("PaidNotApplied") for staff to refund or credit (review F9).
    /// Idempotent: a second notification of the same payment changes nothing.
    /// </summary>
    public async Task ApplyPaymentAsync(SubscriptionInvoice invoice, string reference, Plan? plan, string kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Status == LifecycleCodes.Credited && invoice.PaidAt is null)
        {
            // Money received for an invoice staff credited (a cancelled checkout, DT-183): recorded for staff to refund, never applied; the
            // invoice stays Credited. A repeated notification finds PaidAt set and changes nothing.
            invoice.PaidAt = clock.GetUtcNow().UtcDateTime;
            changes.SetAction(invoice, "PaidNotApplied", reference + " · Invoice credited");
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }
        if (invoice.Status is not (LifecycleCodes.Due or LifecycleCodes.Failed)) return;
        var now = clock.GetUtcNow().UtcDateTime;
        invoice.Status = LifecycleCodes.Paid;
        invoice.PaidAt = now;
        invoice.GatewayReference = reference;
        var tenant = await db.Set<Tenant>().IgnoreQueryFilters().Include(t => t.Plan).SingleAsync(t => t.Id == invoice.TenantId, ct).ConfigureAwait(false);
        var reason = await NotApplicableAsync(invoice, tenant, plan, kind, ct).ConfigureAwait(false);
        if (reason is not null)
        {
            changes.SetAction(invoice, "PaidNotApplied", reference + " · " + reason);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }
        changes.SetAction(invoice, "Paid", reference);
        if (plan is not null && plan.Id != tenant.PlanId)
        {
            tenant.PlanId = plan.Id;
            tenant.BillingCycle = plan.BillingCycle;
        }
        var periodEnd = invoice.PeriodEnd.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);
        if (tenant.CurrentPeriodEnd is null || periodEnd > tenant.CurrentPeriodEnd) tenant.CurrentPeriodEnd = periodEnd;
        if (tenant.Status is LifecycleCodes.Trial or LifecycleCodes.PastDue or LifecycleCodes.Suspended)
        {
            tenant.Status = LifecycleCodes.Active;
            tenant.GraceEndsAt = null;
            changes.SetAction(tenant, "Activate", invoice.Number);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Why a payment cannot be applied to the tenant, or null when it can.</summary>
    private async Task<string?> NotApplicableAsync(SubscriptionInvoice invoice, Tenant tenant, Plan? plan, string kind, CancellationToken ct)
    {
        if (tenant.Status is LifecycleCodes.Cancelled or LifecycleCodes.Deleted) return "Tenant " + tenant.Status;
        if (kind == InvoiceKinds.Upgrade)
        {
            var currentEnd = tenant.CurrentPeriodEnd is DateTime e ? DateOnly.FromDateTime(e) : (DateOnly?)null;
            if (tenant.Status != LifecycleCodes.Active || currentEnd != invoice.PeriodEnd || plan is null || plan.BillingCycle != tenant.Plan.BillingCycle
                || plan.Price <= tenant.Plan.Price)
                return "Upgrade no longer matches the current period";
        }
        if (tenant.Status == LifecycleCodes.Suspended && await SuspendedByStaffAsync(tenant.Id, ct).ConfigureAwait(false)) return "Suspended by platform staff";
        return null;
    }

    /// <summary>Status changes of a tenant in its audit trail; the latest tells whether a suspension was a staff decision (console "Suspend").</summary>
    private static readonly string[] StatusActions = ["Suspend", "Reactivate", "Activate", "TrialEnded", "GraceEnded", "PaymentFailed", "ExtendTrial", "Cancel"];

    private async Task<bool> SuspendedByStaffAsync(long tenantId, CancellationToken ct) =>
        await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.EntityType == "CORE.Tenant" && a.EntityId == tenantId && StatusActions.Contains(a.Action))
            .OrderByDescending(a => a.Id).Select(a => a.Action).FirstOrDefaultAsync(ct).ConfigureAwait(false) == "Suspend";
}
