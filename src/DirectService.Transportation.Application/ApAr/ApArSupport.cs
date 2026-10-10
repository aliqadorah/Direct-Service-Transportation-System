using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.ApAr;

/// <summary>Rules shared by AP/AR documents, payments and returns (7.6.4).</summary>
public static class ApArRules
{
    /// <summary>Document kind from a route or filter value: "bills" / "supplierbill" → SupplierBill, "receivables" / "receivable" → Receivable.</summary>
    public static string Kind(string? value, string field = "kind") => (value?.Trim().ToLowerInvariant()) switch
    {
        "bills" or "bill" or "supplierbill" or "supplier-bills" or "payables" => ApArCodes.SupplierBill,
        "receivables" or "receivable" => ApArCodes.Receivable,
        _ => throw Fail.Field(field, ErrorCodes.ValidationFormat, field),
    };

    public static string? KindOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : Kind(value);

    /// <summary>Party role of a document kind (design: "Party role must match the kind").</summary>
    public static string RoleOf(string kind) => kind == ApArCodes.SupplierBill ? ApArCodes.Supplier : ApArCodes.Customer;

    /// <summary>Number sequence of a document kind (D-16: BIL-, ARD-).</summary>
    public static string Sequence(string kind) => kind == ApArCodes.SupplierBill ? DocumentTypes.SupplierBill : DocumentTypes.Receivable;

    /// <summary>Remaining = Total − Paid − Returned (maintained by the server; CHECK ≥ 0).</summary>
    public static void Rebalance(ApArDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        d.RemainingAmount = d.Total - d.PaidAmount - d.ReturnedAmount;
        if (d.RemainingAmount < 0) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
    }

    /// <summary>Open / Partial / Closed from the balance; Void documents keep their status.</summary>
    public static string PaymentStatus(string status, decimal total, decimal remaining) =>
        status == ApArCodes.Void ? ApArCodes.Void : remaining == 0 ? ApArCodes.Closed : remaining < total ? ApArCodes.Partial : ApArCodes.Open;

    /// <summary>Tax of the lines: Σ amount × rate / 100, rounded half-up once (BR-021).</summary>
    public static decimal Tax(IEnumerable<(decimal Amount, decimal Rate)> lines, int decimals) => Money.Round(lines.Sum(l => l.Amount * l.Rate / 100m), decimals);
}

/// <summary>Pending-posting retries and journal links of AP/AR documents, payments and returns (module ACC, FR-101).</summary>
public abstract class ApArPostingHandler<T>(IAppDbContext db, IUrlTokenService tokens) : CashPostingHandler<T>(db, tokens) where T : class, ICashDocument
{
    public override string SourceModule => AccountingCodes.ModuleAcc;
}

/// <summary>Reversal of an AP/AR posting on void (BR-035): the posted entry is reversed, a waiting pending posting closed.</summary>
internal static class ApArPosting
{
    public static Task ReverseAsync(IAppDbContext db, PostingEngine posting, ICashDocument d, string sourceType, string description, CancellationToken ct) =>
        CashPosting.ReverseAsync(db, posting, d, sourceType, description, ct, AccountingCodes.ModuleAcc);

    /// <summary>Pending flag of a source document of module ACC.</summary>
    public static IQueryable<bool> Pending(IAppDbContext db, string sourceType, long id) =>
        db.Set<PendingPosting>().Where(p => p.SourceModule == AccountingCodes.ModuleAcc && p.SourceType == sourceType && p.SourceId == id && p.ResolvedAt == null).Select(p => true);
}
