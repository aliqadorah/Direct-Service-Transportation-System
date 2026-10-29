using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static DirectService.SharedKernel.DocumentPrinter;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The tenant's subscription (5.3, tenant.manage): plan, status, period, usage against limits and the plans on offer; subscription invoices
/// and their printed tax invoice; checkout through the gateway's payment page — a full period from Trial, PastDue or Suspended, or an
/// immediate pro-rated upgrade while Active; downgrades refused while usage exceeds the target's limits (BR-012) and otherwise effective at
/// once on limits with the new price from the next renewal (DT-123); cancellation by the Owner starts the 90-day retention (5.2).
/// </summary>
public sealed class SubscriptionService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, BillingService billing, PlanLimits limits, IPaymentGateway gateway,
    DataScope scope, IChangeContext changes, IOptions<PlatformOptions> options, DocumentPrinter printer, TimeProvider clock)
{
    private long TenantId => ctx.TenantId ?? throw Fail.Forbidden();

    private Task<Tenant> TenantAsync(CancellationToken ct) => db.Set<Tenant>().Include(t => t.Plan).SingleAsync(t => t.Id == TenantId, ct);

    internal PlanResponse Plan(Plan p, int tenants = 0) => new(support.Token<Plan>(p.PublicId), p.Code, p.NameAr, p.NameEn, p.Price, p.BillingCycle, p.MaxCompanies,
        p.MaxActiveUsers, p.MaxTripsPerMonth, p.MaxStorageMb, p.IsActive, tenants, Convert.ToBase64String(p.RowVersion), options.Value.TaxRate);

    internal SubscriptionInvoiceResponse Invoice(SubscriptionInvoice i) => new(support.Token<SubscriptionInvoice>(i.PublicId), i.Number, i.PeriodStart, i.PeriodEnd, i.Amount,
        i.Tax, i.Total, i.Status, i.PaidAt is DateTime p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null, i.GatewayReference, DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc));

    public async Task<SubscriptionResponse> GetAsync(CancellationToken ct)
    {
        var t = await TenantAsync(ct).ConfigureAwait(false);
        var plans = await db.Set<Plan>().AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Price).ToListAsync(ct).ConfigureAwait(false);
        return new SubscriptionResponse(t.Status, Plan(t.Plan), t.BillingCycle, Utc(t.TrialEndsAt), Utc(t.CurrentPeriodEnd), Utc(t.GraceEndsAt), Utc(t.RetentionEndsAt),
            await limits.UsageAsync(t.Id, ct).ConfigureAwait(false), plans.Select(p => Plan(p)).ToList());
    }

    private static DateTime? Utc(DateTime? d) => d is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    public async Task<IReadOnlyList<SubscriptionInvoiceResponse>> InvoicesAsync(CancellationToken ct) =>
        (await db.Set<SubscriptionInvoice>().AsNoTracking().OrderByDescending(i => i.Id).ToListAsync(ct).ConfigureAwait(false)).Select(Invoice).ToList();

    private async Task<Plan> ActivePlanAsync(string? token, CancellationToken ct)
    {
        var p = await support.FindAsync<Plan>(token ?? throw Fail.Field("planId", ErrorCodes.ValidationRequired, new FieldRef("planId")), ct).ConfigureAwait(false);
        return p.IsActive ? p : throw Fail.Field("planId", ErrorCodes.ValidationFormat, new FieldRef("planId"));
    }

    /// <summary>Checkout (P-04, P-06): an invoice and the gateway's payment page.</summary>
    public async Task<CheckoutResponse> CheckoutAsync(CheckoutRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var plan = await ActivePlanAsync(r.PlanId, ct).ConfigureAwait(false);
        var t = await TenantAsync(ct).ConfigureAwait(false);
        if (t.Status is LifecycleCodes.Cancelled or LifecycleCodes.Deleted) throw Fail.Rule(ErrorCodes.SubscriptionClosed);
        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        // A new checkout replaces any unpaid one (due or failed), so an old payment page can never be paid later at an old price or period
        // (review F2, R4); a late payment of a replaced invoice is recorded, never applied.
        foreach (var stale in await db.Set<SubscriptionInvoice>().Where(i => i.TenantId == t.Id && (i.Status == LifecycleCodes.Due || i.Status == LifecycleCodes.Failed))
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            stale.DeleteReason = "Replaced by a new checkout";
            db.Set<SubscriptionInvoice>().Remove(stale);
        }
        SubscriptionInvoice invoice;
        string kind;
        if (t.Status == LifecycleCodes.Active && t.CurrentPeriodEnd is DateTime end && end > now)
        {
            kind = InvoiceKinds.Upgrade;
            // Upgrade now, pro-rated over the days left in the period (5.3).
            if (plan.Price <= t.Plan.Price || plan.BillingCycle != t.Plan.BillingCycle) throw Fail.Rule(ErrorCodes.SubscriptionNotUpgrade);
            var periodStart = t.Plan.BillingCycle == LifecycleCodes.Annual ? end.AddYears(-1) : end.AddMonths(-1);
            var total = (decimal)(end - periodStart).TotalDays;
            var left = (decimal)(end - now).TotalDays;
            var charge = total <= 0 ? plan.Price - t.Plan.Price : (plan.Price - t.Plan.Price) * Math.Min(1, left / total);
            invoice = await billing.CreateInvoiceAsync(t, charge, today, DateOnly.FromDateTime(end), ct).ConfigureAwait(false);
        }
        else
        {
            // A full period from today (Trial, PastDue, Suspended, or Active without a current period).
            kind = InvoiceKinds.Period;
            invoice = await billing.CreateInvoiceAsync(t, plan.Price, today, DateOnly.FromDateTime(BillingService.NextPeriodEnd(now, plan.BillingCycle)).AddDays(-1), ct)
                .ConfigureAwait(false);
        }
        var checkout = await billing.CheckoutAsync(invoice, t, plan, kind, ct).ConfigureAwait(false);
        return new CheckoutResponse(Invoice(invoice), checkout.PaymentUrl);
    }

    /// <summary>
    /// Gateway webhook (anonymous, signature-verified): a successful payment of the invoice's exact total in the platform currency applies its
    /// invoice and plan; a failed one, or one whose amount or currency differs (review F3), marks the invoice Failed. Unknown, ambiguous or
    /// repeated notifications change nothing.
    /// </summary>
    public async Task<bool> WebhookAsync(string body, string? signature, CancellationToken ct)
    {
        var e = gateway.VerifyWebhook(body, signature);
        if (e is null) return false;
        var matches = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters().Where(i => i.GatewayReference == e.Reference).OrderBy(i => i.IsDeleted).Take(2).ToListAsync(ct)
            .ConfigureAwait(false);
        if (matches.Count == 0 || (matches.Count > 1 && matches[1].IsDeleted == matches[0].IsDeleted)) return true;
        var invoice = matches[0];
        scope.Set(invoice.TenantId, null, null);
        if (invoice.IsDeleted)
        {
            // Money received for a replaced invoice: recorded for staff to refund, never applied (review R4).
            if (e.Succeeded && invoice.Status is LifecycleCodes.Due or LifecycleCodes.Failed)
            {
                invoice.Status = LifecycleCodes.Paid;
                invoice.PaidAt = clock.GetUtcNow().UtcDateTime;
                changes.SetAction(invoice, "PaidNotApplied", e.Reference + " · Invoice replaced");
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return true;
        }
        var mismatch = e.Succeeded && (e.Amount != invoice.Total || !string.Equals(e.Currency, options.Value.Currency, StringComparison.OrdinalIgnoreCase));
        if (!e.Succeeded || mismatch)
        {
            if (invoice.Status == LifecycleCodes.Due)
            {
                invoice.Status = LifecycleCodes.Failed;
                changes.SetAction(invoice, "PaymentFailed", mismatch ? $"Paid {e.Amount} {e.Currency} does not match the invoice" : e.Message);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            return true;
        }
        // The plan by its public id (codes can be reused after a plan is deleted, review R10).
        var plan = e.Metadata.TryGetValue("plan", out var planKey) && Guid.TryParseExact(planKey, "N", out var planId)
            ? await db.Set<Plan>().IgnoreQueryFilters().SingleOrDefaultAsync(p => p.PublicId == planId, ct).ConfigureAwait(false)
            : null;
        var kind = e.Metadata.TryGetValue("kind", out var k) && k == InvoiceKinds.Upgrade ? InvoiceKinds.Upgrade : InvoiceKinds.Period;
        await db.InTransactionAsync(c => billing.ApplyPaymentAsync(invoice, e.Reference, plan, kind, c), ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Downgrade (P-06, BR-012): only to a cheaper plan of the same billing cycle whose limits are all at most the current ones (review F1);
    /// refused with the exceeded limits listed while usage is above them; otherwise the plan changes now and is billed from the next renewal.
    /// </summary>
    public async Task<SubscriptionResponse> DowngradeAsync(PlanChangeRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var plan = await ActivePlanAsync(r.PlanId, ct).ConfigureAwait(false);
        var t = await TenantAsync(ct).ConfigureAwait(false);
        if (t.Status is LifecycleCodes.Cancelled or LifecycleCodes.Deleted) throw Fail.Rule(ErrorCodes.SubscriptionClosed);
        var lower = plan.BillingCycle == t.Plan.BillingCycle && plan.Price < t.Plan.Price && plan.MaxCompanies <= t.Plan.MaxCompanies
            && plan.MaxActiveUsers <= t.Plan.MaxActiveUsers && plan.MaxTripsPerMonth <= t.Plan.MaxTripsPerMonth && plan.MaxStorageMb <= t.Plan.MaxStorageMb;
        if (!lower && plan.Id != t.PlanId) throw Fail.Rule(ErrorCodes.SubscriptionNotDowngrade);
        var exceeded = await limits.ExceededAsync(t.Id, plan, ct).ConfigureAwait(false);
        if (exceeded.Count > 0)
            throw Fail.Rule(ErrorCodes.SubscriptionDowngradeExceeds, string.Join(", ", exceeded.Select(x => $"{x.Limit} {x.Usage}/{x.Max}")));
        t.PlanId = plan.Id;
        t.BillingCycle = plan.BillingCycle;
        changes.SetAction(t, "Downgrade", plan.Code);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Cancellation by the Owner (5.2): Cancelled, retention of 90 days for data export, then deletion by the lifecycle job.</summary>
    public async Task<SubscriptionResponse> CancelAsync(string? reason, CancellationToken ct)
    {
        var why = MasterIds.Reason(reason);
        var t = await TenantAsync(ct).ConfigureAwait(false);
        if (t.Status is LifecycleCodes.Cancelled or LifecycleCodes.Deleted) throw Fail.Rule(ErrorCodes.SubscriptionClosed);
        var now = clock.GetUtcNow().UtcDateTime;
        t.Status = LifecycleCodes.Cancelled;
        t.CancelledAt = now;
        t.RetentionEndsAt = now.AddDays(options.Value.RetentionDays);
        changes.SetAction(t, "Cancel", why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The provider's tax invoice for a subscription charge (P-05) as HTML or PDF.</summary>
    public async Task<GeneratedFile> PrintAsync(string token, string? format, CancellationToken ct)
    {
        var i = await support.FindAsync<SubscriptionInvoice>(token, ct).ConfigureAwait(false);
        var t = await db.Set<Tenant>().AsNoTracking().SingleAsync(x => x.Id == i.TenantId, ct).ConfigureAwait(false);
        var o = options.Value;
        var rate = o.TaxRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        string L(string ar, string en) => printer.L(ar, en);
        var body = $"<div class=\"hd\"><div><b style=\"font-size:13pt\">{E(o.SellerName)}</b><div>{L("الرقم الضريبي", "Tax no.")} {N(o.SellerTaxNumber)}</div></div></div>"
            + $"<h1>{E(L("فاتورة ضريبية — اشتراك", "Tax invoice — subscription"))}</h1><div class=\"meta\">{L("رقم", "No.")} {N(i.Number)} · {L("التاريخ", "Date")} {N(D(DateOnly.FromDateTime(i.CreatedAt)))}</div>"
            + $"<div class=\"fields\"><b>{E(L("العميل", "Customer"))}</b><div>{E(t.Name)}</div><b>{E(L("الفترة", "Period"))}</b><div>{N(D(i.PeriodStart) + " – " + D(i.PeriodEnd))}</div>"
            + $"<b>{E(L("الحالة", "Status"))}</b><div>{E(i.Status)}</div></div>"
            // The plan fee is tax-inclusive: the total is the fee; the amount before tax and the tax are derived from it (owner decision 13 Oct 2026).
            + $"<div class=\"totals\"><div><span>{E(L("المبلغ قبل الضريبة", "Amount before tax"))}</span>{N(M(i.Amount))}</div><div><span>{E(L("ضريبة المبيعات", "Sales tax"))} {N(rate)}%</span>{N(M(i.Tax))}</div>"
            + $"<div class=\"grand\"><span>{E(L("الإجمالي شامل الضريبة", "Total including tax"))}</span>{N(M(i.Total) + " " + o.Currency)}</div></div>"
            + $"<p>{E(L($"السعر شامل ضريبة المبيعات {rate}%", $"Price includes {rate}% sales tax"))}</p>";
        return await printer.OutputAsync(printer.Shell(i.Number, body, null), format, i.Number, ct).ConfigureAwait(false);
    }
}
