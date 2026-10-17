using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Reports;

/// <summary>
/// The standard reports of 7.7.2 and 7.7.3 (FR-142, RPT-01 … RPT-23). Reports already served by their module (trips list RPT-01,
/// driver/partner statements RPT-13/14, ledger and cash statements RPT-15/16, ageing RPT-17/18, registers RPT-21, e-invoicing RPT-22,
/// reconciliation RPT-23) are listed in the catalog with their path; the others are computed here: unbilled trips, shortage, tariff
/// breach, driver productivity, customer revenue, routes, materials, partner shipments, margin analysis, monthly summary, customer
/// statement, driver and partner balances. Trips count when active, by trip date; tonnage = loaded weight (DT-114). Every row links to its
/// record or a filtered list (FR-149); every report exports to Excel/CSV/PDF (FR-150).
/// </summary>
public sealed class StandardReportService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, LedgerReportService ledger, ReportExporter exporter)
{
    private const string Active = "Active";
    public const int MaxYears = 10;
    private bool En => ctx.Language == "en";

    private string L(string ar, string en) => En ? en : ar;

    private sealed record Def(string Key, string Code, string Group, string Ar, string En, string? Path, string[] Parameters, string AltPermission, string[]? Required = null);

    private static readonly Def[] Catalog =
    [
        new("trips", "RPT-01", "operational", "تقرير الرحلات", "Trips report", "/trips", ["from", "to", "customer", "driver", "partner", "loadingSite", "unloadingSite", "material"], PermissionKeys.TripsView),
        new("unbilled-trips", "RPT-02", "operational", "الرحلات غير المفوترة", "Unbilled trips", null, ["to", "customer"], PermissionKeys.TripsView),
        new("shortage", "RPT-03", "operational", "تقرير النقص", "Shortage report", null, ["from", "to", "driver", "loadingSite"], PermissionKeys.TripsView),
        new("tariff-breach", "RPT-04", "operational", "مخالفات التعرفة", "Tariff breach report", null, ["from", "to", "side", "customer", "partner", "loadingSite", "unloadingSite"], PermissionKeys.TripsView),
        new("driver-productivity", "RPT-05", "operational", "إنتاجية السائقين", "Driver productivity", null, ["from", "to", "driver"], PermissionKeys.TripsView),
        new("customer-revenue", "RPT-06", "operational", "إيراد العملاء", "Customer revenue", null, ["from", "to", "customer"], PermissionKeys.TripsView),
        new("routes", "RPT-07", "operational", "تحليل المسارات والمواقع", "Route and site analysis", null, ["from", "to", "loadingSite", "unloadingSite"], PermissionKeys.TripsView),
        new("materials", "RPT-08", "operational", "حجم المواد", "Material volume", null, ["from", "to", "material"], PermissionKeys.TripsView),
        new("partner-shipments", "RPT-09", "operational", "شحنات الشركاء", "Partner shipments", null, ["from", "to", "partner"], PermissionKeys.TripsView),
        new("margin", "RPT-10", "operational", "تحليل الهامش", "Margin analysis", null, ["from", "to", "groupBy"], PermissionKeys.TripsView),
        new("monthly-summary", "RPT-11", "operational", "الملخص الشهري", "Monthly summary", null, ["from", "to"], PermissionKeys.TripsView),
        new("customer-statement", "RPT-12", "financial", "كشف حساب عميل", "Customer statement", null, ["customer", "from", "to"], PermissionKeys.InvView, Required: ["customer"]),
        // Owner decision 17 Oct 2026: a supplier statement like the customer statement, with amount, tax, deduction and net.
        new("supplier-statement", "RPT-24", "financial", "كشف حساب مورد", "Supplier statement", null, ["supplier", "from", "to"], PermissionKeys.AcctApar, Required: ["supplier"]),
        new("driver-statement", "RPT-13", "financial", "كشف حساب سائق", "Driver statement", "/drivers/{driver}/statement", ["driver", "from", "to"], PermissionKeys.DrvView),
        new("partner-statement", "RPT-14", "financial", "كشف حساب شريك", "Partner statement", "/partners/{partner}/statement", ["partner", "from", "to"], PermissionKeys.DrvView),
        new("account-statement", "RPT-15", "financial", "كشف حساب أستاذ", "Ledger account statement", "/accounting/reports/account-statement", ["account", "from", "to"], PermissionKeys.AcctView),
        new("cash-bank-statement", "RPT-16", "financial", "كشف صندوق / بنك", "Cash / bank statement", "/accounting/reports/cash-bank-statement", ["cashBox", "bankAccount", "from", "to"], PermissionKeys.AcctView),
        new("receivables-ageing", "RPT-17", "financial", "أعمار الذمم المدينة", "Receivables ageing", "/apar/ageing/receivables", ["asOf"], PermissionKeys.AcctView),
        new("payables-ageing", "RPT-18", "financial", "أعمار الذمم الدائنة", "Payables ageing", "/apar/ageing/payables", ["asOf"], PermissionKeys.AcctView),
        new("driver-balances", "RPT-19", "financial", "أرصدة السائقين", "Driver balances", null, ["asOf"], PermissionKeys.DrvView),
        new("partner-balances", "RPT-20", "financial", "أرصدة الشركاء", "Partner balances", null, ["asOf"], PermissionKeys.DrvView),
        new("registers", "RPT-21", "financial", "السجلات", "Registers", "/invoices · /credit-notes · /receipts · /advances · /settlements · /partner-payments", ["from", "to"], PermissionKeys.InvView),
        new("e-invoicing", "RPT-22", "financial", "تقرير الفوترة الإلكترونية", "E-invoicing report", "/einvoice-submissions", ["status", "from", "to"], PermissionKeys.InvView),
        new("reconciliation", "RPT-23", "financial", "مطابقة الحسابات المساعدة", "Sub-ledger reconciliation", "/accounting/reports/reconciliation", ["asOf"], PermissionKeys.AcctView),
    ];

