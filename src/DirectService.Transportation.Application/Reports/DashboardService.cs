using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Reports;

/// <summary>
/// The dashboard (FR-137 … FR-141, SCR-07): today or month-to-date figures; alert cards that open filtered lists (driver documents, unbilled
/// trips, overdue receivables, drafts awaiting approval, rejected e-invoices, pending postings, trips needing review / with shortage / with a
/// tariff breach, loading sites without current-month rates, opening-balance difference during setup); one balance per cash box and bank
/// account; the user's pinned reports. Every part appears only with its permission (FR-141, DT-117); live refresh is pushed through the
/// real-time channel (B11).
/// </summary>
public sealed class DashboardService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, LedgerReportService ledger, MonthlyRateService rates,
    PostingEngine posting, ISavedReportService saved, TimeProvider clock)
{
    private const string Active = "Active";

    private bool En => ctx.Language == "en";

    private string L(string ar, string en) => En ? en : ar;

    private bool Any(params string[] keys) => keys.Any(ctx.HasPermission);

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<DashboardResponse> GetAsync(string? period, CancellationToken ct)
    {
        var p = (period?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "mtd" or "month" => "mtd",
            "today" => "today",
            _ => throw Fail.Field("period", ErrorCodes.ValidationFormat, new FieldRef("period")),
        };
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var from = p == "today" ? today : new DateOnly(today.Year, today.Month, 1);

        var figures = await FiguresAsync(from, today, ct).ConfigureAwait(false);
        var alerts = await AlertsAsync(from, today, ct).ConfigureAwait(false);
        IReadOnlyList<DashboardCashCard>? cash = null;
        if (Any(PermissionKeys.AcctView, PermissionKeys.ReportsView))
            cash = (await ledger.CashBankBalancesAsync(today, ct).ConfigureAwait(false)).Where(b => b.Status == Active)
                .Select(b => new DashboardCashCard(b.Account.Kind, b.Account.Id, b.Account.Name, b.Balance)).ToList();
        var pins = Any(PermissionKeys.ReportsView) ? await saved.PinsAsync(ct).ConfigureAwait(false) : [];
        decimal? difference = null;
        if (Any(PermissionKeys.AcctView) && await posting.IsSetupOpenAsync(ct).ConfigureAwait(false))
            difference = await ledger.OpeningDifferenceAsync(today, ct).ConfigureAwait(false);
        return new DashboardResponse(p, figures, alerts, cash, pins, difference, clock.GetUtcNow().UtcDateTime);
    }

    /// <summary>FR-137: trips, tonnage (loaded weight), revenue, wages, partner costs and margin of active trips by trip date; invoiced = approved invoices by invoice date; collections = active receipts by receipt date.</summary>
    private async Task<DashboardFigures> FiguresAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        int? trips = null;
        decimal? tonnage = null, revenue = null, wages = null, partner = null, margin = null, invoiced = null, collections = null;
        if (Any(PermissionKeys.TripsView))
        {
            var t = await db.Set<Trip>().AsNoTracking().Where(x => x.Status == Active && x.TripDate >= from && x.TripDate <= to).GroupBy(_ => 1)
                .Select(g => new { N = g.Count(), W = g.Sum(x => x.LoadedWeight), R = g.Sum(x => x.Revenue), D = g.Sum(x => x.DriverWage), P = g.Sum(x => x.PartnerCost) })
                .SingleOrDefaultAsync(ct).ConfigureAwait(false);
            trips = t?.N ?? 0;
            tonnage = t?.W ?? 0;
            revenue = t?.R ?? 0;
            wages = t?.D ?? 0;
            partner = t?.P ?? 0;
            margin = revenue - wages - partner;
        }
        if (Any(PermissionKeys.InvView))
        {
            invoiced = await db.Set<Invoice>().AsNoTracking().Where(i => (i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed) && i.InvoiceDate >= from && i.InvoiceDate <= to)
                .SumAsync(i => i.Total, ct).ConfigureAwait(false);
        }
        if (Any(PermissionKeys.InvView, PermissionKeys.RcptManage))
        {
            collections = await db.Set<Receipt>().AsNoTracking().Where(r => r.Status == CashCodes.Active && r.ReceiptDate >= from && r.ReceiptDate <= to)
                .SumAsync(r => r.Amount, ct).ConfigureAwait(false);
        }
        return new DashboardFigures(from, to, trips, tonnage, revenue, wages, partner, margin, invoiced, collections);
    }

    private static DrillLink Link(string type, params (string Key, string Value)[] filters) =>
        new(type, null, filters.ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal));

    /// <summary>FR-138: alert cards with a count, a value where it means something, a detail line and the filtered list they open.</summary>
    private async Task<List<DashboardAlert>> AlertsAsync(DateOnly from, DateOnly today, CancellationToken ct)
    {
        var alerts = new List<DashboardAlert>();
        if (Any(PermissionKeys.DrvView, PermissionKeys.MastersManage))
        {
            var limit = today.AddDays(MasterCodes.AlertDays);
            var drivers = db.Set<Driver>().AsNoTracking().Where(d => d.Status == Active);
            var expired = await drivers.CountAsync(d => d.LicenceExpiry < today || d.RegistrationExpiry < today || d.InsuranceExpiry < today || d.InspectionExpiry < today, ct)
                .ConfigureAwait(false);
            var expiring = await drivers.CountAsync(d => !(d.LicenceExpiry < today || d.RegistrationExpiry < today || d.InsuranceExpiry < today || d.InspectionExpiry < today)
                && (d.LicenceExpiry <= limit || d.RegistrationExpiry <= limit || d.InsuranceExpiry <= limit || d.InspectionExpiry <= limit), ct).ConfigureAwait(false);
            if (expired + expiring > 0)
                alerts.Add(new DashboardAlert("driver-documents", expired > 0 ? "error" : "warning", L("وثائق سائقين منتهية أو قريبة الانتهاء", "Driver documents expired or expiring"),
                    expired + expiring, null, L($"منتهية {expired} · خلال 30 يوماً {expiring}", $"Expired {expired} · within 30 days {expiring}"),
                    Link("drivers", ("expiry", expired > 0 ? "expired" : "expiring"))));
        }
        if (Any(PermissionKeys.TripsView, PermissionKeys.InvView))
        {
            var u = await db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active && t.InvoiceId == null).GroupBy(_ => 1)
                .Select(g => new { N = g.Count(), V = g.Sum(t => t.Revenue), Oldest = g.Min(t => t.TripDate) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (u is { N: > 0 })
                alerts.Add(new DashboardAlert("unbilled-trips", "warning", L("رحلات غير مفوترة", "Unbilled trips"), u.N, u.V,
                    L("أقدمها ", "Oldest ") + D(u.Oldest), Link("trips", ("billing", "unbilled"))));
        }
        if (Any(PermissionKeys.InvView))
        {
            var o = await db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceCodes.Approved && i.RemainingAmount > 0 && i.DueDate < today).GroupBy(_ => 1)
                .Select(g => new { N = g.Count(), V = g.Sum(i => i.RemainingAmount) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (o is { N: > 0 })
                alerts.Add(new DashboardAlert("overdue-receivables", "error", L("ذمم مستحقة متأخرة", "Overdue receivables"), o.N, o.V, null, Link("invoices", ("overdue", "true"))));
            var drafts = await db.Set<Invoice>().AsNoTracking().Where(i => i.Status == InvoiceCodes.Draft).GroupBy(_ => 1)
                .Select(g => new { N = g.Count(), V = g.Sum(i => i.Total) }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (drafts is { N: > 0 })
                alerts.Add(new DashboardAlert("draft-invoices", "info", L("مسودات بانتظار الاعتماد", "Drafts awaiting approval"), drafts.N, drafts.V, null, Link("invoices", ("status", "draft"))));
            var rejected = await db.Set<EInvoiceSubmission>().AsNoTracking().CountAsync(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null), ct)
                .ConfigureAwait(false);
            if (Any(PermissionKeys.AcctApar, PermissionKeys.AcctView))
                rejected += await db.Set<ApArEInvoiceSubmission>().AsNoTracking().CountAsync(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null), ct)
                    .ConfigureAwait(false);
            if (rejected > 0)
                alerts.Add(new DashboardAlert("rejected-einvoices", "error", L("فواتير إلكترونية مرفوضة أو تحتاج تصحيحاً", "E-invoices rejected or needing correction"), rejected, null, null,
                    Link("einvoice-submissions", ("status", "attention"))));
        }
        if (Any(PermissionKeys.JeView, PermissionKeys.JeExport))
        {
            var pending = await db.Set<PendingPosting>().AsNoTracking().CountAsync(x => x.ResolvedAt == null, ct).ConfigureAwait(false);
            if (pending > 0)
                alerts.Add(new DashboardAlert("pending-postings", "error", L("ترحيلات معلّقة", "Pending postings"), pending, null, null, Link("pending-postings")));
        }
        if (Any(PermissionKeys.TripsView))
        {
            var trips = db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active);
            var review = await trips.CountAsync(t => t.NeedsReview, ct).ConfigureAwait(false);
            if (review > 0) alerts.Add(new DashboardAlert("needs-review", "warning", L("رحلات تحتاج مراجعة", "Trips needing review"), review, null, null, Link("trips", ("review", "true"))));
            var inPeriod = trips.Where(t => t.TripDate >= from && t.TripDate <= today);
            var shortage = await inPeriod.CountAsync(t => t.IsShortageFlagged, ct).ConfigureAwait(false);
            if (shortage > 0)
                alerts.Add(new DashboardAlert("shortage", "warning", L("رحلات بنقص", "Trips with shortage"), shortage, null, null,
                    Link("trips", ("shortage", "true"), ("from", D(from)), ("to", D(today)))));
            var breach = await inPeriod.CountAsync(t => t.IsTariffBreach, ct).ConfigureAwait(false);
            if (breach > 0)
                alerts.Add(new DashboardAlert("tariff-breach", "warning", L("رحلات بمخالفة تعرفة", "Trips with a tariff breach"), breach, null, null,
                    Link("trips", ("breach", "true"), ("from", D(from)), ("to", D(today)))));
        }
        var missing = await rates.MissingAsync(null, ct).ConfigureAwait(false);
        if (missing.Sites.Count > 0)
            alerts.Add(new DashboardAlert("missing-rates", "warning", L("مواقع تحميل بلا أسعار للشهر الحالي", "Loading sites without current-month rates"), missing.Sites.Count, null,
                string.Join("، ", missing.Sites.Take(5).Select(s => s.Name)), Link("monthly-rates", ("month", missing.Month))));
        return alerts;
    }
}
