using System.Globalization;
using System.Net;
using System.Text;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>
/// Printed invoices and credit notes (FR-053 … FR-058, UX-22, UX-23): HTML in A4, right-to-left Arabic (or English), company header
/// with logo and tax numbers, customer details with tax number, number and date, lines or the claim's grouped totals followed by the
/// shipment annex, totals with tax, amount in words, e-invoice QR, signature footer, page X of Y; DRAFT and VOID stamps. The same HTML
/// becomes the PDF through <see cref="IPdfRenderer"/>, so preview, PDF and printout are identical. Every print is logged.
/// </summary>
public sealed class InvoicePrintService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, InvoiceService invoices, InvoiceLayoutService layouts,
    DocumentPrinter printer, IQrCodeRenderer qr, AuditTrail audit, IssuedNames names)
{
    private bool En => ctx.Language == "en";

    public async Task<GeneratedFile> InvoiceAsync(string token, string? format, string? layoutType, CancellationToken ct)
    {
        var inv = await support.FindAsync<Invoice>(token, ct).ConfigureAwait(false);
        var details = await invoices.DetailsAsync(inv.Id, ct).ConfigureAwait(false);
        var type = string.IsNullOrWhiteSpace(layoutType) ? inv.LayoutType : layoutType;
        var layout = await layouts.GetAsync(type, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
        var customer = await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).SingleAsync(c => c.Id == inv.CustomerId, ct).ConfigureAwait(false);
        (details, customer.Name) = await AsIssuedAsync(inv, details, customer, ct).ConfigureAwait(false);
        var qrText = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(s => s.InvoiceId == inv.Id && s.Status == "Submitted").OrderByDescending(s => s.Id)
            .Select(s => s.QrCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var html = InvoiceHtml(details, layout, company, customer, qrText);
        var stamp = inv.Status == InvoiceCodes.Draft ? "Draft" : inv.Status == InvoiceCodes.Void ? "Void" : null;
        audit.Record(InvoiceService.EntityType, inv.Id, "Print", $"{layout.LayoutType}{(stamp is null ? string.Empty : " · " + stamp)}");
        return await OutputAsync(html, format, $"{inv.Number ?? "draft"}", ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> CreditNoteAsync(string token, string? format, CreditNoteService credits, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credits);
        var cn = await support.FindAsync<CreditNote>(token, ct).ConfigureAwait(false);
        var details = await credits.DetailsAsync(cn.Id, ct).ConfigureAwait(false);
        var inv = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == cn.InvoiceId, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
        var customer = await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).SingleAsync(c => c.Id == cn.CustomerId, ct).ConfigureAwait(false);
        customer.Name = await names.OfAsync("TRN.Customer", customer.Id, "Name", customer.Name, cn.CreatedAt, ct).ConfigureAwait(false);   // FR-163
        var layout = await layouts.GetAsync(InvoiceCodes.TaxInvoice, ct).ConfigureAwait(false);
        var qrText = await db.Set<EInvoiceSubmission>().AsNoTracking().Where(s => s.CreditNoteId == cn.Id && s.Status == "Submitted").OrderByDescending(s => s.Id)
            .Select(s => s.QrCode).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var html = CreditHtml(details, inv, layout, company, customer, qrText);
        audit.Record(CreditNoteService.EntityType, cn.Id, "Print", cn.Status == InvoiceCodes.Void ? "Void" : null);
        return await OutputAsync(html, format, cn.Number, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// FR-163: an approved invoice prints the customer, sites, material and driver names it was issued with (renames after its approval are
    /// undone from the audit log); a draft prints the current names.
    /// </summary>
    private async Task<(InvoiceDetails Details, string CustomerName)> AsIssuedAsync(Invoice inv, InvoiceDetails details, Customer customer, CancellationToken ct)
    {
        if (inv.ApprovedAt is not DateTime at) return (details, customer.Name);
        var customerName = await names.OfAsync("TRN.Customer", customer.Id, "Name", customer.Name, at, ct).ConfigureAwait(false);
        var trips = await db.Set<InvoiceLine>().AsNoTracking().Where(l => l.InvoiceId == inv.Id && l.TripId != null)
            .Join(db.Set<Trip>().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]), l => l.TripId, t => (long?)t.Id, (l, t) => new { l.SortOrder, t.LoadingSiteId, t.UnloadingSiteId, t.MaterialId, t.DriverId })
            .ToListAsync(ct).ConfigureAwait(false);
        if (trips.Count == 0) return (details, customerName);
        var loading = await names.ManyAsync("TRN.LoadingSite", trips.Select(t => t.LoadingSiteId).Distinct().ToList(), "Name", at, ct).ConfigureAwait(false);
        var unloading = await names.ManyAsync("TRN.UnloadingSite", trips.Select(t => t.UnloadingSiteId).Distinct().ToList(), "Name", at, ct).ConfigureAwait(false);
        var materials = await names.ManyAsync("TRN.Material", trips.Select(t => t.MaterialId).Distinct().ToList(), "Name", at, ct).ConfigureAwait(false);
        var drivers = await names.ManyAsync("TRN.Driver", trips.Select(t => t.DriverId).Distinct().ToList(), "FullName", at, ct).ConfigureAwait(false);
        var bySort = trips.ToDictionary(t => t.SortOrder);
        var lines = details.Lines.Select(l => bySort.TryGetValue(l.SortOrder, out var t) ? l with
        {
            LoadingSite = loading.GetValueOrDefault(t.LoadingSiteId, l.LoadingSite ?? string.Empty), UnloadingSite = unloading.GetValueOrDefault(t.UnloadingSiteId, l.UnloadingSite ?? string.Empty),
            Material = materials.GetValueOrDefault(t.MaterialId, l.Material ?? string.Empty), Driver = drivers.GetValueOrDefault(t.DriverId, l.Driver ?? string.Empty),
        } : l).ToList();
        return (details with { Lines = lines }, customerName);
    }

    /// <summary>The designer's live preview (FR-055): the saved layout of a type with sample trips, as HTML.</summary>
    /// <summary>The layout with sample trips: the stored one, or the unsaved one of a request (the designer's live preview, DT-154).</summary>
    public async Task<GeneratedFile> LayoutPreviewAsync(string layoutType, CancellationToken ct, InvoiceLayoutRequest? draft = null)
    {
        var layout = draft is null ? await layouts.GetAsync(layoutType, ct).ConfigureAwait(false) : await layouts.DraftAsync(layoutType, draft, ct).ConfigureAwait(false);
        var company = await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);
        var customer = new Customer { Name = En ? "Sample customer" : "عميل للمعاينة", TaxNumber = "000000000" };
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        InvoiceLineResponse Line(int n, string site, decimal qty) => new($"s{n}", n, new DirectService.Shared.Contracts.Ref($"t{n}", $"TRP-{10480 + n}"), null, qty, 6.000m,
            Money.Round(qty * 6.000m), 0, today, today, "10-11111", En ? "Sample driver" : "سائق", site, En ? "Port" : "الميناء", En ? "Potash" : "بوتاس", qty + 0.15m, qty);
        var lines = new[] { Line(1, En ? "Site A" : "الموقع أ", 31.850m), Line(2, En ? "Site A" : "الموقع أ", 32.000m), Line(3, En ? "Site B" : "الموقع ب", 30.900m) };
        var subtotal = lines.Sum(l => l.Amount);
        var totals = InvoiceCalculator.Invoice(subtotal, 0, 0, company.TaxRate, company.TaxInclusive);
        var invoice = new InvoiceResponse("preview", "INV-00000", InvoiceCodes.Trip, new DirectService.Shared.Contracts.Ref("c", customer.Name), today, today.AddDays(30), totals.Subtotal, 0, 0, null,
            totals.BaseAmount, company.TaxRate, totals.Tax, totals.Total, 0, 0, totals.Total, InvoiceCodes.Approved, "Unpaid", false, layout.LayoutType, null, null, null, null, false, null,
            "NotRequired", lines.Length, DateTime.UtcNow, string.Empty);
        var words = En ? AmountInWords.English(totals.Total, company.CurrencyCode, company.CurrencyDecimals) : AmountInWords.Arabic(totals.Total, company.CurrencyCode, company.CurrencyDecimals);
        var html = InvoiceHtml(new InvoiceDetails(invoice, lines, [], words, []), layout, company, customer, layout.ShowQr ? "PREVIEW" : null);
        return new GeneratedFile("preview.html", "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html));
    }

    private Task<GeneratedFile> OutputAsync(string html, string? format, string name, CancellationToken ct) => printer.OutputAsync(html, format, name, ct);

    // ============================================================ HTML

    private static string E(string? s) => DocumentPrinter.E(s);

    private static string M(decimal v) => DocumentPrinter.M(v);

    private static string D(DateOnly? d) => DocumentPrinter.D(d);

    /// <summary>A left-to-right run inside RTL text (numbers, codes, dates).</summary>
    private static string N(string? s) => DocumentPrinter.N(s);

    private string L(string ar, string en) => En ? en : ar;

    private string ColumnLabel(string key, InvoiceLayoutResponse layout) => layout.Labels.TryGetValue(key, out var label) ? label : key switch
    {
        "receiptNo" => L("رقم السند", "Receipt no."),
        "truckPlate" => L("رقم اللوحة", "Plate"),
        "loadingDate" => L("تاريخ التحميل", "Loading date"),
        "unloadingDate" => L("تاريخ التنزيل", "Unloading date"),
        "loadedWeight" => L("الوزن المحمّل", "Loaded (t)"),
        "unloadedWeight" => L("الوزن المنزّل", "Unloaded (t)"),
        "billingQty" => L("الكمية المفوترة", "Billed (t)"),
        "unitPrice" => L("السعر", "Price"),
        "amount" => L("المبلغ", "Amount"),
        "driver" => L("السائق", "Driver"),
        "loadingSite" => L("موقع التحميل", "Loading site"),
        "unloadingSite" => L("موقع التنزيل", "Unloading site"),
        "material" => L("المادة", "Material"),
        _ => key.StartsWith("custom.", StringComparison.Ordinal) ? key[7..] : key,
    };

    private static string Cell(string key, InvoiceLineResponse l) => key switch
    {
        "receiptNo" => N(l.Trip?.Name ?? l.Description),
        "truckPlate" => N(l.TruckPlate),
        "loadingDate" => N(D(l.LoadingDate)),
        "unloadingDate" => N(D(l.UnloadingDate)),
        "loadedWeight" => N(l.LoadedWeight?.ToString("0.000", CultureInfo.InvariantCulture)),
        "unloadedWeight" => N(l.UnloadedWeight?.ToString("0.000", CultureInfo.InvariantCulture)),
        "billingQty" => N(l.Quantity.ToString("0.000", CultureInfo.InvariantCulture)),
        "unitPrice" => N(l.UnitPrice.ToString("0.000", CultureInfo.InvariantCulture)),
        "amount" => N(M(l.Amount)),
        "driver" => E(l.Driver),
        "loadingSite" => E(l.LoadingSite),
        "unloadingSite" => E(l.UnloadingSite),
        "material" => E(l.Material),
        _ when key.StartsWith("custom.", StringComparison.Ordinal) => E(l.Custom?.GetValueOrDefault(key[7..])),   // trip custom fields in the annex (FR-056)
        _ => string.Empty,
    };

    private string Shell(string title, string body, string? stamp) => printer.Shell(title, body, stamp);

    private string Header(Company company, InvoiceLayoutResponse layout) => printer.Header(company, layout.ShowLogo, layout.BandText);

    private string Parties(Customer c, string? qrText, InvoiceLayoutResponse layout) =>
        $"<div class=\"parties\"><div><b>{L("العميل", "Customer")}:</b> {E(c.Name)}<br>{L("الرقم الضريبي", "Tax no.")} {N(c.TaxNumber)}<br>{E(c.Address)} {E(c.City)}</div>"
        + (layout.ShowQr && !string.IsNullOrEmpty(qrText) ? $"<div class=\"qr\">{qr.Svg(qrText)}</div>" : string.Empty) + "</div>";

    private string Totals(decimal subtotal, decimal discount, decimal fees, string? feesText, decimal baseAmount, decimal taxRate, decimal tax, decimal total) =>
        $"<div class=\"totals\"><div><span>{L("المجموع", "Subtotal")}</span>{N(M(subtotal))}</div>"
        + (discount > 0 ? $"<div><span>{L("الخصم", "Discount")}</span>{N(M(discount))}</div>" : string.Empty)
        + (fees > 0 ? $"<div><span>{L("رسوم إضافية", "Extra fees")} {E(feesText)}</span>{N(M(fees))}</div>" : string.Empty)
        + $"<div><span>{L("الأساس", "Base")}</span>{N(M(baseAmount))}</div><div><span>{L("الضريبة", "Tax")} {N(taxRate.ToString("0.##", CultureInfo.InvariantCulture) + "%")}</span>{N(M(tax))}</div>"
        + $"<div class=\"grand\"><span>{L("الإجمالي", "Total")}</span>{N(M(total))}</div></div>";

    private static string Signatures(InvoiceLayoutResponse layout) =>
        layout.Signatures.Count == 0 ? string.Empty : $"<div class=\"sig\">{string.Concat(layout.Signatures.Select(s => $"<div>{E(s)}</div>"))}</div>";

    internal string InvoiceHtml(InvoiceDetails d, InvoiceLayoutResponse layout, Company company, Customer customer, string? qrText)
    {
        var i = d.Invoice;
        var sb = new StringBuilder();
        sb.Append(Header(company, layout));
        sb.Append(CultureInfo.InvariantCulture, $"<h1>{E(layout.Title)}</h1><div class=\"meta\">{L("رقم", "No.")} {N(i.Number ?? L("مسودة", "Draft"))} · {L("التاريخ", "Date")} {N(D(i.InvoiceDate))} · {L("الاستحقاق", "Due")} {N(D(i.DueDate))}</div>");
        sb.Append(Parties(customer, qrText, layout));
        var tripLines = d.Lines.Where(l => l.Trip is not null).ToList();
        if (layout.LayoutType == InvoiceCodes.Claim && tripLines.Count > 0)
        {
            // Page 1: totals grouped by loading site, route or material (FR-054).
            var groups = tripLines.GroupBy(l => layout.Grouping switch
            {
                "Route" => $"{l.LoadingSite} ← {l.UnloadingSite}",
                "Material" => l.Material ?? string.Empty,
                _ => l.LoadingSite ?? string.Empty,
            }).Select(g => (Name: g.Key, Count: g.Count(), Qty: g.Sum(x => x.Quantity), Amount: g.Sum(x => x.Amount))).OrderBy(g => g.Name, StringComparer.Ordinal).ToList();
            var groupLabel = layout.Grouping switch { "Route" => L("المسار", "Route"), "Material" => L("المادة", "Material"), _ => L("موقع التحميل", "Loading site") };
            sb.Append(CultureInfo.InvariantCulture, $"<table><thead><tr><th>{groupLabel}</th><th>{L("الرحلات", "Trips")}</th><th>{L("الكمية (طن)", "Quantity (t)")}</th><th>{L("المبلغ", "Amount")}</th></tr></thead><tbody>");
            foreach (var g in groups) sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{E(g.Name)}</td><td class=\"n\">{N(g.Count.ToString(CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(g.Qty.ToString("#,##0.000", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(M(g.Amount))}</td></tr>");
            sb.Append("</tbody></table>");
            sb.Append(Totals(i.Subtotal, i.Discount, i.ExtraFees, i.ExtraFeesDescription, i.BaseAmount, i.TaxRate, i.Tax, i.Total));
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"words\">{L("المبلغ كتابةً", "Amount in words")}: {E(d.AmountInWords)}</div>");
            if (i.Notes is not null) sb.Append(CultureInfo.InvariantCulture, $"<div class=\"foot\">{E(i.Notes)}</div>");
            sb.Append(Signatures(layout));
            // Page 2: one row per shipment with the layout's annex columns (FR-054, FR-056).
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"annex\"><h1 style=\"font-size:12pt\">{L("ملحق الشحنات", "Shipment annex")} · {N(i.Number ?? L("مسودة", "Draft"))}</h1><table><thead><tr>");
            foreach (var c in layout.AnnexColumns) sb.Append(CultureInfo.InvariantCulture, $"<th>{E(ColumnLabel(c, layout))}</th>");
            sb.Append("</tr></thead><tbody>");
            foreach (var l in tripLines)
            {
                sb.Append("<tr>");
                foreach (var c in layout.AnnexColumns) sb.Append(CultureInfo.InvariantCulture, $"<td class=\"{(c is "receiptNo" or "driver" or "loadingSite" or "unloadingSite" or "material" ? string.Empty : "n")}\">{Cell(c, l)}</td>");
                sb.Append("</tr>");
            }
            sb.Append(CultureInfo.InvariantCulture, $"</tbody></table><div class=\"totals\"><div class=\"grand\"><span>{L("المجموع", "Subtotal")}</span>{N(M(tripLines.Sum(l => l.Amount)))}</div></div></div>");
        }
        else
        {
            // Tax invoice, or a manual invoice: one row per line (FR-057).
            sb.Append(CultureInfo.InvariantCulture, $"<table><thead><tr><th>#</th><th>{L("البيان", "Description")}</th><th>{L("الكمية", "Quantity")}</th><th>{L("سعر الوحدة", "Unit price")}</th><th>{L("المبلغ", "Amount")}</th></tr></thead><tbody>");
            var n = 0;
            foreach (var l in d.Lines)
            {
                var text = l.Trip is null ? E(l.Description) : $"{N(l.Trip.Name)} · {E(l.LoadingSite)} ← {E(l.UnloadingSite)} · {E(l.Material)} · {N(l.TruckPlate)}";
                sb.Append(CultureInfo.InvariantCulture, $"<tr><td class=\"n\">{N((++n).ToString(CultureInfo.InvariantCulture))}</td><td>{text}</td><td class=\"n\">{N(l.Quantity.ToString("#,##0.000", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(l.UnitPrice.ToString("0.000", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(M(l.Amount))}</td></tr>");
            }
            sb.Append("</tbody></table>");
            sb.Append(Totals(i.Subtotal, i.Discount, i.ExtraFees, i.ExtraFeesDescription, i.BaseAmount, i.TaxRate, i.Tax, i.Total));
            sb.Append(CultureInfo.InvariantCulture, $"<div class=\"words\">{L("المبلغ كتابةً", "Amount in words")}: {E(d.AmountInWords)}</div>");
            if (i.Notes is not null) sb.Append(CultureInfo.InvariantCulture, $"<div class=\"foot\">{E(i.Notes)}</div>");
            sb.Append(Signatures(layout));
        }
        if (layout.FooterText is not null) sb.Append(CultureInfo.InvariantCulture, $"<div class=\"foot\">{E(layout.FooterText)}</div>");
        var stamp = i.Status == InvoiceCodes.Draft ? "DRAFT" : i.Status == InvoiceCodes.Void ? "VOID" : null;
        return Shell(layout.Title + " " + (i.Number ?? string.Empty), sb.ToString(), stamp);
    }

    internal string CreditHtml(CreditNoteDetails d, Invoice inv, InvoiceLayoutResponse layout, Company company, Customer customer, string? qrText)
    {
        var c = d.CreditNote;
        var sb = new StringBuilder();
        sb.Append(Header(company, layout));
        sb.Append(CultureInfo.InvariantCulture, $"<h1>{L("إشعار دائن", "Credit note")}</h1><div class=\"meta\">{L("رقم", "No.")} {N(c.Number)} · {L("التاريخ", "Date")} {N(D(c.CreditDate))}<br>"
            + $"{L("الفاتورة الأصلية", "Original invoice")} {N(inv.Number)} · {N(D(inv.InvoiceDate))}</div>");
        sb.Append(Parties(customer, qrText, layout));
        sb.Append(CultureInfo.InvariantCulture, $"<div><b>{L("السبب", "Reason")}:</b> {E(c.Reason)}</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<table><thead><tr><th>{L("البند المرتجع", "Returned item")}</th><th>{L("الكمية", "Quantity")}</th><th>{L("المبلغ", "Amount")}</th></tr></thead><tbody>");
        foreach (var l in d.Lines)
            sb.Append(CultureInfo.InvariantCulture, $"<tr><td>{(l.Trip is null ? E(l.Description) : N(l.Trip.Name))}</td><td class=\"n\">{N(l.Quantity.ToString("#,##0.000", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(M(l.Amount))}</td></tr>");
        sb.Append("</tbody></table>");
        sb.Append(Totals(c.Subtotal, c.DiscountShare, c.FeesShare, null, c.BaseAmount, inv.TaxRate, c.Tax, c.Total));
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"words\">{L("المبلغ كتابةً", "Amount in words")}: {E(d.AmountInWords)}</div>");
        sb.Append(Signatures(layout));
        return Shell(L("إشعار دائن", "Credit note") + " " + c.Number, sb.ToString(), c.Status == InvoiceCodes.Void ? "VOID" : null);
    }
}