    /// <summary>Reports the caller may run: reports.view opens all (PRD 3.4), otherwise the module's view permission.</summary>
    public IReadOnlyList<ReportCatalogEntry> CatalogFor() => Catalog.Where(Allowed)
        .Select(d => new ReportCatalogEntry(d.Key, d.Code, d.Group, En ? d.En : d.Ar, d.Path, d.Parameters, d.Required ?? [])).ToList();

    private bool Allowed(Def d) => ctx.HasPermission(PermissionKeys.ReportsView) || ctx.HasPermission(d.AltPermission);

    public async Task<ReportResult> RunAsync(string key, StandardReportQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var def = Catalog.FirstOrDefault(d => d.Key == key && d.Path is null) ?? throw Fail.NotFound();
        if (!Allowed(def)) throw Fail.Forbidden();
        var title = En ? def.En : def.Ar;
        return key switch
        {
            "unbilled-trips" => await UnbilledAsync(def, title, q, ct).ConfigureAwait(false),
            "shortage" => await ShortageAsync(def, title, q, ct).ConfigureAwait(false),
            "tariff-breach" => await BreachAsync(def, title, q, ct).ConfigureAwait(false),
            "driver-productivity" => await ProductivityAsync(def, title, q, ct).ConfigureAwait(false),
            "customer-revenue" => await CustomerRevenueAsync(def, title, q, ct).ConfigureAwait(false),
            "routes" => await RoutesAsync(def, title, q, ct).ConfigureAwait(false),
            "materials" => await MaterialsAsync(def, title, q, ct).ConfigureAwait(false),
            "partner-shipments" => await PartnerShipmentsAsync(def, title, q, ct).ConfigureAwait(false),
            "margin" => await MarginAsync(def, title, q, ct).ConfigureAwait(false),
            "monthly-summary" => await MonthlyAsync(def, title, q, ct).ConfigureAwait(false),
            "customer-statement" => await CustomerStatementAsync(def, title, q, ct).ConfigureAwait(false),
            "supplier-statement" => await SupplierStatementAsync(def, title, q, ct).ConfigureAwait(false),
            "driver-balances" => await DriverBalancesAsync(def, title, q, ct).ConfigureAwait(false),
            "partner-balances" => await PartnerBalancesAsync(def, title, q, ct).ConfigureAwait(false),
            _ => throw Fail.NotFound(),
        };
    }

    public async Task<GeneratedFile> ExportAsync(string key, StandardReportQuery q, string? format, CancellationToken ct)
    {
        var r = await RunAsync(key, q, ct).ConfigureAwait(false);
        return await exporter.ExportAsync(ToTable(r), format, key, ct).ConfigureAwait(false);
    }

    /// <summary>A result as an export table (columns typed for printing).</summary>
    public static ReportTable ToTable(ReportResult r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new ReportTable(r.Title, r.Filters.Select(f => (f.Label, f.Value)).ToList(),
            r.Columns.Select(c => new ReportColumn(c.Header, c.Kind switch
            {
                "money" => ReportValue.Money,
                "number" or "quantity" or "percent" => ReportValue.Number,
                "date" => ReportValue.Date,
                _ => ReportValue.Text,
            })).ToList(), r.Rows.Select(x => x.Values).ToList(), r.Totals, r.Note);
    }

    // ============================================================ helpers

    private sealed record Filters(DateOnly From, DateOnly To, Guid? Customer, Guid? Driver, Guid? Partner, Guid? Loading, Guid? Unloading, Guid? Material);

    private async Task<Filters> FiltersAsync(StandardReportQuery q, CancellationToken ct)
    {
        var (from, to) = await ledger.PeriodAsync(q.From, q.To, ct).ConfigureAwait(false);
        if (to.Year - from.Year > MaxYears || to.Year > 9000) throw Fail.Field("to", ErrorCodes.ValidationRange, "to");   // review B10-7
        Guid? G<T>(string? token, string field) => string.IsNullOrWhiteSpace(token) ? null : support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
        return new Filters(from, to, G<Customer>(q.Customer, "customer"), G<Driver>(q.Driver, "driver"), G<Partner>(q.Partner, "partner"),
            G<LoadingSite>(q.LoadingSite, "loadingSite"), G<UnloadingSite>(q.UnloadingSite, "unloadingSite"), G<Material>(q.Material, "material"));
    }

