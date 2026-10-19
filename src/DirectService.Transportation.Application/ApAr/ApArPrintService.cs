using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using static DirectService.Transportation.Application.Common.DocumentPrinter;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>
/// Printed AP/AR documents (FR-127): supplier bill / receivable document with lines, tax, total and amount in words (receivables carry the
/// e-invoicing QR once accepted), payment and collection vouchers, return notes. VOID stamp on void documents, "Copy" on every print after
/// the first (as FR-090), every print logged.
/// </summary>
public sealed class ApArPrintService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, DocumentPrinter printer, AuditTrail audit, IQrCodeRenderer qr,
    ApArDocumentService documents, ApArPaymentService payments, ApArReturnService returns, EInvoiceService einvoice)
{
    private string L(string ar, string en) => printer.L(ar, en);

    private async Task<Company> CompanyAsync(CancellationToken ct) => await db.Set<Company>().AsNoTracking().SingleAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false);

    private async Task<string?> MarkAsync(string entityType, long id, CancellationToken ct)
    {
        var printed = await db.Set<AuditLog>().AsNoTracking().AnyAsync(a => a.EntityType == entityType && a.EntityId == id && a.Action == "Print", ct).ConfigureAwait(false);
        audit.Record(entityType, id, "Print", printed ? "Copy" : null);
        return printed ? L("نسخة", "Copy") : null;
    }

    private static string Field(string label, string value) => $"<b>{E(label)}</b><div>{value}</div>";

    private string Method(string method) => method switch
    {
        CashCodes.Cash => L("نقداً", "Cash"),
        CashCodes.Bank => L("تحويل بنكي", "Bank transfer"),
        _ => "CliQ",
    };

    private async Task<(string? TaxNumber, string? Phone, string? Address)> PartyAsync(string token, CancellationToken ct)
    {
        var g = support.Decode<ApArParty>(token);
        var p = await db.Set<ApArParty>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(x => x.PublicId == g && x.CompanyId == ctx.CompanyId)
            .Select(x => new { x.TaxNumber, x.Phone, x.Address }).SingleAsync(ct).ConfigureAwait(false);
        return (p.TaxNumber, p.Phone, p.Address);
    }

    public async Task<GeneratedFile> DocumentAsync(string kind, string token, string? format, CancellationToken ct)
    {
        var doc = await documents.FindAsync(ApArRules.Kind(kind), token, ct).ConfigureAwait(false);
        var d = await documents.DetailsAsync(doc.Id, ct).ConfigureAwait(false);
        var x = d.Document;
        var bill = x.Kind == ApArCodes.SupplierBill;
        var deductions = bill && x.Deduction > 0;   // revision 6: a deduction column and total only when the bill has deductions
        var mark = await MarkAsync(ApArDocumentService.EntityType, doc.Id, ct).ConfigureAwait(false);
        var (tax, phone, address) = await PartyAsync(x.Party.Id, ct).ConfigureAwait(false);
        var qrText = bill ? null : await einvoice.ApArQrAsync(doc.Id, null, ct).ConfigureAwait(false);
        var title = bill ? L("فاتورة مورد", "Supplier bill") : L("فاتورة", "Invoice");
        var body = printer.Header(await CompanyAsync(ct).ConfigureAwait(false))
            + $"<h1>{E(title)}</h1><div class=\"meta\">{L("رقم", "No.")} {N(x.Number)} · {L("التاريخ", "Date")} {N(D(x.DocumentDate))} · {L("الاستحقاق", "Due")} {N(D(x.DueDate))}</div>"
            + "<div class=\"parties\"><div class=\"fields\">"
            + Field(bill ? L("المورد", "Supplier") : L("العميل", "Customer"), E(x.Party.Name))
            + (tax is null ? string.Empty : Field(L("الرقم الضريبي", "Tax no."), N(tax)))
            + (phone is null ? string.Empty : Field(L("الهاتف", "Phone"), N(phone)))
            + (address is null ? string.Empty : Field(L("العنوان", "Address"), E(address)))
            + (x.Reference is null ? string.Empty : Field(L("المرجع", "Reference"), N(x.Reference)))
            + "</div>" + (string.IsNullOrEmpty(qrText) ? string.Empty : $"<div class=\"qr\">{qr.Svg(qrText)}</div>") + "</div>"
            + $"<table><thead><tr><th>#</th><th>{E(L("الوصف", "Description"))}</th><th>{E(L("الحساب", "Account"))}</th><th>{E(L("الضريبة %", "Tax %"))}</th>"
            + $"<th>{E(L("المبلغ", "Amount"))}</th>" + (deductions ? $"<th>{E(L("الخصم", "Deduction"))}</th>" : string.Empty) + "</tr></thead><tbody>"
            + string.Concat(d.Lines.Select((l, i) => $"<tr><td class=\"n\">{N((i + 1).ToString(CultureInfo.InvariantCulture))}</td><td>{E(l.Description)}</td>"
                + $"<td>{N(l.Account.Code)} {E(printer.En ? l.Account.NameEn ?? l.Account.NameAr : l.Account.NameAr)}</td>"
                + $"<td class=\"n\">{N(l.TaxRate.ToString("0.##", CultureInfo.InvariantCulture))}</td><td class=\"n\">{N(M(l.Amount))}</td>"
                + (deductions ? $"<td class=\"n\">{N(M(l.Deduction))}</td>" : string.Empty) + "</tr>"))
            + "</tbody></table>"
            + "<div class=\"totals\">"
            + $"<div><span>{E(L("المجموع", "Subtotal"))}</span>{N(M(x.Subtotal))}</div>"
            + $"<div><span>{E(bill ? L("ضريبة المدخلات", "Input tax") : L("ضريبة المبيعات", "Sales tax"))}</span>{N(M(x.Tax))}</div>"
            + (deductions ? $"<div><span>{E(L("الخصم", "Deduction"))}</span>{N(M(-x.Deduction))}</div>" : string.Empty)
            + $"<div class=\"grand\"><span>{E(L("الإجمالي", "Total"))}</span>{N(M(x.Total))}</div>"
            + (x.PaidAmount + x.ReturnedAmount > 0
                ? $"<div><span>{E(L("المدفوع والمرتجع", "Paid and returned"))}</span>{N(M(x.PaidAmount + x.ReturnedAmount))}</div><div><span>{E(L("المتبقي", "Remaining"))}</span>{N(M(x.RemainingAmount))}</div>"
                : string.Empty)
            + "</div>"
            + $"<div class=\"words\"><b>{E(L("المبلغ كتابة", "Amount in words"))}:</b> {E(d.AmountInWords)}</div>"
            + Signatures([L("أعدّه", "Prepared by"), L("اعتمده", "Approved by"), bill ? L("المورد", "Supplier") : L("المستلم", "Received by")]);
        return await printer.OutputAsync(printer.Shell(x.Number, body, x.Status == ApArCodes.Void ? "VOID" : null, mark), format, x.Number, ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> PaymentAsync(string token, string? format, CancellationToken ct)
    {
        var payment = await support.FindAsync<ApArPayment>(token, ct).ConfigureAwait(false);
        var d = await payments.DetailsAsync(payment.Id, ct).ConfigureAwait(false);
        var p = d.Payment;
        var bill = p.Kind == ApArCodes.SupplierBill;
        var mark = await MarkAsync(ApArPaymentService.EntityType, payment.Id, ct).ConfigureAwait(false);
        var body = printer.Header(await CompanyAsync(ct).ConfigureAwait(false))
            + $"<h1>{E(bill ? L("سند صرف", "Payment voucher") : L("سند قبض", "Receipt voucher"))}</h1>"
            + $"<div class=\"meta\">{L("رقم", "No.")} {N(p.Number)} · {L("التاريخ", "Date")} {N(D(p.PaymentDate))}</div><div class=\"fields\">"
            + Field(bill ? L("صرفنا إلى", "Paid to") : L("استلمنا من", "Received from"), E(p.Party.Name))
            + Field(L("المبلغ", "Amount"), N(M(p.Amount)))
            + Field(L("المبلغ كتابة", "Amount in words"), E(d.AmountInWords))
            + Field(L("عن", "For"), $"{E(bill ? L("فاتورة المورد", "Supplier bill") : L("المستند", "Document"))} {N(p.Document.Number)}")
            + Field(L("طريقة الدفع", "Method"), E(Method(p.PaymentMethod)))
            + Field(p.Account.Kind == CashCodes.CashBox ? L("الصندوق", "Cash box") : L("الحساب البنكي", "Bank account"), E(p.Account.Name))
            + (p.Reference is null ? string.Empty : Field(L("المرجع", "Reference"), N(p.Reference)))
            + Field(L("عدد المرفقات", "Attachments"), N(p.AttachmentsCount.ToString(CultureInfo.InvariantCulture)))
            + "</div>" + Signatures(bill ? [L("صرف بواسطة", "Paid by"), L("استلم بواسطة", "Received by")] : [L("المحاسب", "Accountant"), L("أمين الصندوق", "Cashier")]);
        return await printer.OutputAsync(printer.Shell(p.Number, body, p.Status == CashCodes.Void ? "VOID" : null, mark), format, p.Number, ct).ConfigureAwait(false);
    }

    public async Task<GeneratedFile> ReturnAsync(string token, string? format, CancellationToken ct)
    {
        var ret = await support.FindAsync<ApArReturn>(token, ct).ConfigureAwait(false);
        var d = await returns.DetailsAsync(ret.Id, ct).ConfigureAwait(false);
        var r = d.Return;
        var bill = r.Kind == ApArCodes.SupplierBill;
        var mark = await MarkAsync(ApArReturnService.EntityType, ret.Id, ct).ConfigureAwait(false);
        var qrText = bill ? null : await einvoice.ApArQrAsync(null, ret.Id, ct).ConfigureAwait(false);
        var body = printer.Header(await CompanyAsync(ct).ConfigureAwait(false))
            + $"<h1>{E(bill ? L("مرتجع مشتريات", "Purchase return") : L("إشعار دائن", "Credit note"))}</h1>"
            + $"<div class=\"meta\">{L("رقم", "No.")} {N(r.Number)} · {L("التاريخ", "Date")} {N(D(r.ReturnDate))}</div><div class=\"parties\"><div class=\"fields\">"
            + Field(bill ? L("المورد", "Supplier") : L("العميل", "Customer"), E(r.Party.Name))
            + Field(L("المستند الأصلي", "Original document"), N(r.Document.Number))
            + Field(L("السبب", "Reason"), E(r.Reason))
            + Field(L("المبلغ", "Amount"), N(M(r.Amount)))
            + Field(L("الضريبة", "Tax"), N(M(r.Tax)))
            + (r.Deduction > 0 ? Field(L("الخصم المسترد", "Deduction returned"), N(M(-r.Deduction))) : string.Empty)
            + Field(L("الإجمالي", "Total"), N(M(r.Total)))
            + Field(L("المبلغ كتابة", "Amount in words"), E(d.AmountInWords))
            + "</div>" + (string.IsNullOrEmpty(qrText) ? string.Empty : $"<div class=\"qr\">{qr.Svg(qrText)}</div>") + "</div>"
            + Signatures([L("أعدّه", "Prepared by"), L("اعتمده", "Approved by")]);
        return await printer.OutputAsync(printer.Shell(r.Number, body, r.Status == ApArCodes.Void ? "VOID" : null, mark), format, r.Number, ct).ConfigureAwait(false);
    }
}
