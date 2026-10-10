using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Application.Common.DocumentPrinter;

namespace DirectService.Transportation.Application.Cash;

/// <summary>
/// Vouchers and statements (FR-070, FR-075, FR-082, FR-083, FR-087, FR-088, FR-090): receipt voucher; payment voucher for advances,
/// partner payments and a settlement's net; settlement statement followed by its payment voucher; driver and partner statements. Each
/// shows the company header, number, date, party, amount in figures and words, method, cash box / bank, reference and signatures; void
/// documents carry a VOID stamp; every print after the first is marked "Copy"; every print is logged.
/// </summary>
public sealed class VoucherPrintService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, DocumentPrinter printer, AuditTrail audit,
    ReceiptService receipts, AdvanceService advances, SettlementService settlements, PartnerPaymentService payments, AccountStatementService statements)
{
    private string L(string ar, string en) => printer.L(ar, en);

    private async Task<Company> CompanyAsync(CancellationToken ct) => await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);

    /// <summary>"Copy" when the document was printed before (FR-090); the print is logged with the caller's save.</summary>
    private async Task<string?> MarkAsync(string entityType, long id, string? note, CancellationToken ct)
    {
        var printed = await db.Set<AuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == entityType && a.EntityId == id && a.Action == "Print", ct).ConfigureAwait(false);
        audit.Record(entityType, id, "Print", printed ? "Copy" + (note is null ? string.Empty : " · " + note) : note);
        return printed ? L("نسخة", "Copy") : null;
    }

    private string Method(string method) => method switch
    {
        CashCodes.Cash => L("نقداً", "Cash"),
        CashCodes.Bank => L("تحويل بنكي", "Bank transfer"),
        _ => "CliQ",
    };

    private static string Field(string label, string value) => $"<b>{E(label)}</b><div>{value}</div>";

    private string Voucher(Company company, string title, string number, DateOnly date, string partyLabel, string party, decimal amount, string words, string method,
        MoneyAccountRef account, string? reference, IEnumerable<string> extra, IEnumerable<string> signatures) =>
        printer.Header(company)
        + $"<h1>{E(title)}</h1><div class=\"meta\">{L("رقم", "No.")} {N(number)} · {L("التاريخ", "Date")} {N(D(date))}</div>"
        + "<div class=\"fields\">"
        + Field(partyLabel, E(party))
        + Field(L("المبلغ", "Amount"), N(M(amount)))
        + Field(L("المبلغ كتابة", "Amount in words"), E(words))
        + Field(L("طريقة الدفع", "Method"), E(method))
        + Field(account.Kind == CashCodes.CashBox ? L("الصندوق", "Cash box") : L("الحساب البنكي", "Bank account"), E(account.Name))
        + (reference is null ? string.Empty : Field(L("المرجع", "Reference"), N(reference)))
        + string.Concat(extra)
        + "</div>" + Signatures(signatures);

    // ------------------------------------------------------------ receipts (FR-070)

    public async Task<GeneratedFile> ReceiptAsync(string token, string? format, CancellationToken ct)
    {
        var receipt = await support.FindAsync<Receipt>(token, ct).ConfigureAwait(false);
        var d = (await receipts.DetailsAsync(receipt.Id, ct).ConfigureAwait(false));
        var r = d.Receipt;
        var mark = await MarkAsync(ReceiptService.EntityType, receipt.Id, null, ct).ConfigureAwait(false);
        var body = Voucher(await CompanyAsync(ct).ConfigureAwait(false), L("سند قبض", "Receipt voucher"), r.Number, r.ReceiptDate, L("استلمنا من", "Received from"), r.Customer.Name,
            r.Amount, d.AmountInWords, Method(r.PaymentMethod), r.Account, r.Reference,
            [
                Field(L("عن", "For"), r.Invoice is null ? E(L("دفعة على الحساب", "On account")) : $"{L("الفاتورة", "Invoice")} {N(r.Invoice.Number)}"),
                Field(L("عدد المرفقات", "Attachments"), N(r.AttachmentsCount.ToString(CultureInfo.InvariantCulture))),
                r.Notes is null ? string.Empty : Field(L("ملاحظات", "Notes"), E(r.Notes)),
            ],
            [L("المحاسب", "Accountant"), L("أمين الصندوق", "Cashier"), L("المستلم منه", "Received from")]);
        return await printer.OutputAsync(printer.Shell(r.Number, body, r.Status == CashCodes.Void ? "VOID" : null, mark), format, r.Number, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ payment vouchers (FR-075, FR-087)

    public async Task<GeneratedFile> AdvanceAsync(string token, string? format, CancellationToken ct)
    {
        var advance = await support.FindAsync<Advance>(token, ct).ConfigureAwait(false);
        var d = await advances.DetailsAsync(advance.Id, ct).ConfigureAwait(false);
        var a = d.Advance;
        var mark = await MarkAsync(AdvanceService.EntityType, advance.Id, null, ct).ConfigureAwait(false);
        var body = Voucher(await CompanyAsync(ct).ConfigureAwait(false), L("سند صرف — سلفة سائق", "Payment voucher — driver advance"), a.Number, a.AdvanceDate,
            L("صرفنا إلى", "Paid to"), a.Driver.Name, a.Amount, d.AmountInWords, Method(a.PaymentMethod), a.Account, a.Reference,
            [a.Notes is null ? string.Empty : Field(L("ملاحظات", "Notes"), E(a.Notes))], [L("صرف بواسطة", "Paid by"), L("استلم بواسطة", "Received by")]);
        return await printer.OutputAsync(printer.Shell(a.Number, body, a.Status == CashCodes.Void ? "VOID" : null, mark), format, a.Number, ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> PartnerPaymentAsync(string token, string? format, CancellationToken ct)
    {
        var payment = await support.FindAsync<PartnerPayment>(token, ct).ConfigureAwait(false);
        var d = await payments.DetailsAsync(payment.Id, ct).ConfigureAwait(false);
        var p = d.Payment;
        var mark = await MarkAsync(PartnerPaymentService.EntityType, payment.Id, null, ct).ConfigureAwait(false);
        var body = Voucher(await CompanyAsync(ct).ConfigureAwait(false), L("سند صرف — دفعة شريك", "Payment voucher — partner payment"), p.Number, p.PaymentDate,
            L("صرفنا إلى", "Paid to"), p.Partner.Name, p.Amount, d.AmountInWords, Method(p.PaymentMethod), p.Account, p.Reference,
            [p.Notes is null ? string.Empty : Field(L("ملاحظات", "Notes"), E(p.Notes))], [L("صرف بواسطة", "Paid by"), L("استلم بواسطة", "Received by")]);
        return await printer.OutputAsync(printer.Shell(p.Number, body, p.Status == CashCodes.Void ? "VOID" : null, mark), format, p.Number, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ settlement statement + voucher (FR-082)

    public async Task<GeneratedFile> SettlementAsync(string token, string? format, CancellationToken ct)
    {
        var stl = await support.FindAsync<Settlement>(token, ct).ConfigureAwait(false);
        var d = await settlements.DetailsAsync(stl.Id, ct).ConfigureAwait(false);
        var s = d.Settlement;
        var plate = await db.Set<Driver>().IgnoreQueryFilters().Where(x => x.Id == stl.DriverId).Select(x => x.PlateNumber).SingleAsync(ct).ConfigureAwait(false);
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        var mark = await MarkAsync(SettlementService.EntityType, stl.Id, null, ct).ConfigureAwait(false);
        var trips = string.Concat(d.Trips.Select(t =>
            $"<tr><td>{N(t.ReceiptNo)}</td><td>{N(D(t.TripDate))}</td><td>{E(t.Route)}</td><td>{E(t.Material)}</td><td class=\"n\">{N(t.WageQty.ToString("0.000", CultureInfo.InvariantCulture))}</td>"
            + $"<td class=\"n\">{N(t.DriverRate.ToString("0.000", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(M(t.DriverWage))}</td></tr>"));
        var advanceRows = string.Concat(d.Advances.Select(a => $"<tr><td>{N(a.Number)}</td><td>{N(D(a.AdvanceDate))}</td><td class=\"n\">{N(M(a.Amount))}</td></tr>"));
        var deductionRows = string.Concat(d.Deductions.Select(x => $"<tr><td>{E(DeductionLabel(x.Type))}</td><td>{E(x.Note)}</td><td class=\"n\">{N(M(x.Amount))}</td></tr>"));
        var statement = printer.Header(company)
            + $"<h1>{L("كشف تسوية سائق", "Driver settlement statement")}</h1><div class=\"meta\">{L("رقم", "No.")} {N(s.Number)} · {L("التاريخ", "Date")} {N(D(s.SettlementDate))}</div>"
            + $"<div class=\"fields\">{Field(L("السائق", "Driver"), E(s.Driver.Name))}{Field(L("رقم اللوحة", "Plate"), N(plate))}</div>"
            + $"<h2>{L("الرحلات", "Trips")}</h2><table><tr><th>{L("السند", "Receipt")}</th><th>{L("التاريخ", "Date")}</th><th>{L("المسار", "Route")}</th><th>{L("المادة", "Material")}</th>"
            + $"<th>{L("الكمية", "Qty")}</th><th>{L("الأجرة", "Rate")}</th><th>{L("الأجر", "Wage")}</th></tr>{trips}</table>"
            + (d.Advances.Count == 0 ? string.Empty : $"<h2>{L("السلف", "Advances")}</h2><table><tr><th>{L("الرقم", "No.")}</th><th>{L("التاريخ", "Date")}</th><th>{L("المبلغ", "Amount")}</th></tr>{advanceRows}</table>")
            + (d.Deductions.Count == 0 ? string.Empty : $"<h2>{L("الخصومات", "Deductions")}</h2><table><tr><th>{L("النوع", "Type")}</th><th>{L("ملاحظة", "Note")}</th><th>{L("المبلغ", "Amount")}</th></tr>{deductionRows}</table>")
            + $"<div class=\"totals\"><div><span>{L("إجمالي الأجور", "Gross wages")}</span>{N(M(s.GrossWages))}</div><div><span>{L("السلف", "Advances")}</span>{N(M(s.AdvancesTotal))}</div>"
            + $"<div><span>{L("الخصومات", "Deductions")}</span>{N(M(s.DeductionsTotal))}</div><div class=\"grand\"><span>{L("الصافي", "Net payable")}</span>{N(M(s.NetPayable))}</div></div>"
            + $"<div class=\"words\">{E(d.AmountInWords)}</div>" + Signatures([L("المحاسب", "Accountant"), L("المدير المالي", "Finance manager"), L("السائق", "Driver")]);
        var voucher = s.NetPayable == 0 ? string.Empty : "<div class=\"annex\">" + Voucher(company, L("سند صرف — صافي تسوية", "Payment voucher — settlement net"), s.Number, s.SettlementDate, L("صرفنا إلى", "Paid to"),
            s.Driver.Name, s.NetPayable, d.AmountInWords, Method(s.PaymentMethod), s.Account, s.Reference, [], [L("صرف بواسطة", "Paid by"), L("استلم بواسطة", "Received by")]) + "</div>";
        return await printer.OutputAsync(printer.Shell(s.Number, statement + voucher, s.Status == CashCodes.Void ? "VOID" : null, mark), format, s.Number, ct).ConfigureAwait(false);
    }

    private string DeductionLabel(string type) => type switch
    {
        CashCodes.Shortage => L("نقص وزن", "Shortage"),
        CashCodes.Fine => L("غرامة", "Fine"),
        _ => L("أخرى", "Other"),
    };

    // ------------------------------------------------------------ statements (FR-083, FR-088)

    public async Task<GeneratedFile> DriverStatementAsync(string token, DateOnly? from, DateOnly? to, string? format, CancellationToken ct)
    {
        var s = await statements.DriverStatementAsync(token, from, to, ct).ConfigureAwait(false);
        if (format?.ToLowerInvariant() is "xlsx" or "csv") return statements.Export(s, "driver-statement", format.ToLowerInvariant());
        var driver = await support.FindAsync<Driver>(token, ct).ConfigureAwait(false);
        audit.Record("TRN.Driver", driver.Id, "Print", $"Statement {D(s.From)}…{D(s.To)}");
        return await StatementAsync(s, L("كشف حساب سائق", "Driver statement"), withShipments: false, format, ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> PartnerStatementAsync(string token, DateOnly? from, DateOnly? to, string? format, CancellationToken ct)
    {
        var s = await statements.PartnerStatementAsync(token, from, to, ct).ConfigureAwait(false);
        if (format?.ToLowerInvariant() is "xlsx" or "csv") return statements.Export(s, "partner-statement", format.ToLowerInvariant());
        var partner = await support.FindAsync<Partner>(token, ct).ConfigureAwait(false);
        audit.Record("TRN.Partner", partner.Id, "Print", $"Statement {D(s.From)}…{D(s.To)}");
        return await StatementAsync(s, L("كشف حساب شريك", "Partner statement"), withShipments: true, format, ct).ConfigureAwait(false);
    }

    private async Task<GeneratedFile> StatementAsync(StatementResponse s, string title, bool withShipments, string? format, CancellationToken ct)
    {
        var company = await CompanyAsync(ct).ConfigureAwait(false);
        string Num(decimal? v, string f) => v is decimal x ? N(x.ToString(f, CultureInfo.InvariantCulture)) : string.Empty;
        var head = $"<tr><th>{L("التاريخ", "Date")}</th><th>{L("المستند", "Document")}</th><th>{L("البيان", "Description")}</th>"
            + (withShipments ? $"<th>{L("الوزن", "Weight")}</th><th>{L("السعر", "Rate")}</th>" : string.Empty)
            + $"<th>{L("له", "Increase")}</th><th>{L("عليه", "Decrease")}</th><th>{L("الرصيد", "Balance")}</th></tr>";
        var span = withShipments ? 5 : 3;
        var rows = $"<tr><td colspan=\"{span}\">{L("الرصيد الافتتاحي", "Opening balance")}</td><td></td><td></td><td class=\"n\">{N(M(s.Opening))}</td></tr>"
            + string.Concat(s.Lines.Select(l => $"<tr><td>{N(D(l.Date))}</td><td>{N(l.Document.Number)}</td><td>{E(l.Description)}</td>"
                + (withShipments ? $"<td class=\"n\">{Num(l.Weight, "0.000")}</td><td class=\"n\">{Num(l.Rate, "0.000")}</td>" : string.Empty)
                + $"<td class=\"n\">{(l.Increase == 0 ? string.Empty : N(M(l.Increase)))}</td><td class=\"n\">{(l.Decrease == 0 ? string.Empty : N(M(l.Decrease)))}</td><td class=\"n\">{N(M(l.Balance))}</td></tr>"))
            + $"<tr><th colspan=\"{span}\">{L("الرصيد الختامي", "Closing balance")}</th><th class=\"n\">{N(M(s.TotalIncrease))}</th><th class=\"n\">{N(M(s.TotalDecrease))}</th><th class=\"n\">{N(M(s.Closing))}</th></tr>";
        var body = printer.Header(company) + $"<h1>{E(title)}</h1><div class=\"meta\">{E(s.Party.Name)} · {N(D(s.From))} – {N(D(s.To))}</div><table>{head}{rows}</table>";
        return await printer.OutputAsync(printer.Shell(title, body, null), format, "statement", ct).ConfigureAwait(false);
    }
}