    /// <summary>Active trips of the period with the common filters.</summary>
    private IQueryable<Trip> Trips(Filters f, bool period = true)
    {
        var q = db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active);
        if (period) q = q.Where(t => t.TripDate >= f.From && t.TripDate <= f.To);
        if (f.Customer is Guid c) q = q.Where(t => t.Customer.PublicId == c);
        if (f.Driver is Guid d) q = q.Where(t => t.Driver.PublicId == d);
        if (f.Partner is Guid p) q = q.Where(t => t.Partner != null && t.Partner.PublicId == p);
        if (f.Loading is Guid l) q = q.Where(t => t.LoadingSite.PublicId == l);
        if (f.Unloading is Guid u) q = q.Where(t => t.UnloadingSite.PublicId == u);
        if (f.Material is Guid m) q = q.Where(t => t.Material.PublicId == m);
        return q;
    }

    private List<ReportFilterValue> PeriodFilters(Filters f) => [new(L("الفترة", "Period"), D(f.From) + " – " + D(f.To))];

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private ResultColumn C(string key, string ar, string en, string kind = "text") => new(key, En ? en : ar, kind);

    private static DrillLink TripsLink(Filters f, params (string Key, string Value)[] extra)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal) { ["from"] = D(f.From), ["to"] = D(f.To) };
        foreach (var (k, v) in extra) d[k] = v;
        return new DrillLink("trips", null, d);
    }

    private static decimal Pct(decimal part, decimal whole) => whole == 0 ? 0 : Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);

    private static ReportResult Result(Def d, string title, List<ReportFilterValue> filters, List<ResultColumn> columns, List<ResultRow> rows, IReadOnlyList<object?>? totals,
        string? note = null) => new(d.Key, title, filters, columns, rows, totals, note, false);

    // ============================================================ RPT-02 unbilled trips

    private async Task<ReportResult> UnbilledAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var rows = await Trips(f, period: false).Where(t => t.InvoiceId == null && t.TripDate <= f.To)
            .GroupBy(t => new { t.CustomerId, t.Customer.PublicId, t.Customer.Name })
            .Select(g => new { g.Key.PublicId, g.Key.Name, Trips = g.Count(), Value = g.Sum(t => t.Revenue), Oldest = g.Min(t => t.TripDate) })
            .OrderBy(x => x.Oldest).ToListAsync(ct).ConfigureAwait(false);
        var result = rows.Select(x => new ResultRow([x.Name, x.Trips, x.Value, x.Oldest, today.DayNumber - x.Oldest.DayNumber],
            new DrillLink("trips", null, new Dictionary<string, string> { ["customer"] = support.Token<Customer>(x.PublicId), ["billing"] = "unbilled", ["to"] = D(f.To) }))).ToList();
        return Result(d, title, [new(L("حتى", "Up to"), D(f.To))],
            [C("customer", "العميل", "Customer"), C("trips", "الرحلات", "Trips", "number"), C("value", "القيمة", "Value", "money"), C("oldest", "أقدم رحلة", "Oldest trip", "date"),
             C("age", "العمر (يوم)", "Age (days)", "number")],
            result, [L("الإجمالي", "Total"), rows.Sum(x => x.Trips), rows.Sum(x => x.Value), null, null]);
    }

    // ============================================================ RPT-03 shortage

    private async Task<ReportResult> ShortageAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).Where(t => t.IsShortageFlagged)
            .GroupBy(t => new { DriverPid = t.Driver.PublicId, Driver = t.Driver.FullName, SitePid = t.LoadingSite.PublicId, Site = t.LoadingSite.Name })
            .Select(g => new { g.Key.DriverPid, g.Key.Driver, g.Key.SitePid, g.Key.Site, Trips = g.Count(), Loaded = g.Sum(t => t.LoadedWeight), Shortage = g.Sum(t => t.ShortageWeight ?? 0) })
            .OrderByDescending(x => x.Shortage).ToListAsync(ct).ConfigureAwait(false);
        var result = rows.Select(x => new ResultRow([x.Driver, x.Site, x.Trips, x.Loaded, x.Shortage, Pct(x.Shortage, x.Loaded)],
            TripsLink(f, ("driver", support.Token<Driver>(x.DriverPid)), ("loadingSite", support.Token<LoadingSite>(x.SitePid)), ("shortage", "true")))).ToList();
        var loaded = rows.Sum(x => x.Loaded);
        var shortage = rows.Sum(x => x.Shortage);
        return Result(d, title, PeriodFilters(f),
            [C("driver", "السائق", "Driver"), C("loadingSite", "موقع التحميل", "Loading site"), C("trips", "رحلات مؤشّرة", "Flagged trips", "number"),
             C("loaded", "المحمّل (طن)", "Loaded (t)", "quantity"), C("shortage", "النقص (طن)", "Shortage (t)", "quantity"), C("pct", "النقص %", "Shortage %", "percent")],
            result, [L("الإجمالي", "Total"), null, rows.Sum(x => x.Trips), loaded, shortage, Pct(shortage, loaded)]);
    }

    // ============================================================ RPT-04 tariff breach (FR-023, FR-024)

    private async Task<ReportResult> BreachAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        // Trips store the customer tariff price only (FR-022, FR-023): the breach is the customer price against it; "partner" lists the breaches
        // of the partner's trips (DT-119, review B10-1).
        var side = (q.Side?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "customer" or "customers" => "customer",
            "partner" or "partners" => "partner",
            _ => throw Fail.Field("side", ErrorCodes.ValidationFormat, "side"),
        };
        var query = Trips(f).Where(t => t.IsTariffBreach && t.TariffPrice != null);
        if (side == "partner") query = query.Where(t => t.PartnerId != null);
        var rows = await query.OrderBy(t => t.TripDate).ThenBy(t => t.Id)
            .Select(t => new { t.PublicId, t.ReceiptNo, t.TripDate, Customer = t.Customer.Name, Partner = t.Partner != null ? t.Partner.Name : null, From = t.LoadingSite.Name,
                To = t.UnloadingSite.Name, t.CustomerPrice, Tariff = t.TariffPrice!.Value, t.BillingQty })
            .Take(SheetData.MaxRows + 1).ToListAsync(ct).ConfigureAwait(false);
        var decimals = await ledger_Decimals(ct).ConfigureAwait(false);
        var items = rows.Take(SheetData.MaxRows).Select(x => (x, Amount: Accounting.Money.Round((x.CustomerPrice - x.Tariff) * x.BillingQty, decimals))).ToList();
        var loss = items.Where(i => i.Amount < 0).Sum(i => i.Amount);
        var excess = items.Where(i => i.Amount > 0).Sum(i => i.Amount);
        var filters = PeriodFilters(f);
        filters.Add(new(L("الطرف", "Side"), side == "partner" ? L("الشركاء", "Partners") : L("العملاء", "Customers")));
        return new ReportResult(d.Key, title, filters,
            [C("receipt", "السند", "Receipt"), C("date", "التاريخ", "Date", "date"), C(side, side == "partner" ? "الشريك" : "العميل", side == "partner" ? "Partner" : "Customer"),
             C("route", "المسار", "Route"), C("price", "السعر", "Price", "money"), C("tariff", "سعر التعرفة", "Tariff price", "money"), C("qty", "كمية الفوترة", "Billing quantity", "quantity"),
             C("breach", "المخالفة", "Breach", "money")],
            items.Select(i => new ResultRow([i.x.ReceiptNo, i.x.TripDate, side == "partner" ? i.x.Partner : i.x.Customer, i.x.From + " → " + i.x.To, i.x.CustomerPrice, i.x.Tariff,
                i.x.BillingQty, i.Amount], new DrillLink("trip", support.Token<Trip>(i.x.PublicId), null))).ToList(),
            [L("الصافي", "Net"), null, null, null, null, null, null, loss + excess],
            L("الخسارة ", "Loss ") + Accounting.Money.Format(loss) + " · " + L("الزيادة ", "Excess ") + Accounting.Money.Format(excess), rows.Count > SheetData.MaxRows);
    }

    private async Task<int> ledger_Decimals(CancellationToken ct) =>
        await db.Set<Domain.Core.Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => (int)c.CurrencyDecimals).SingleAsync(ct).ConfigureAwait(false);

    // ============================================================ RPT-05 … RPT-10 groupings of trips

    private sealed record Agg(int Trips, decimal Loaded, decimal Revenue, decimal Wage, decimal PartnerCost, decimal Shortage);

    private async Task<ReportResult> ProductivityAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).GroupBy(t => new { t.Driver.PublicId, t.Driver.FullName, t.FleetClass })
            .Select(g => new { g.Key.PublicId, g.Key.FullName, g.Key.FleetClass, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage),
                g.Sum(t => t.PartnerCost), g.Sum(t => t.ShortageWeight ?? 0)) })
            .ToListAsync(ct).ConfigureAwait(false);
        rows = rows.OrderByDescending(x => x.A.Revenue).ToList();
        var t = Sum(rows.Select(x => x.A));
        return Result(d, title, PeriodFilters(f),
            [C("driver", "السائق", "Driver"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"), C("revenue", "الإيراد", "Revenue", "money"),
             C("wage", "الأجر", "Wage", "money"), C("margin", "الهامش", "Margin", "money"), C("shortage", "النقص %", "Shortage %", "percent")],
            rows.Select(x => new ResultRow([x.FullName, x.A.Trips, x.A.Loaded, x.A.Revenue, x.A.Wage, x.A.Revenue - x.A.Wage - x.A.PartnerCost, Pct(x.A.Shortage, x.A.Loaded)],
                TripsLink(f, ("driver", support.Token<Driver>(x.PublicId))))).ToList(),
            [L("الإجمالي", "Total"), t.Trips, t.Loaded, t.Revenue, t.Wage, t.Revenue - t.Wage - t.PartnerCost, Pct(t.Shortage, t.Loaded)]);
    }

    private static Agg Sum(IEnumerable<Agg> items)
    {
        var list = items.ToList();
        return new Agg(list.Sum(a => a.Trips), list.Sum(a => a.Loaded), list.Sum(a => a.Revenue), list.Sum(a => a.Wage), list.Sum(a => a.PartnerCost), list.Sum(a => a.Shortage));
    }

    private async Task<ReportResult> CustomerRevenueAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).GroupBy(t => new { t.CustomerId, t.Customer.PublicId, t.Customer.Name })
            .Select(g => new { g.Key.CustomerId, g.Key.PublicId, g.Key.Name, Trips = g.Count(), Tons = g.Sum(t => t.LoadedWeight), Revenue = g.Sum(t => t.Revenue) })
            .ToListAsync(ct).ConfigureAwait(false);
        // Discount = discounts of the customer's approved invoices dated in the period (net of credit-note discount shares).
        var invoices = db.Set<Invoice>().AsNoTracking().Where(i => (i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed) && i.InvoiceDate >= f.From && i.InvoiceDate <= f.To);
        if (f.Customer is Guid c) invoices = invoices.Where(i => i.Customer.PublicId == c);
        var discounts = await invoices.GroupBy(i => i.CustomerId).Select(g => new { g.Key, D = g.Sum(i => i.Discount) }).ToDictionaryAsync(x => x.Key, x => x.D, ct).ConfigureAwait(false);
        var credits = await db.Set<CreditNote>().AsNoTracking().Where(n => n.Status == InvoiceCodes.Posted && n.CreditDate >= f.From && n.CreditDate <= f.To)
            .GroupBy(n => n.CustomerId).Select(g => new { g.Key, D = g.Sum(n => n.DiscountShare) }).ToDictionaryAsync(x => x.Key, x => x.D, ct).ConfigureAwait(false);
        var list = rows.Select(x => (x, Discount: discounts.GetValueOrDefault(x.CustomerId) - credits.GetValueOrDefault(x.CustomerId))).OrderByDescending(x => x.x.Revenue).ToList();
        return Result(d, title, PeriodFilters(f),
            [C("customer", "العميل", "Customer"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"), C("revenue", "الإيراد", "Revenue", "money"),
             C("discount", "الخصم", "Discount", "money"), C("net", "الصافي", "Net", "money")],
            list.Select(x => new ResultRow([x.x.Name, x.x.Trips, x.x.Tons, x.x.Revenue, x.Discount, x.x.Revenue - x.Discount], TripsLink(f, ("customer", support.Token<Customer>(x.x.PublicId))))).ToList(),
            [L("الإجمالي", "Total"), list.Sum(x => x.x.Trips), list.Sum(x => x.x.Tons), list.Sum(x => x.x.Revenue), list.Sum(x => x.Discount), list.Sum(x => x.x.Revenue - x.Discount)]);
    }

    private async Task<ReportResult> RoutesAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).GroupBy(t => new { L = t.LoadingSite.PublicId, LN = t.LoadingSite.Name, U = t.UnloadingSite.PublicId, UN = t.UnloadingSite.Name })
            .Select(g => new { g.Key.L, g.Key.LN, g.Key.U, g.Key.UN, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
            .ToListAsync(ct).ConfigureAwait(false);
        rows = rows.OrderByDescending(x => x.A.Revenue).ToList();
        var t = Sum(rows.Select(x => x.A));
        return Result(d, title, PeriodFilters(f),
            [C("route", "المسار", "Route"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"), C("revenue", "الإيراد", "Revenue", "money"),
             C("cost", "التكلفة", "Cost", "money"), C("margin", "الهامش", "Margin", "money")],
            rows.Select(x => new ResultRow([x.LN + " → " + x.UN, x.A.Trips, x.A.Loaded, x.A.Revenue, x.A.Wage + x.A.PartnerCost, x.A.Revenue - x.A.Wage - x.A.PartnerCost],
                TripsLink(f, ("loadingSite", support.Token<LoadingSite>(x.L)), ("unloadingSite", support.Token<UnloadingSite>(x.U))))).ToList(),
            [L("الإجمالي", "Total"), t.Trips, t.Loaded, t.Revenue, t.Wage + t.PartnerCost, t.Revenue - t.Wage - t.PartnerCost]);
    }

    private async Task<ReportResult> MaterialsAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).GroupBy(t => new { t.Material.PublicId, t.Material.Name, t.TripDate.Year, t.TripDate.Month })
            .Select(g => new { g.Key.PublicId, g.Key.Name, g.Key.Year, g.Key.Month, Trips = g.Count(), Tons = g.Sum(t => t.LoadedWeight), Revenue = g.Sum(t => t.Revenue) })
            .OrderBy(x => x.Name).ThenBy(x => x.Year).ThenBy(x => x.Month).ToListAsync(ct).ConfigureAwait(false);
        return Result(d, title, PeriodFilters(f),
            [C("material", "المادة", "Material"), C("month", "الشهر", "Month"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"),
             C("revenue", "الإيراد", "Revenue", "money")],
            rows.Select(x => new ResultRow([x.Name, $"{x.Year:D4}-{x.Month:D2}", x.Trips, x.Tons, x.Revenue], TripsLink(f, ("material", support.Token<Material>(x.PublicId))))).ToList(),
            [L("الإجمالي", "Total"), null, rows.Sum(x => x.Trips), rows.Sum(x => x.Tons), rows.Sum(x => x.Revenue)]);
    }

    private async Task<ReportResult> PartnerShipmentsAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var rows = await Trips(f).Where(t => t.PartnerId != null).GroupBy(t => new { Id = t.PartnerId!.Value, t.Partner!.PublicId, t.Partner.Name })
            .Select(g => new { g.Key.Id, g.Key.PublicId, g.Key.Name, Trips = g.Count(), Tons = g.Sum(t => t.LoadedWeight), Cost = g.Sum(t => t.PartnerCost) })
            .ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.Id).ToList();
        var paid = await db.Set<PartnerPayment>().AsNoTracking().Where(p => ids.Contains(p.PartnerId) && p.Status == CashCodes.Active && p.PaymentDate >= f.From && p.PaymentDate <= f.To)
            .GroupBy(p => p.PartnerId).Select(g => new { g.Key, A = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var balances = await PartnerBalanceAsync(ids, f.To, ct).ConfigureAwait(false);
        var list = rows.OrderByDescending(r => r.Cost).ToList();
        return Result(d, title, PeriodFilters(f),
            [C("partner", "الشريك", "Partner"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"), C("cost", "التكلفة", "Cost", "money"),
             C("payments", "المدفوعات", "Payments", "money"), C("balance", "الرصيد المستحق", "Balance due", "money")],
            list.Select(x => new ResultRow([x.Name, x.Trips, x.Tons, x.Cost, paid.GetValueOrDefault(x.Id), balances.GetValueOrDefault(x.Id)],
                new DrillLink("partner-account", support.Token<Partner>(x.PublicId), null))).ToList(),
            [L("الإجمالي", "Total"), list.Sum(x => x.Trips), list.Sum(x => x.Tons), list.Sum(x => x.Cost), list.Sum(x => paid.GetValueOrDefault(x.Id)),
             list.Sum(x => balances.GetValueOrDefault(x.Id))]);
    }

    /// <summary>What the company owes each partner as of a date: partner cost of active trips − active payments (DT-99).</summary>
    private async Task<Dictionary<long, decimal>> PartnerBalanceAsync(IReadOnlyCollection<long>? ids, DateOnly asOf, CancellationToken ct)
    {
        var trips = db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active && t.PartnerId != null && t.TripDate <= asOf);
        var pays = db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Status == CashCodes.Active && p.PaymentDate <= asOf);
        if (ids is not null)
        {
            trips = trips.Where(t => ids.Contains(t.PartnerId!.Value));
            pays = pays.Where(p => ids.Contains(p.PartnerId));
        }
        var cost = await trips.GroupBy(t => t.PartnerId!.Value).Select(g => new { g.Key, A = g.Sum(t => t.PartnerCost) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var paid = await pays.GroupBy(p => p.PartnerId).Select(g => new { g.Key, A = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        return cost.Keys.Union(paid.Keys).ToDictionary(k => k, k => cost.GetValueOrDefault(k) - paid.GetValueOrDefault(k));
    }

    private async Task<ReportResult> MarginAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var by = (q.GroupBy?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "customer" => "customer",
            "route" => "route",
            "driver" => "driver",
            "month" => "month",
            _ => throw Fail.Field("groupBy", ErrorCodes.ValidationFormat, "groupBy"),
        };
        var trips = Trips(f);
        List<(string Label, DrillLink Link, Agg A)> rows = by switch
        {
            "customer" => (await trips.GroupBy(t => new { t.Customer.PublicId, t.Customer.Name })
                    .Select(g => new { g.Key.PublicId, g.Key.Name, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.Name, TripsLink(f, ("customer", support.Token<Customer>(x.PublicId))), x.A)).ToList(),
            "driver" => (await trips.GroupBy(t => new { t.Driver.PublicId, t.Driver.FullName })
                    .Select(g => new { g.Key.PublicId, g.Key.FullName, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.FullName, TripsLink(f, ("driver", support.Token<Driver>(x.PublicId))), x.A)).ToList(),
            "route" => (await trips.GroupBy(t => new { L = t.LoadingSite.PublicId, LN = t.LoadingSite.Name, U = t.UnloadingSite.PublicId, UN = t.UnloadingSite.Name })
                    .Select(g => new { g.Key.L, g.Key.LN, g.Key.U, g.Key.UN, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
                    .ToListAsync(ct).ConfigureAwait(false))
                .Select(x => (x.LN + " → " + x.UN, TripsLink(f, ("loadingSite", support.Token<LoadingSite>(x.L)), ("unloadingSite", support.Token<UnloadingSite>(x.U))), x.A)).ToList(),
            _ => (await trips.GroupBy(t => new { t.TripDate.Year, t.TripDate.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
                    .ToListAsync(ct).ConfigureAwait(false))
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .Select(x => ($"{x.Year:D4}-{x.Month:D2}", TripsLink(f, ("from", D(new DateOnly(x.Year, x.Month, 1))), ("to", D(new DateOnly(x.Year, x.Month, 1).AddMonths(1).AddDays(-1)))), x.A))
                .ToList(),
        };
        if (by != "month") rows = rows.OrderByDescending(r => r.A.Revenue - r.A.Wage - r.A.PartnerCost).ToList();
        var t = Sum(rows.Select(r => r.A));
        var filters = PeriodFilters(f);
        filters.Add(new(L("التجميع", "Grouped by"), by));
        return Result(d, title, filters,
            [C(by, L("البند", "Item"), L("البند", "Item")), C("revenue", "الإيراد", "Revenue", "money"), C("wages", "أجور السائقين", "Driver wages", "money"),
             C("partnerCosts", "تكلفة الشركاء", "Partner costs", "money"), C("margin", "الهامش", "Margin", "money"), C("marginPct", "الهامش %", "Margin %", "percent")],
            rows.Select(r => new ResultRow([r.Label, r.A.Revenue, r.A.Wage, r.A.PartnerCost, r.A.Revenue - r.A.Wage - r.A.PartnerCost, Pct(r.A.Revenue - r.A.Wage - r.A.PartnerCost, r.A.Revenue)], r.Link)).ToList(),
            [L("الإجمالي", "Total"), t.Revenue, t.Wage, t.PartnerCost, t.Revenue - t.Wage - t.PartnerCost, Pct(t.Revenue - t.Wage - t.PartnerCost, t.Revenue)]);
    }

    // ============================================================ RPT-11 monthly summary

    private async Task<ReportResult> MonthlyAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var trips = await Trips(f).GroupBy(t => new { t.TripDate.Year, t.TripDate.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, A = new Agg(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), 0) })
            .ToListAsync(ct).ConfigureAwait(false);
        var invoiced = await db.Set<Invoice>().AsNoTracking().Where(i => (i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed) && i.InvoiceDate >= f.From && i.InvoiceDate <= f.To)
            .GroupBy(i => new { i.InvoiceDate.Year, i.InvoiceDate.Month }).Select(g => new { g.Key.Year, g.Key.Month, A = g.Sum(i => i.Total) }).ToListAsync(ct).ConfigureAwait(false);
        var collected = await db.Set<Receipt>().AsNoTracking().Where(r => r.Status == CashCodes.Active && r.ReceiptDate >= f.From && r.ReceiptDate <= f.To)
            .GroupBy(r => new { r.ReceiptDate.Year, r.ReceiptDate.Month }).Select(g => new { g.Key.Year, g.Key.Month, A = g.Sum(r => r.Amount) }).ToListAsync(ct).ConfigureAwait(false);
        var months = new List<(int Year, int Month)>();
        for (var m = new DateOnly(f.From.Year, f.From.Month, 1); m <= f.To; m = m.AddMonths(1)) months.Add((m.Year, m.Month));
        var rows = months.Select(m =>
        {
            var a = trips.FirstOrDefault(t => t.Year == m.Year && t.Month == m.Month)?.A ?? new Agg(0, 0, 0, 0, 0, 0);
            var inv = invoiced.FirstOrDefault(i => i.Year == m.Year && i.Month == m.Month)?.A ?? 0;
            var col = collected.FirstOrDefault(c => c.Year == m.Year && c.Month == m.Month)?.A ?? 0;
            var first = new DateOnly(m.Year, m.Month, 1);
            return (m, a, inv, col, Link: TripsLink(f, ("from", D(first < f.From ? f.From : first)), ("to", D(first.AddMonths(1).AddDays(-1) > f.To ? f.To : first.AddMonths(1).AddDays(-1)))));
        }).ToList();
        var t = Sum(rows.Select(r => r.a));
        return Result(d, title, PeriodFilters(f),
            [C("month", "الشهر", "Month"), C("trips", "الرحلات", "Trips", "number"), C("tons", "الأطنان", "Tons", "quantity"), C("revenue", "الإيراد", "Revenue", "money"),
             C("costs", "التكاليف", "Costs", "money"), C("margin", "الهامش", "Margin", "money"), C("invoiced", "المفوتر", "Invoiced", "money"), C("collected", "المحصّل", "Collected", "money")],
            rows.Select(r => new ResultRow([$"{r.m.Year:D4}-{r.m.Month:D2}", r.a.Trips, r.a.Loaded, r.a.Revenue, r.a.Wage + r.a.PartnerCost, r.a.Revenue - r.a.Wage - r.a.PartnerCost, r.inv, r.col],
                r.Link)).ToList(),
            [L("الإجمالي", "Total"), t.Trips, t.Loaded, t.Revenue, t.Wage + t.PartnerCost, t.Revenue - t.Wage - t.PartnerCost, rows.Sum(r => r.inv), rows.Sum(r => r.col)]);
    }

    // ============================================================ RPT-12 customer statement (7.7.3)

    private async Task<ReportResult> CustomerStatementAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Customer)) throw Fail.Field("customer", ErrorCodes.ValidationRequired, "customer");
        var customer = await support.FindAsync<Customer>(q.Customer, ct).ConfigureAwait(false);
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var invoices = db.Set<Invoice>().AsNoTracking().Where(i => i.CustomerId == customer.Id && (i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed));
        var credits = db.Set<CreditNote>().AsNoTracking().Where(n => n.CustomerId == customer.Id && n.Status == InvoiceCodes.Posted);
        var receipts = db.Set<Receipt>().AsNoTracking().Where(r => r.CustomerId == customer.Id && r.Status == CashCodes.Active);
        var opening = await invoices.Where(i => i.InvoiceDate < f.From).SumAsync(i => i.Total, ct).ConfigureAwait(false)
            - await credits.Where(n => n.CreditDate < f.From).SumAsync(n => n.Total, ct).ConfigureAwait(false)
            - await receipts.Where(r => r.ReceiptDate < f.From).SumAsync(r => r.Amount, ct).ConfigureAwait(false);
        var events = (await invoices.Where(i => i.InvoiceDate >= f.From && i.InvoiceDate <= f.To).Select(i => new { i.Id, i.PublicId, Number = i.Number!, Date = i.InvoiceDate, Kind = "invoice", Debit = i.Total, Credit = 0m, Unapplied = false }).ToListAsync(ct).ConfigureAwait(false))
            .Concat(await credits.Where(n => n.CreditDate >= f.From && n.CreditDate <= f.To).Select(n => new { n.Id, n.PublicId, n.Number, Date = n.CreditDate, Kind = "credit-note", Debit = 0m, Credit = n.Total, Unapplied = false }).ToListAsync(ct).ConfigureAwait(false))
            .Concat(await receipts.Where(r => r.ReceiptDate >= f.From && r.ReceiptDate <= f.To).Select(r => new { r.Id, r.PublicId, r.Number, Date = r.ReceiptDate, Kind = "receipt", Debit = 0m, Credit = r.Amount, Unapplied = !r.Allocations.Any() }).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(e => e.Date).ThenBy(e => e.Kind == "invoice" ? 0 : 1).ThenBy(e => e.Id).ToList();
        var running = opening;
        var rows = new List<ResultRow> { new([f.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, opening], null) };
        foreach (var e in events)
        {
            running += e.Debit - e.Credit;
            var label = e.Kind switch
            {
                "invoice" => L("فاتورة", "Invoice"),
                "credit-note" => L("إشعار دائن", "Credit note"),
                _ => e.Unapplied ? L("سند قبض على الحساب (غير مخصص)", "Receipt on account (unapplied)") : L("سند قبض", "Receipt"),
            };
            var link = e.Kind switch
            {
                "invoice" => new DrillLink("invoice", support.Token<Invoice>(e.PublicId), null),
                "credit-note" => new DrillLink("credit-note", support.Token<CreditNote>(e.PublicId), null),
                _ => new DrillLink("receipt", support.Token<Receipt>(e.PublicId), null),
            };
            rows.Add(new ResultRow([e.Date, e.Number, label, e.Debit == 0 ? null : e.Debit, e.Credit == 0 ? null : e.Credit, running], link));
        }
        var unapplied = await receipts.Where(r => r.ReceiptDate <= f.To).SumAsync(r => r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0), ct).ConfigureAwait(false);
        var filters = PeriodFilters(f);
        filters.Insert(0, new(L("العميل", "Customer"), customer.Name));
        return new ReportResult(d.Key, title, filters,
            [C("date", "التاريخ", "Date", "date"), C("document", "المستند", "Document"), C("description", "البيان", "Description"), C("debit", "مدين", "Debit", "money"),
             C("credit", "دائن", "Credit", "money"), C("balance", "الرصيد", "Balance", "money")],
            rows, [null, null, L("الإجمالي والرصيد الختامي", "Totals and closing balance"), events.Sum(e => e.Debit), events.Sum(e => e.Credit), running],
            L("مقبوضات غير مخصصة حتى نهاية الفترة: ", "Unapplied receipts up to the period end: ") + Accounting.Money.Format(unapplied), false);
    }

    // ============================================================ RPT-24 supplier statement (owner decision 17 Oct 2026)

    /// <summary>
    /// A supplier's bills (amount, tax, deduction, net = payable), returns (the same, negative) and payments (net only), with the running
    /// balance owed to the supplier, the opening balance before the period and totals.
    /// </summary>
    private async Task<ReportResult> SupplierStatementAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q.Supplier)) throw Fail.Field("supplier", ErrorCodes.ValidationRequired, "supplier");
        var supplier = await support.FindAsync<ApArParty>(q.Supplier, ct).ConfigureAwait(false);
        if (supplier.PartyRole != "Supplier") throw Fail.Field("supplier", ErrorCodes.ValidationFormat, "supplier");
        var f = await FiltersAsync(q, ct).ConfigureAwait(false);
        var bills = db.Set<ApArDocument>().AsNoTracking().Where(x => x.PartyId == supplier.Id && x.DocumentKind == ApArCodes.SupplierBill && x.Status == ApArCodes.Posted);
        var returns = db.Set<ApArReturn>().AsNoTracking().Where(x => x.Document.PartyId == supplier.Id && x.Document.DocumentKind == ApArCodes.SupplierBill && x.Status == ApArCodes.Posted);
        var payments = db.Set<ApArPayment>().AsNoTracking().Where(x => x.Document.PartyId == supplier.Id && x.Document.DocumentKind == ApArCodes.SupplierBill && x.Status == CashCodes.Active);
        var opening = await bills.Where(x => x.DocumentDate < f.From).SumAsync(x => x.Total, ct).ConfigureAwait(false)
            - await returns.Where(x => x.ReturnDate < f.From).SumAsync(x => x.Total, ct).ConfigureAwait(false)
            - await payments.Where(x => x.PaymentDate < f.From).SumAsync(x => x.Amount, ct).ConfigureAwait(false);
        var events = (await bills.Where(x => x.DocumentDate >= f.From && x.DocumentDate <= f.To)
                .Select(x => new { x.Id, x.PublicId, x.Number, Date = x.DocumentDate, Kind = "bill", x.Reference, Amount = (decimal?)x.Subtotal, Tax = (decimal?)x.Tax, Deduction = (decimal?)x.Deduction, Net = x.Total })
                .ToListAsync(ct).ConfigureAwait(false))
            .Concat(await returns.Where(x => x.ReturnDate >= f.From && x.ReturnDate <= f.To)
                .Select(x => new { x.Id, x.PublicId, x.Number, Date = x.ReturnDate, Kind = "return", Reference = (string?)x.Document.Number, Amount = (decimal?)(-x.Amount), Tax = (decimal?)(-x.Tax), Deduction = (decimal?)(-x.Deduction), Net = -x.Total })
                .ToListAsync(ct).ConfigureAwait(false))
            .Concat(await payments.Where(x => x.PaymentDate >= f.From && x.PaymentDate <= f.To)
                .Select(x => new { x.Id, x.PublicId, x.Number, Date = x.PaymentDate, Kind = "payment", Reference = (string?)x.Document.Number, Amount = (decimal?)null, Tax = (decimal?)null, Deduction = (decimal?)null, Net = -x.Amount })
                .ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(e => e.Date).ThenBy(e => e.Kind == "bill" ? 0 : 1).ThenBy(e => e.Id).ToList();
        var running = opening;
        var rows = new List<ResultRow> { new([f.From, null, L("رصيد افتتاحي", "Opening balance"), null, null, null, null, opening], null) };
        foreach (var e in events)
        {
            running += e.Net;
            var label = e.Kind switch
            {
                "bill" => L("فاتورة مورد", "Supplier bill") + (e.Reference is null ? string.Empty : " · " + e.Reference),
                "return" => L("مرتجع مشتريات", "Purchase return") + " · " + e.Reference,
                _ => L("دفعة", "Payment") + " · " + e.Reference,
            };
            var link = e.Kind switch
            {
                "bill" => new DrillLink("supplier-bill", support.Token<ApArDocument>(e.PublicId), null),
                "return" => new DrillLink("apar-return", support.Token<ApArReturn>(e.PublicId), null),
                _ => new DrillLink("apar-payment", support.Token<ApArPayment>(e.PublicId), null),
            };
            rows.Add(new ResultRow([e.Date, e.Number, label, e.Amount, e.Tax, e.Deduction, e.Net, running], link));
        }
        var filters = PeriodFilters(f);
        filters.Insert(0, new(L("المورد", "Supplier"), supplier.Name));
        return new ReportResult(d.Key, title, filters,
            [C("date", "التاريخ", "Date", "date"), C("document", "المستند", "Document"), C("description", "البيان", "Description"), C("amount", "المبلغ", "Amount", "money"),
             C("tax", "الضريبة", "Tax", "money"), C("deduction", "الخصم", "Deduction", "money"), C("net", "الصافي", "Net", "money"), C("balance", "الرصيد المستحق", "Balance owed", "money")],
            rows, [null, null, L("الإجمالي والرصيد الختامي", "Totals and closing balance"), events.Sum(e => e.Amount ?? 0), events.Sum(e => e.Tax ?? 0),
                events.Sum(e => e.Deduction ?? 0), events.Sum(e => e.Net), running], null, false);
    }

    // ============================================================ RPT-19 / RPT-20 balances

    private async Task<ReportResult> DriverBalancesAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var asOf = q.AsOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        // Open = not settled: wages of own-fleet active trips and active advances without a settlement, up to the date (8.5, DT-93).
        // Open as of the date: not settled, or settled by a settlement dated after it or voided (review B10-2).
        var wages = await db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active && t.PartnerId == null && t.TripDate <= asOf && t.DriverWage != 0
                && (t.SettlementId == null || t.Settlement!.Status != Active || t.Settlement.SettlementDate > asOf))
            .GroupBy(t => t.DriverId).Select(g => new { g.Key, A = g.Sum(t => t.DriverWage), N = g.Count() }).ToListAsync(ct).ConfigureAwait(false);
        var advances = await db.Set<Advance>().AsNoTracking().Where(a => a.Status == CashCodes.Active && a.AdvanceDate <= asOf
                && (a.SettlementId == null || a.Settlement!.Status != Active || a.Settlement.SettlementDate > asOf))
            .GroupBy(a => a.DriverId).Select(g => new { g.Key, A = g.Sum(a => a.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var ids = wages.Select(w => w.Key).Union(advances.Keys).ToList();
        var drivers = await db.Set<Driver>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.FullName }).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var rows = ids.Where(drivers.ContainsKey).Select(id =>
        {
            var w = wages.FirstOrDefault(x => x.Key == id);
            var wage = w?.A ?? 0;
            var adv = advances.GetValueOrDefault(id);
            return (Driver: drivers[id], Trips: w?.N ?? 0, Wage: wage, Advances: adv, Net: wage - adv);
        }).OrderByDescending(r => r.Net).ToList();
        return Result(d, title, [new(L("كما في", "As of"), D(asOf))],
            [C("driver", "السائق", "Driver"), C("trips", "رحلات مفتوحة", "Open trips", "number"), C("wages", "أجور مفتوحة", "Open wages", "money"),
             C("advances", "سلف مفتوحة", "Open advances", "money"), C("net", "الصافي المستحق", "Net due", "money")],
            rows.Select(r => new ResultRow([r.Driver.FullName, r.Trips, r.Wage, r.Advances, r.Net], new DrillLink("driver-account", support.Token<Driver>(r.Driver.PublicId), null))).ToList(),
            [L("الإجمالي", "Total"), rows.Sum(r => r.Trips), rows.Sum(r => r.Wage), rows.Sum(r => r.Advances), rows.Sum(r => r.Net)]);
    }

    private async Task<ReportResult> PartnerBalancesAsync(Def d, string title, StandardReportQuery q, CancellationToken ct)
    {
        var asOf = q.AsOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var cost = await db.Set<Trip>().AsNoTracking().Where(t => t.Status == Active && t.PartnerId != null && t.TripDate <= asOf)
            .GroupBy(t => t.PartnerId!.Value).Select(g => new { g.Key, A = g.Sum(t => t.PartnerCost) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var paid = await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.Status == CashCodes.Active && p.PaymentDate <= asOf)
            .GroupBy(p => p.PartnerId).Select(g => new { g.Key, A = g.Sum(p => p.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct).ConfigureAwait(false);
        var ids = cost.Keys.Union(paid.Keys).ToList();
        var partners = await db.Set<Partner>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.Name }).ToDictionaryAsync(x => x.Id, ct).ConfigureAwait(false);
        var rows = ids.Where(partners.ContainsKey).Select(id => (P: partners[id], Cost: cost.GetValueOrDefault(id), Paid: paid.GetValueOrDefault(id)))
            .OrderByDescending(r => r.Cost - r.Paid).ToList();
        return Result(d, title, [new(L("كما في", "As of"), D(asOf))],
            [C("partner", "الشريك", "Partner"), C("cost", "التكلفة", "Cost", "money"), C("payments", "المدفوعات", "Payments", "money"), C("balance", "الرصيد المستحق", "Balance due", "money")],
            rows.Select(r => new ResultRow([r.P.Name, r.Cost, r.Paid, r.Cost - r.Paid], new DrillLink("partner-account", support.Token<Partner>(r.P.PublicId), null))).ToList(),
            [L("الإجمالي", "Total"), rows.Sum(r => r.Cost), rows.Sum(r => r.Paid), rows.Sum(r => r.Cost - r.Paid)]);
    }
}
