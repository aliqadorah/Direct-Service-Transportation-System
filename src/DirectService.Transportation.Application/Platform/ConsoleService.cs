using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Application.Platform;

/// <summary>
/// The provider console (5.5, D-13: every platform-staff user): tenants with status, plan, usage and dates; plans (create, edit, retire,
/// delete when unused — BR-040); account actions — extend a trial, apply a credit to an invoice (D-11), suspend, reactivate (BR-011) — each
/// audited in the tenant's log; support-access requests (approved by the tenant's Owner, BR-014); announcements; platform health. Staff never
/// read tenant business data here.
/// </summary>
public sealed class ConsoleService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, PlanLimits limits, SubscriptionService subscriptions,
    NotificationService notifications, DataScope scope, IChangeContext changes, IOptions<PlatformOptions> options, TimeProvider clock, AuditTrail audit, IPlatformLock locks)
{
    private static readonly DateTime Started = DateTime.UtcNow;

    private long StaffId => ctx.IsPlatformStaff && ctx.UserId is long id ? id : throw Fail.NotFound();

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static DateTime? Utc(DateTime? d) => d is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    // ============================================================ plans (P-03)

    public async Task<IReadOnlyList<PlanResponse>> PlansAsync(CancellationToken ct)
    {
        _ = StaffId;
        var plans = await db.Set<Plan>().AsNoTracking().OrderBy(p => p.Price).ToListAsync(ct).ConfigureAwait(false);
        var used = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => !t.IsDeleted).GroupBy(t => t.PlanId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct).ConfigureAwait(false);
        return plans.Select(p => subscriptions.Plan(p, used.GetValueOrDefault(p.Id))).ToList();
    }

    public async Task<PlanResponse> SavePlanAsync(string? token, PlanRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        _ = StaffId;
        var code = TextNormalizer.CleanCode(r.Code)?.ToUpperInvariant();
        var nameAr = TextNormalizer.CleanText(r.NameAr);
        var nameEn = TextNormalizer.CleanText(r.NameEn);
        var cycle = r.BillingCycle is "Annual" ? LifecycleCodes.Annual : r.BillingCycle is null or "Monthly" ? LifecycleCodes.Monthly : throw Fail.Field("billingCycle", ErrorCodes.ValidationFormat, new FieldRef("billingCycle"));
        new Validator().Require("code", code).MaxLength("code", code, 30).Require("nameAr", nameAr).MaxLength("nameAr", nameAr, 100).Require("nameEn", nameEn).MaxLength("nameEn", nameEn, 100)
            .Check(r.Price is >= 0 && Money.IsValidAmount(r.Price.Value), "price", ErrorCodes.ValidationRange)
            .Check(r.MaxCompanies is > 0, "maxCompanies", ErrorCodes.ValidationRange).Check(r.MaxActiveUsers is > 0, "maxActiveUsers", ErrorCodes.ValidationRange)
            .Check(r.MaxTripsPerMonth is > 0, "maxTripsPerMonth", ErrorCodes.ValidationRange).Check(r.MaxStorageMb is > 0, "maxStorageMb", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        Plan plan;
        if (token is null)
        {
            plan = new Plan();
            db.Set<Plan>().Add(plan);
        }
        else
        {
            plan = await support.FindAsync<Plan>(token, ct).ConfigureAwait(false);
            db.ExpectRowVersion(plan, r.RowVersion);
        }
        // The code is unique among all plans that are not deleted, retired ones included (UX_Plans_Code, review F11).
        if (await db.Set<Plan>().AnyAsync(p => p.Id != plan.Id && p.Code == code, ct).ConfigureAwait(false))
            throw Fail.Field("code", ErrorCodes.DuplicateKey, code);
        // Changing a plan never changes invoices already issued (design).
        plan.Code = code!;
        plan.NameAr = nameAr!;
        plan.NameEn = nameEn!;
        plan.Price = r.Price!.Value;
        plan.BillingCycle = cycle;
        plan.MaxCompanies = r.MaxCompanies!.Value;
        plan.MaxActiveUsers = r.MaxActiveUsers!.Value;
        plan.MaxTripsPerMonth = r.MaxTripsPerMonth!.Value;
        plan.MaxStorageMb = r.MaxStorageMb!.Value;
        plan.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await PlansAsync(ct).ConfigureAwait(false)).Single(p => p.Code == plan.Code && p.Id == support.Token<Plan>(plan.PublicId));
    }

    /// <summary>A plan used by any tenant cannot be deleted; retire it (IsActive = 0) instead (BR-040).</summary>
    public async Task DeletePlanAsync(string token, string? reason, CancellationToken ct)
    {
        _ = StaffId;
        var why = MasterIds.Reason(reason);
        var plan = await support.FindAsync<Plan>(token, ct).ConfigureAwait(false);
        var used = await db.Set<Tenant>().IgnoreQueryFilters().CountAsync(t => t.PlanId == plan.Id && !t.IsDeleted, ct).ConfigureAwait(false);
        if (used > 0) throw Fail.Rule(ErrorCodes.RecordInUse, used);
        plan.DeleteReason = why;
        db.Set<Plan>().Remove(plan);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ============================================================ tenants (P-11, P-12)

    private async Task<ConsoleTenantResponse> TenantAsync(Tenant t, CancellationToken ct)
    {
        var owner = await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(r => r.TenantId == t.Id && !r.IsDeleted && r.Role.IsPreset && r.Role.Name == PresetRoles.Owner && r.User.Status == "Active")
            .Select(r => r.User.Email).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new ConsoleTenantResponse(support.Token<Tenant>(t.PublicId), t.Name, t.Status, new PlanSummary(support.Token<Plan>(t.Plan.PublicId), t.Plan.Code, t.Plan.NameEn),
            t.BillingCycle, await limits.UsageAsync(t.Id, ct).ConfigureAwait(false), DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc), Utc(t.TrialEndsAt), Utc(t.CurrentPeriodEnd),
            Utc(t.GraceEndsAt), Utc(t.CancelledAt), Utc(t.RetentionEndsAt), owner, Convert.ToBase64String(t.RowVersion));
    }

    /// <summary>
    /// The provider console's search (owner feedback 13 Oct 2026): provider data only — tenants by name or a user's e-mail, subscriptions by
    /// plan code or name, subscription invoices by number or payment reference, platform staff by name or e-mail; at most 5 hits per kind.
    /// Invoices and subscriptions point to their tenant. Company data (trips, invoices, drivers) is never searched here.
    /// </summary>
    public async Task<SearchResponse> SearchAsync(string? q, CancellationToken ct)
    {
        _ = StaffId;
        var text = MasterDataSupport.SearchText(q);
        if (text is null || text.Length < 2) return new(q ?? "", []);
        const int max = 5;
        var hits = new List<SearchHit>();
        var tenants = db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => !t.IsDeleted);
        var byEmail = db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => !u.IsDeleted && u.TenantId != null && u.Email!.Contains(text)).Select(u => u.TenantId);
        foreach (var t in await tenants.Where(t => t.Name.Contains(text) || byEmail.Contains(t.Id)).OrderBy(t => t.Name).Take(max)
                     .Select(t => new { t.PublicId, t.Name, t.Status }).ToListAsync(ct).ConfigureAwait(false))
            hits.Add(new(ConsoleSearchKinds.Tenant, support.Token<Tenant>(t.PublicId), t.Name, t.Status));
        foreach (var t in await tenants.Where(t => t.Plan.Code.Contains(text) || t.Plan.NameEn.Contains(text) || t.Plan.NameAr.Contains(text)).OrderBy(t => t.Name).Take(max)
                     .Select(t => new { t.PublicId, t.Name, t.Status, t.Plan.Code }).ToListAsync(ct).ConfigureAwait(false))
            hits.Add(new(ConsoleSearchKinds.Subscription, support.Token<Tenant>(t.PublicId), t.Name, $"{t.Code} · {t.Status}"));
        foreach (var i in await db.Set<SubscriptionInvoice>().AsNoTracking().IgnoreQueryFilters()
                     .Where(i => !i.IsDeleted && (i.Number.Contains(text) || i.GatewayReference == text))
                     .Join(tenants, i => i.TenantId, t => t.Id, (i, t) => new { i.Number, i.Total, i.Status, t.PublicId, t.Name }).OrderByDescending(x => x.Number).Take(max)
                     .ToListAsync(ct).ConfigureAwait(false))
            hits.Add(new(ConsoleSearchKinds.Invoice, support.Token<Tenant>(i.PublicId), i.Number, $"{i.Name} · {Money.Format(i.Total)} · {i.Status}"));
        foreach (var u in await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => u.IsPlatformStaff && !u.IsDeleted && (u.FullName.Contains(text) || u.Email!.Contains(text)))
                     .OrderBy(u => u.FullName).Take(max).Select(u => new { u.PublicId, u.FullName, u.Email }).ToListAsync(ct).ConfigureAwait(false))
            hits.Add(new(ConsoleSearchKinds.Staff, support.Token<User>(u.PublicId), u.FullName, u.Email));
        return new(text, hits);
    }

    public async Task<PagedResult<ConsoleTenantResponse>> TenantsAsync(ConsoleTenantQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        _ = StaffId;
        var query = db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => !t.IsDeleted).Include(t => t.Plan).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Status)) query = query.Where(t => t.Status == q.Status);
        if (MasterDataSupport.SearchText(q.Q) is { } text)
        {
            var byEmail = db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => !u.IsDeleted && u.TenantId != null && u.Email!.Contains(text)).Select(u => u.TenantId);
            query = query.Where(t => t.Name.Contains(text) || byEmail.Contains(t.Id));
        }
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query.OrderByDescending(t => t.Id).Skip(paging.Skip).Take(paging.SafePageSize).ToListAsync(ct).ConfigureAwait(false);
        var items = new List<ConsoleTenantResponse>();
        foreach (var t in rows) items.Add(await TenantAsync(t, ct).ConfigureAwait(false));
        return new PagedResult<ConsoleTenantResponse>(items, paging.SafePage, paging.SafePageSize, total);
    }

    private async Task<Tenant> FindTenantAsync(string token, CancellationToken ct)
    {
        _ = StaffId;
        var pid = support.Decode<Tenant>(token) ?? throw Fail.NotFound();
        var t = await db.Set<Tenant>().IgnoreQueryFilters().Include(x => x.Plan).SingleOrDefaultAsync(x => x.PublicId == pid && !x.IsDeleted, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        // The tenant's audit log receives the action (P-12): the scope carries the tenant for the audit rows.
        scope.Set(t.Id, null, ctx.UserId, ctx.IpAddress);
        return t;
    }

    public async Task<ConsoleTenantDetails> TenantDetailsAsync(string token, CancellationToken ct)
    {
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        var invoices = await db.Set<SubscriptionInvoice>().AsNoTracking().IgnoreQueryFilters().Where(i => i.TenantId == t.Id && !i.IsDeleted).OrderByDescending(i => i.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        var grants = await SupportRowsAsync(db.Set<SupportAccessGrant>().AsNoTracking().IgnoreQueryFilters().Where(g => g.TenantId == t.Id && !g.IsDeleted), ct).ConfigureAwait(false);
        var (due, refunded) = await RefundStateAsync(t.Id, ct).ConfigureAwait(false);
        return new ConsoleTenantDetails(await TenantAsync(t, ct).ConfigureAwait(false), invoices.Select(subscriptions.Invoice).ToList(), grants, due, refunded);
    }

    /// <summary>Payments to refund and payments marked refunded across the platform, newest first (console health page, DT-184, DT-185).</summary>
    public async Task<RefundsResponse> RefundsAsync(CancellationToken ct)
    {
        _ = StaffId;
        var (due, refunded) = await RefundStateAsync(null, ct).ConfigureAwait(false);
        return new RefundsResponse(due, refunded);
    }

    /// <summary>
    /// Marks a payment to refund as refunded (owner request 14 Oct 2026, DT-185): the refund itself is made outside the platform; staff record
    /// its date and reference as a "Refunded" audit entry on the invoice, linked to the "PaidNotApplied" entry of the payment (no schema
    /// change). Refused when the invoice has no payment to refund or it is already marked; the date lies between the payment (Amman date) and
    /// today. One marking at a time platform-wide, so a payment is never marked twice.
    /// </summary>
    public async Task<RefundedResponse> MarkRefundedAsync(string invoiceToken, MarkRefundedRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        _ = StaffId;
        var pid = support.Decode<SubscriptionInvoice>(invoiceToken) ?? throw Fail.NotFound();
        var invoice = await db.Set<SubscriptionInvoice>().AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(i => i.PublicId == pid, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        await using var held = await locks.TryAcquireAsync("platform-refunds", ct).ConfigureAwait(false) ?? throw Fail.Conflict(ErrorCodes.RefundBusy);
        var payment = await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.Action == PaidNotApplied && a.EntityType == InvoiceEntity && a.EntityId == invoice.Id).OrderBy(a => a.Id).Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.Rule(ErrorCodes.RefundNotDue, invoice.Number);
        if (await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters().AnyAsync(a => a.Action == Refunded && a.EntityType == InvoiceEntity && a.EntityId == invoice.Id, ct)
                .ConfigureAwait(false))
            throw Fail.Rule(ErrorCodes.RefundAlreadyMarked, invoice.Number);
        var reference = TextNormalizer.CleanText(r.Reference);
        var paidOn = invoice.PaidAt is DateTime paid ? AmmanDate(paid) : (DateOnly?)null;
        var today = AmmanDate(Now);
        var errors = new List<FieldError>();
        if (r.RefundedOn is not DateOnly on || on > today || (paidOn is DateOnly first && on < first))
            errors.Add(new FieldError("refundedOn", ErrorCodes.RefundDate, [paidOn?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "—"]));
        if (string.IsNullOrEmpty(reference) || reference.Length > 100) errors.Add(new FieldError("reference", ErrorCodes.RefundReference, [100]));
        if (errors.Count > 0) throw Fail.Fields(errors);
        audit.Record(InvoiceEntity, invoice.Id, Refunded, tenantId: invoice.TenantId, values: new Dictionary<string, object?>
        {
            ["refundedOn"] = r.RefundedOn!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["reference"] = reference,
            ["payment"] = payment,
            ["gatewayReference"] = invoice.GatewayReference,
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var (_, refunded) = await RefundStateAsync(invoice.TenantId, ct).ConfigureAwait(false);
        return refunded.First(x => x.Payment.Number == invoice.Number);
    }

    private const string InvoiceEntity = "CORE.SubscriptionInvoice";
    private const string PaidNotApplied = "PaidNotApplied";
    private const string Refunded = "Refunded";

    private static DateOnly AmmanDate(DateTime utc)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Amman");
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));
    }

    /// <summary>
    /// Payments received but not applied (DT-184): every subscription invoice with a "PaidNotApplied" audit entry — the only record of such a
    /// payment, written by the billing code for a credited checkout, a replaced checkout or a payment that could not be applied. One with a
    /// "Refunded" entry (DT-185) is listed as refunded with its date, reference and who marked it; the others are due.
    /// </summary>
    private async Task<(IReadOnlyList<RefundDueResponse> Due, IReadOnlyList<RefundedResponse> Refunded)> RefundStateAsync(long? tenantId, CancellationToken ct)
    {
        var entries = await db.Set<AuditLog>().AsNoTracking().IgnoreQueryFilters()
            .Where(a => (a.Action == PaidNotApplied || a.Action == Refunded) && a.EntityType == InvoiceEntity && a.EntityId != null && (tenantId == null || a.TenantId == tenantId))
            .OrderBy(a => a.Id)
            .Select(a => new { a.Action, EntityId = a.EntityId!.Value, a.NewValues, a.OccurredAt, MarkedBy = db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == a.UserId).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = entries.Where(e => e.Action == PaidNotApplied).Select(e => e.EntityId).Distinct().ToList();
        if (ids.Count == 0) return ([], []);
        var invoices = await db.Set<SubscriptionInvoice>().AsNoTracking().IgnoreQueryFilters().Where(i => ids.Contains(i.Id))
            .Join(db.Set<Tenant>().IgnoreQueryFilters(), i => i.TenantId, t => t.Id, (i, t) => new { Invoice = i, t.PublicId, t.Name }).ToListAsync(ct).ConfigureAwait(false);
        string Kind(long id)
        {
            var reason = entries.First(e => e.Action == PaidNotApplied && e.EntityId == id).NewValues ?? "";
            return reason.Contains("Invoice credited", StringComparison.Ordinal) ? RefundKinds.Credited
                : reason.Contains("Invoice replaced", StringComparison.Ordinal) ? RefundKinds.Replaced : RefundKinds.NotApplied;
        }
        var due = new List<RefundDueResponse>();
        var refunded = new List<RefundedResponse>();
        foreach (var x in invoices.OrderByDescending(x => x.Invoice.PaidAt).ThenByDescending(x => x.Invoice.Id))
        {
            var row = new RefundDueResponse(support.Token<SubscriptionInvoice>(x.Invoice.PublicId), x.Invoice.Number, support.Token<Tenant>(x.PublicId), x.Name, x.Invoice.Total,
                x.Invoice.PaidAt is DateTime p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null, Kind(x.Invoice.Id));
            var mark = entries.FirstOrDefault(e => e.Action == Refunded && e.EntityId == x.Invoice.Id);
            if (mark is null)
            {
                due.Add(row);
                continue;
            }
            using var values = System.Text.Json.JsonDocument.Parse(mark.NewValues ?? "{}");
            string? Value(string name) => values.RootElement.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
            refunded.Add(new RefundedResponse(row, DateOnly.ParseExact(Value("refundedOn") ?? "0001-01-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Value("reference") ?? "", mark.MarkedBy, DateTime.SpecifyKind(mark.OccurredAt, DateTimeKind.Utc)));
        }
        return (due, refunded.OrderByDescending(r => r.MarkedAt).ToList());
    }

    /// <summary>Extends a trial (days 1–90); a tenant suspended at the end of its trial returns to Trial.</summary>
    public async Task<ConsoleTenantResponse> ExtendTrialAsync(string token, ExtendTrialRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        if (r.Days is not (>= 1 and <= 90)) throw Fail.Field("days", ErrorCodes.ValidationRange, new FieldRef("days"));
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        var suspendedTrial = t.Status == LifecycleCodes.Suspended && t.CurrentPeriodEnd is null;
        if (t.Status != LifecycleCodes.Trial && !suspendedTrial) throw Fail.Rule(ErrorCodes.ValidationFailed);
        var from = t.TrialEndsAt is DateTime e && e > Now ? e : Now;
        t.TrialEndsAt = from.AddDays(r.Days.Value);
        t.Status = LifecycleCodes.Trial;
        t.GraceEndsAt = null;
        changes.SetAction(t, "ExtendTrial", why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await TenantAsync(t, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A credit reduces only the invoice it is applied to (D-11): the full amount marks it Credited; a part lowers its total, and the net
    /// amount and tax are derived from the new total (tax-inclusive, Total = Amount + Tax kept).
    /// </summary>
    public async Task<SubscriptionInvoiceResponse> CreditAsync(string token, ApplyCreditRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        var pid = support.Decode<SubscriptionInvoice>(r.InvoiceId) ?? throw Fail.Field("invoiceId", ErrorCodes.ValidationFormat, new FieldRef("invoiceId"));
        var invoice = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters().SingleOrDefaultAsync(i => i.PublicId == pid && i.TenantId == t.Id && !i.IsDeleted, ct).ConfigureAwait(false)
            ?? throw Fail.Field("invoiceId", ErrorCodes.ValidationFormat, new FieldRef("invoiceId"));
        if (invoice.Status is not (LifecycleCodes.Due or LifecycleCodes.Failed)) throw Fail.Rule(ErrorCodes.SubscriptionInvoiceNotDue, invoice.Number);
        var decimals = options.Value.CurrencyDecimals;
        if (r.Amount is not decimal credit || credit <= 0 || credit > invoice.Total || !Money.IsValidAmount(credit, decimals))
            throw Fail.Field("amount", ErrorCodes.ValidationRange, new FieldRef("amount"));
        return await ApplyCreditAsync(invoice, credit, why, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels an unpaid checkout (owner decision 13 Oct 2026, DT-183): the SUB- invoice is already an issued tax invoice (numbered without
    /// gaps, printable), so it is never voided — staff credit its whole remaining total with a reason and it becomes Credited. Only an
    /// invoice awaiting payment qualifies: Due with a gateway reference (a renewal is Due without one until charged; paid, failed and
    /// credited invoices are refused). A late payment of its old payment page is recorded, never applied (BillingService.ApplyPaymentAsync).
    /// </summary>
    public async Task<SubscriptionInvoiceResponse> CancelCheckoutAsync(string token, string invoiceToken, ReasonRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        var pid = support.Decode<SubscriptionInvoice>(invoiceToken) ?? throw Fail.NotFound();
        var invoice = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters().SingleOrDefaultAsync(i => i.PublicId == pid && i.TenantId == t.Id && !i.IsDeleted, ct).ConfigureAwait(false)
            ?? throw Fail.NotFound();
        if (invoice.Status != LifecycleCodes.Due || string.IsNullOrEmpty(invoice.GatewayReference)) throw Fail.Rule(ErrorCodes.SubscriptionNotAwaitingPayment, invoice.Number);
        return await ApplyCreditAsync(invoice, invoice.Total, "Unpaid checkout cancelled: " + why, ct).ConfigureAwait(false);
    }

    private async Task<SubscriptionInvoiceResponse> ApplyCreditAsync(SubscriptionInvoice invoice, decimal credit, string why, CancellationToken ct)
    {
        var decimals = options.Value.CurrencyDecimals;
        if (credit == invoice.Total)
        {
            invoice.Status = LifecycleCodes.Credited;
        }
        else
        {
            // The lower total stays tax-inclusive: net and tax derived from it again (owner decision 13 Oct 2026).
            var total = invoice.Total - credit;
            (invoice.Amount, invoice.Tax) = BillingService.Split(total, options.Value.TaxRate, decimals);
            invoice.Total = total;
        }
        changes.SetAction(invoice, "Credit", why + " · " + Money.Format(credit));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return subscriptions.Invoice(invoice);
    }

    /// <summary>Suspends a tenant (read-only, BR-010); the suspension date is kept in GraceEndsAt for the 30-day cancellation (DT-125).</summary>
    public async Task<ConsoleTenantResponse> SuspendAsync(string token, TenantStatusRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        if (t.Status is LifecycleCodes.Suspended or LifecycleCodes.Cancelled or LifecycleCodes.Deleted) throw Fail.Rule(ErrorCodes.ValidationFailed);
        t.Status = LifecycleCodes.Suspended;
        t.GraceEndsAt = Now;
        changes.SetAction(t, "Suspend", why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await TenantAsync(t, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reactivates a suspended, past-due or cancelled tenant (BR-011): full access at once, no data change. A running trial goes back to Trial;
    /// a paid period still running goes back to Active; otherwise the tenant is PastDue with the usual grace to pay — never a free period
    /// (review F4). A tenant on the free trial plan whose trial has ended needs a trial extension or a paid subscription instead.
    /// </summary>
    public async Task<ConsoleTenantResponse> ReactivateAsync(string token, TenantStatusRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var t = await FindTenantAsync(token, ct).ConfigureAwait(false);
        if (t.Status is not (LifecycleCodes.Suspended or LifecycleCodes.PastDue or LifecycleCodes.Cancelled)) throw Fail.Rule(ErrorCodes.ValidationFailed);
        if (t.CurrentPeriodEnd is null && t.TrialEndsAt is DateTime e && e > Now)
        {
            t.Status = LifecycleCodes.Trial;
            t.GraceEndsAt = null;
        }
        else if (t.Plan.Price == 0)
        {
            throw Fail.Rule(ErrorCodes.SubscriptionFreePlan);
        }
        else if (t.CurrentPeriodEnd is DateTime end && end > Now)
        {
            t.Status = LifecycleCodes.Active;
            t.GraceEndsAt = null;
        }
        else
        {
            t.Status = LifecycleCodes.PastDue;
            t.GraceEndsAt = Now.AddDays(options.Value.GraceDays);
        }
        t.CancelledAt = null;
        t.RetentionEndsAt = null;
        changes.SetAction(t, "Reactivate", why);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await TenantAsync(t, ct).ConfigureAwait(false);
    }

    // ============================================================ support access (P-13, BR-014)

    private async Task<List<SupportAccessResponse>> SupportRowsAsync(IQueryable<SupportAccessGrant> q, CancellationToken ct)
    {
        var rows = await q.OrderByDescending(g => g.Id).Take(200)
            .Select(g => new { G = g, Tenant = db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == g.TenantId).Select(t => t.Name).FirstOrDefault(),
                OwnerEmail = db.Set<UserCompanyRole>().IgnoreQueryFilters().Where(r => r.TenantId == g.TenantId && !r.IsDeleted && r.Role.IsPreset && r.Role.Name == PresetRoles.Owner && r.User.Status == "Active")
                    .OrderBy(r => r.Id).Select(r => r.User.Email).FirstOrDefault(),
                Requester = db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == g.RequestedBy).Select(u => u.FullName).FirstOrDefault(), Decider = db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == g.ApprovedBy).Select(u => u.FullName).FirstOrDefault() })
            .ToListAsync(ct).ConfigureAwait(false);
        var refusals = await SupportAccessService.RefusalReasonsAsync(db, null, rows.Where(x => x.G.Status == LifecycleCodes.Rejected).Select(x => x.G.Id).ToList(), ct)
            .ConfigureAwait(false);
        var ends = await SupportAccessService.EndsAsync(db, null, rows.Where(x => x.G.Status == LifecycleCodes.Expired).Select(x => x.G.Id).ToList(), ct).ConfigureAwait(false);
        var now = Now;
        return rows.Select(x => new SupportAccessResponse(support.Token<SupportAccessGrant>(x.G.PublicId), x.Tenant ?? string.Empty, x.Requester ?? string.Empty, x.G.Reason,
            x.G.Status == LifecycleCodes.Approved && x.G.EndsAt <= now ? LifecycleCodes.Expired : x.G.Status, Utc(x.G.StartsAt), Utc(x.G.EndsAt), x.Decider,
            DateTime.SpecifyKind(x.G.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(x.G.RowVersion), refusals.GetValueOrDefault(x.G.Id), x.OwnerEmail, ends.GetValueOrDefault(x.G.Id))).ToList();
    }

    /// <summary>
    /// Subscribers for a support request (owner testing 19 Oct 2026, DT-214): by subscriber name, the owner's email or any user's email (at
    /// least 2 characters; empty = the newest). Owner-email matches come first, then name matches, then other users' emails; each option
    /// carries what tells same-named subscribers apart (owner email, plan, sign-up date, status). Deleted subscribers are never offered.
    /// </summary>
    public async Task<IReadOnlyList<SupportTenantOption>> SupportTenantsAsync(string? q, CancellationToken ct)
    {
        _ = StaffId;
        const int max = 20;
        var text = MasterDataSupport.SearchText(q);
        var tenants = db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => !t.IsDeleted && t.Status != LifecycleCodes.Deleted);
        var owners = db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(r => !r.IsDeleted && r.Role.IsPreset && r.Role.Name == PresetRoles.Owner && r.User.Status == "Active" && !r.User.IsDeleted);
        var shaped = tenants.Select(t => new
        {
            t.Id, t.PublicId, t.Name, t.Status, t.CreatedAt, t.Plan.Code, t.Plan.NameEn,
            Owner = owners.Where(r => r.TenantId == t.Id).OrderBy(r => r.Id).Select(r => r.User.Email).FirstOrDefault(),
        });
        if (text is not null && text.Length < 2) return [];
        var picked = text is null
            ? (await shaped.OrderByDescending(t => t.Id).Take(max).ToListAsync(ct).ConfigureAwait(false)).Select(t => (t, Rank: 1, Matched: (string?)null)).ToList()
            : [];
        if (text is not null)
        {
            var byOwner = await shaped.Where(t => t.Owner != null && t.Owner.Contains(text)).OrderBy(t => t.Name).ThenBy(t => t.Id).Take(max).ToListAsync(ct).ConfigureAwait(false);
            var byName = await shaped.Where(t => t.Name.Contains(text)).OrderBy(t => t.Name).ThenBy(t => t.Id).Take(max).ToListAsync(ct).ConfigureAwait(false);
            var users = await db.Set<User>().AsNoTracking().IgnoreQueryFilters().Where(u => !u.IsDeleted && u.TenantId != null && u.Email != null && u.Email.Contains(text))
                .OrderBy(u => u.Email).Take(max).Select(u => new { TenantId = u.TenantId!.Value, u.Email }).ToListAsync(ct).ConfigureAwait(false);
            var userTenantIds = users.Select(u => u.TenantId).Distinct().ToList();
            var byUser = await shaped.Where(t => userTenantIds.Contains(t.Id)).ToListAsync(ct).ConfigureAwait(false);
            picked = byOwner.Select(t => (t, Rank: 0, Matched: (string?)null))
                .Concat(byName.Select(t => (t, Rank: 1, Matched: (string?)null)))
                .Concat(byUser.OrderBy(t => t.Name).Select(t => (t, Rank: 2, Matched: (string?)users.First(u => u.TenantId == t.Id).Email)))
                .GroupBy(x => x.t.Id).Select(g => g.OrderBy(x => x.Rank).First())
                .OrderBy(x => x.Rank).ThenBy(x => x.t.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(x => x.t.Id).Take(max).ToList();
        }
        return picked.Select(x => new SupportTenantOption(support.Token<Tenant>(x.t.PublicId), x.t.Name, x.t.Owner, x.t.Code, x.t.NameEn,
            DateTime.SpecifyKind(x.t.CreatedAt, DateTimeKind.Utc), x.t.Status, x.Matched == x.t.Owner ? null : x.Matched)).ToList();
    }

    /// <summary>A staff request for access to a tenant; its Owners are notified (FR-190) and decide.</summary>
    public async Task<SupportAccessResponse> RequestSupportAsync(SupportAccessRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var reason = TextNormalizer.CleanText(r.Reason);
        new Validator().Require("reason", reason).MaxLength("reason", reason, 500).ThrowIfInvalid();
        var t = await FindTenantAsync(r.TenantId ?? throw Fail.Field("tenantId", ErrorCodes.ValidationRequired, new FieldRef("tenantId")), ct).ConfigureAwait(false);
        var grant = new SupportAccessGrant { TenantId = t.Id, RequestedBy = StaffId, Reason = reason!, Status = LifecycleCodes.Requested };
        db.Set<SupportAccessGrant>().Add(grant);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        var owners = await db.Set<UserCompanyRole>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.TenantId == t.Id && !x.IsDeleted && x.User.Status == "Active"
                && db.Set<RolePermission>().IgnoreQueryFilters().Any(p => p.RoleId == x.RoleId && !p.IsDeleted && p.Permission.PermissionKey == PermissionKeys.TenantManage))
            .Select(x => x.UserId).Distinct().ToListAsync(ct).ConfigureAwait(false);
        await notifications.NotifyAsync(owners, null, NotificationService.SupportRequested, new Dictionary<string, string?> { ["reason"] = reason },
            "/tenant/support-access", ct, t.Id).ConfigureAwait(false);
        return (await SupportRowsAsync(db.Set<SupportAccessGrant>().AsNoTracking().IgnoreQueryFilters().Where(g => g.Id == grant.Id), ct).ConfigureAwait(false))[0];
    }

    public async Task<IReadOnlyList<SupportAccessResponse>> MySupportAsync(CancellationToken ct)
    {
        var me = StaffId;
        return await SupportRowsAsync(db.Set<SupportAccessGrant>().AsNoTracking().IgnoreQueryFilters().Where(g => g.RequestedBy == me && !g.IsDeleted), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The active companies the staff member can open now: those of tenants with an approved, current window of theirs (DT-152), with the
    /// read-only session permissions — the same rule the request pipeline applies to every support request.
    /// </summary>
    public async Task<IReadOnlyList<Contracts.Identity.CompanyAccess>> SupportCompaniesAsync(CancellationToken ct)
    {
        var me = StaffId;
        var now = Now;
        var companies = await db.Set<Company>().AsNoTracking().IgnoreQueryFilters()
            .Where(c => !c.IsDeleted && c.Status == "Active" && db.Set<SupportAccessGrant>().IgnoreQueryFilters()
                .Any(g => g.TenantId == c.TenantId && g.RequestedBy == me && !g.IsDeleted && g.Status == LifecycleCodes.Approved && g.StartsAt <= now && g.EndsAt > now))
            .OrderBy(c => c.Name).Select(c => new { c.PublicId, c.Name, c.NameEn, c.CurrencyCode, c.CurrencyDecimals }).Take(200).ToListAsync(ct).ConfigureAwait(false);
        var permissions = SupportAccessService.SessionPermissions.Order(StringComparer.Ordinal).ToList();
        return companies.Select(c => new Contracts.Identity.CompanyAccess(support.Token<Company>(c.PublicId), c.Name, c.NameEn, "Active", "Support", true, permissions,
            c.CurrencyCode, c.CurrencyDecimals)).ToList();
    }

    // ============================================================ announcements and health (P-14, P-15)

    public async Task<IReadOnlyList<AnnouncementResponse>> AnnouncementsAsync(bool currentOnly, CancellationToken ct)
    {
        var now = Now;
        var q = db.Set<Announcement>().AsNoTracking();
        if (currentOnly) q = q.Where(a => a.StartsAt <= now && a.EndsAt > now);
        else _ = StaffId;
        return (await q.OrderByDescending(a => a.StartsAt).Take(100).ToListAsync(ct).ConfigureAwait(false))
            .Select(a => new AnnouncementResponse(support.Token<Announcement>(a.PublicId), a.Title, a.Message, DateTime.SpecifyKind(a.StartsAt, DateTimeKind.Utc),
                DateTime.SpecifyKind(a.EndsAt, DateTimeKind.Utc), Convert.ToBase64String(a.RowVersion))).ToList();
    }

    public async Task<AnnouncementResponse> SaveAnnouncementAsync(string? token, AnnouncementRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        _ = StaffId;
        var title = TextNormalizer.CleanText(r.Title);
        var message = TextNormalizer.CleanText(r.Message);
        new Validator().Require("title", title).MaxLength("title", title, 200).Require("message", message).MaxLength("message", message, 1000)
            .Check(r.StartsAt is not null && r.EndsAt is not null && r.EndsAt > r.StartsAt, "endsAt", ErrorCodes.ValidationRange).ThrowIfInvalid();
        Announcement a;
        if (token is null)
        {
            a = new Announcement();
            db.Set<Announcement>().Add(a);
        }
        else
        {
            a = await support.FindAsync<Announcement>(token, ct).ConfigureAwait(false);
            db.ExpectRowVersion(a, r.RowVersion);
        }
        a.Title = title!;
        a.Message = message!;
        a.StartsAt = r.StartsAt!.Value.ToUniversalTime();
        a.EndsAt = r.EndsAt!.Value.ToUniversalTime();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AnnouncementResponse(support.Token<Announcement>(a.PublicId), a.Title, a.Message, DateTime.SpecifyKind(a.StartsAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(a.EndsAt, DateTimeKind.Utc), Convert.ToBase64String(a.RowVersion));
    }

    public async Task DeleteAnnouncementAsync(string token, CancellationToken ct)
    {
        _ = StaffId;
        var a = await support.FindAsync<Announcement>(token, ct).ConfigureAwait(false);
        a.DeleteReason = "Removed by provider staff";
        db.Set<Announcement>().Remove(a);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Platform health (P-15): database reachable, tenants by status, totals, queues, process memory and uptime.</summary>
    public async Task<HealthResponse> HealthAsync(CancellationToken ct)
    {
        _ = StaffId;
        var ok = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
        var byStatus = await db.Set<Tenant>().AsNoTracking().IgnoreQueryFilters().Where(t => !t.IsDeleted).GroupBy(t => t.Status).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct).ConfigureAwait(false);
        string[] all = ["Tenant", "Company", "SoftDelete"];
        var users = await db.Set<User>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(u => !u.IsDeleted && u.Status == "Active", ct).ConfigureAwait(false);
        var companies = await db.Set<Company>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(c => !c.IsDeleted && c.Status == "Active", ct).ConfigureAwait(false);
        var month = new DateTime(Now.Year, Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var trips = await db.Set<Trip>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(t => !t.IsDeleted && t.CreatedAt >= month, ct).ConfigureAwait(false);
        var pending = await db.Set<PendingPosting>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(p => p.ResolvedAt == null && !p.IsDeleted, ct).ConfigureAwait(false);
        var einv = await db.Set<EInvoiceSubmission>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(s => !s.IsDeleted && (s.Status == "Queued" || s.Status == "Rejected"), ct)
            .ConfigureAwait(false)
            + await db.Set<ApArEInvoiceSubmission>().AsNoTracking().IgnoreQueryFilters(all).CountAsync(s => !s.IsDeleted && (s.Status == "Queued" || s.Status == "Rejected"), ct)
                .ConfigureAwait(false);
        return new HealthResponse(ok, byStatus, users, companies, trips, pending, einv, Environment.WorkingSet / 1048576, DateTime.UtcNow - Started, Now);
    }
}
