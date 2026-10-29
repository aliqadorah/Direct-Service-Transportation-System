using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Payables and receivables ageing by party as of a date (FR-128, RPT-17, RPT-18): the amount still due on each document as of the date
/// (total − payments/receipts − returns/credit notes dated up to it) in buckets by days past the due date — not due, 1–30, 31–60, 61–90,
/// over 90. Payables: supplier bills. Receivables: approved transport invoices per transport customer, with on-account receipts shown
/// separately as unapplied (7.7.3), and Accounting receivable documents per accounting customer.
/// </summary>
public sealed class AgeingService(IAppDbContext db, MasterDataSupport support, IReportExporter exporter)
{
    private sealed record Due(string Type, long PartyId, DateOnly DueDate, decimal Amount);

    public async Task<AgeingReport> AgeingAsync(string kind, DateOnly? asOf, CancellationToken ct)
    {
        var k = (kind?.Trim().ToLowerInvariant()) switch
        {
            "payables" => "Payables",
            "receivables" => "Receivables",
            _ => throw Fail.Field("kind", ErrorCodes.ValidationFormat, new FieldRef("kind")),
        };
        var date = asOf ?? await support.TodayAsync(ct).ConfigureAwait(false);
        var dues = new List<Due>();
        var unapplied = new Dictionary<long, decimal>();

        // Accounting documents of the kind.
        var docKind = k == "Payables" ? ApArCodes.SupplierBill : ApArCodes.Receivable;
        dues.AddRange((await db.Set<ApArDocument>().AsNoTracking()
                .Where(d => d.DocumentKind == docKind && d.Status == ApArCodes.Posted && d.DocumentDate <= date)
                .Select(d => new
                {
                    d.PartyId, d.DueDate, d.Total,
                    Paid = db.Set<ApArPayment>().Where(p => p.DocumentId == d.Id && p.Status == CashCodes.Active && p.PaymentDate <= date).Sum(p => (decimal?)p.Amount) ?? 0,
                    Returned = db.Set<ApArReturn>().Where(x => x.DocumentId == d.Id && x.Status == ApArCodes.Posted && x.ReturnDate <= date).Sum(x => (decimal?)x.Total) ?? 0,
                }).ToListAsync(ct).ConfigureAwait(false))
            .Select(d => new Due(AccountingCodes.PartyApAr, d.PartyId, d.DueDate, d.Total - d.Paid - d.Returned)));

        if (k == "Receivables")
        {
            dues.AddRange((await db.Set<Invoice>().AsNoTracking()
                    .Where(i => (i.Status == InvoiceCodes.Approved || i.Status == InvoiceCodes.Closed) && i.InvoiceDate <= date)
                    .Select(i => new
                    {
                        i.CustomerId, i.DueDate, i.Total,
                        Paid = db.Set<ReceiptAllocation>().Where(a => a.InvoiceId == i.Id && a.Receipt.Status == CashCodes.Active && a.Receipt.ReceiptDate <= date).Sum(a => (decimal?)a.Amount) ?? 0,
                        Credited = db.Set<CreditNote>().Where(c => c.InvoiceId == i.Id && c.Status == InvoiceCodes.Posted && c.CreditDate <= date).Sum(c => (decimal?)c.Total) ?? 0,
                    }).ToListAsync(ct).ConfigureAwait(false))
                .Select(i => new Due(AccountingCodes.PartyCustomer, i.CustomerId, i.DueDate, i.Total - i.Paid - i.Credited)));
            // The part of each receipt not allocated to invoices (revision 6: a receipt may be applied to several invoices, the rest on account).
            unapplied = (await db.Set<Receipt>().AsNoTracking().Where(r => r.Status == CashCodes.Active && r.ReceiptDate <= date)
                    .Select(r => new { r.CustomerId, U = r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0) }).Where(x => x.U != 0)
                    .GroupBy(x => x.CustomerId).Select(g => new { g.Key, A = g.Sum(x => x.U) }).ToListAsync(ct).ConfigureAwait(false))
                .ToDictionary(x => x.Key, x => x.A);
        }

        var open = dues.Where(d => d.Amount != 0).ToList();
        var keys = open.Select(d => (d.Type, d.PartyId)).Concat(unapplied.Keys.Select(id => (AccountingCodes.PartyCustomer, id))).Distinct().ToList();
        var names = await NamesAsync(keys, ct).ConfigureAwait(false);
        var rows = keys.Select(key =>
            {
                var mine = open.Where(d => d.Type == key.Item1 && d.PartyId == key.Item2).ToList();
                var buckets = Buckets(mine.Select(d => (date.DayNumber - d.DueDate.DayNumber, d.Amount)), key.Item1 == AccountingCodes.PartyCustomer ? unapplied.GetValueOrDefault(key.Item2) : 0);
                return new AgeingRow(names[key], buckets, mine.Count);
            })
            .OrderBy(r => r.Party.Name, StringComparer.CurrentCulture).ToList();
        var totals = new AgeingBuckets(rows.Sum(r => r.Amounts.NotDue), rows.Sum(r => r.Amounts.Days1To30), rows.Sum(r => r.Amounts.Days31To60), rows.Sum(r => r.Amounts.Days61To90),
            rows.Sum(r => r.Amounts.Over90), rows.Sum(r => r.Amounts.Total), rows.Sum(r => r.Amounts.Unapplied));
        return new AgeingReport(k, date, rows, totals);
    }

    /// <summary>Amounts per bucket by days past due (≤ 0 = not due).</summary>
    public static AgeingBuckets Buckets(IEnumerable<(int DaysPastDue, decimal Amount)> items, decimal unapplied)
    {
        ArgumentNullException.ThrowIfNull(items);
        decimal n = 0, a = 0, b = 0, c = 0, d = 0;
        foreach (var (days, amount) in items)
        {
            if (days <= 0) n += amount;
            else if (days <= 30) a += amount;
            else if (days <= 60) b += amount;
            else if (days <= 90) c += amount;
            else d += amount;
        }
        return new AgeingBuckets(n, a, b, c, d, n + a + b + c + d, unapplied);
    }

    private async Task<Dictionary<(string, long), PartyResponse>> NamesAsync(List<(string Type, long Id)> keys, CancellationToken ct)
    {
        var customerIds = keys.Where(k => k.Type == AccountingCodes.PartyCustomer).Select(k => k.Id).ToList();
        var partyIds = keys.Where(k => k.Type == AccountingCodes.PartyApAr).Select(k => k.Id).ToList();
        var result = new Dictionary<(string, long), PartyResponse>();
        foreach (var c in await db.Set<Customer>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(c => customerIds.Contains(c.Id))
                     .Select(c => new { c.Id, c.PublicId, c.Name }).ToListAsync(ct).ConfigureAwait(false))
            result[(AccountingCodes.PartyCustomer, c.Id)] = new PartyResponse(AccountingCodes.ModuleTrn, AccountingCodes.PartyCustomer, support.Token<Customer>(c.PublicId), c.Name);
        foreach (var p in await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([QueryFilterNames.SoftDelete]).Where(p => partyIds.Contains(p.Id))
                     .Select(p => new { p.Id, p.PublicId, p.Name }).ToListAsync(ct).ConfigureAwait(false))
            result[(AccountingCodes.PartyApAr, p.Id)] = new PartyResponse(AccountingCodes.ModuleAcc, AccountingCodes.PartyApAr, support.Token<ApArParty>(p.PublicId), p.Name);
        return result;
    }

    public async Task<GeneratedFile> ExportAsync(string kind, DateOnly? asOf, string? format, CancellationToken ct)
    {
        var r = await AgeingAsync(kind, asOf, ct).ConfigureAwait(false);
        var receivables = r.Kind == "Receivables";
        string L(string ar, string en) => exporter.L(ar, en);
        List<ReportColumn> columns =
        [
            new(receivables ? L("العميل", "Customer") : L("المورد", "Supplier")), new(L("غير مستحق", "Not due"), ReportValue.Money), new("1–30", ReportValue.Money),
            new("31–60", ReportValue.Money), new("61–90", ReportValue.Money), new(L("أكثر من 90", "Over 90"), ReportValue.Money), new(L("الإجمالي", "Total"), ReportValue.Money),
        ];
        if (receivables) columns.Add(new(L("دفعات غير مخصصة", "Unapplied payments"), ReportValue.Money));
        IReadOnlyList<object?> Row(string name, AgeingBuckets b) => receivables
            ? [name, b.NotDue, b.Days1To30, b.Days31To60, b.Days61To90, b.Over90, b.Total, b.Unapplied]
            : [name, b.NotDue, b.Days1To30, b.Days31To60, b.Days61To90, b.Over90, b.Total];
        var table = new ReportTable(receivables ? L("أعمار الذمم المدينة", "Receivables ageing") : L("أعمار الذمم الدائنة", "Payables ageing"),
            [(L("كما في", "As of"), r.AsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))], columns,
            r.Rows.Select(x => Row(x.Party.Name ?? string.Empty, x.Amounts)).ToList(), Row(L("الإجمالي", "Total"), r.Totals));
        return await exporter.ExportAsync(table, format, receivables ? "receivables-ageing" : "payables-ageing", ct).ConfigureAwait(false);
    }
}
