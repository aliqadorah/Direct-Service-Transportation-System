using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Contracts.ApAr;

/// <summary>Stored values of accounts payable and receivable (design: acc.ApArParties, ApArDocuments, ApArPayments, ApArReturns).</summary>
public static class ApArCodes
{
    public const string Supplier = "Supplier";
    public const string Customer = "Customer";
    public const string SupplierBill = "SupplierBill";
    public const string Receivable = "Receivable";
    public const string Posted = "Posted";
    public const string Void = "Void";
    public const string Active = "Active";

    /// <summary>Payment state shown on documents (derived from the remaining balance).</summary>
    public const string Open = "Open";
    public const string Partial = "Partial";
    public const string Closed = "Closed";
}

// ------------------------------------------------------------------ parties (FR-122)

/// <summary>An accounting party: supplier or customer for non-transport documents; the control account must match the role.</summary>
public sealed record ApArPartyRequest(
    string? Role, string? Name, string? TaxNumber, string? Phone, string? Address, string? ControlAccountId, int? PaymentTermsDays, string? ClientId, string? RowVersion);

/// <summary>A party with its open balance (remaining of its posted documents) and the transport customers linked to it.</summary>
public sealed record ApArPartyResponse(
    string Id, string Role, string Name, string? TaxNumber, string? Phone, string? Address, AccountRef ControlAccount, int PaymentTermsDays, string Status,
    decimal OpenBalance, IReadOnlyList<Ref> LinkedCustomers, DateTime CreatedAt, string RowVersion);

public sealed record ApArPartyDetails(ApArPartyResponse Party, IReadOnlyList<ReferenceCount> References, IReadOnlyList<HistoryEntry> History);

public sealed record ApArPartyListQuery(string? Role, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

/// <summary>A party choice for documents: accounting parties of the role and, for receivables, the transport customers (FR-122).</summary>
public sealed record ApArPartyOption(string Type, string Id, string Name, string? TaxNumber, int PaymentTermsDays);

// ------------------------------------------------------------------ documents (FR-123, FR-124)

/// <summary>A document line: description, expense/asset (bills) or revenue (receivables) account, amount before tax, tax rate (company rate when empty), cost centre.</summary>
public sealed record ApArLineRequest(string? Description, string? AccountId, decimal? Amount, decimal? TaxRate, string? CostCentreId);

/// <summary>
/// A supplier bill or receivable document. Receivables name an accounting customer (<see cref="PartyId"/>) or a transport customer
/// (<see cref="CustomerId"/>, its linked accounting party is used). Due date defaults to the date + the party's payment terms.
/// </summary>
public sealed record ApArDocumentRequest(
    string? PartyId, string? CustomerId, DateOnly? DocumentDate, DateOnly? DueDate, string? Reference, IReadOnlyList<ApArLineRequest>? Lines, string? ClientId);

public sealed record ApArLineResponse(string Id, string Description, AccountRef Account, decimal Amount, decimal TaxRate, Ref? CostCentre);

public sealed record ApArDocumentResponse(
    string Id, string Kind, string Number, Ref Party, DateOnly DocumentDate, DateOnly DueDate, string? Reference, decimal Subtotal, decimal Tax, decimal Total,
    decimal PaidAmount, decimal ReturnedAmount, decimal RemainingAmount, string Status, string PaymentStatus, int DaysOverdue, DocRef? JournalEntry,
    bool PostingPending, string? EInvoiceStatus, string? VoidReason, DateTime CreatedAt, string RowVersion);

public sealed record ApArDocumentDetails(
    ApArDocumentResponse Document, IReadOnlyList<ApArLineResponse> Lines, IReadOnlyList<ApArPaymentResponse> Payments, IReadOnlyList<ApArReturnResponse> Returns,
    string AmountInWords, IReadOnlyList<HistoryEntry> History);

/// <summary>Document list filters: party, period, status (active/void/all), payment state (open/partial/closed/overdue), number/reference search.</summary>
public sealed record ApArDocumentListQuery(
    string? Party, DateOnly? From, DateOnly? To, string? Status, string? Payment, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record ApArDocumentTotals(int Count, decimal Total, decimal Remaining);

public sealed record ApArDocumentPage(PagedResult<ApArDocumentResponse> Page, ApArDocumentTotals Totals);

// ------------------------------------------------------------------ payments and collections (FR-125)

public sealed record ApArPaymentRequest(
    string? DocumentId, DateOnly? PaymentDate, decimal? Amount, string? PaymentMethod, string? CashBoxId, string? BankAccountId, string? Reference, int? AttachmentsCount,
    string? ClientId);

/// <summary>A payment to a supplier (Kind = SupplierBill) or a collection from a customer (Kind = Receivable).</summary>
public sealed record ApArPaymentResponse(
    string Id, string Number, string Kind, DocRef Document, Ref Party, DateOnly PaymentDate, decimal Amount, string PaymentMethod, MoneyAccountRef Account,
    string? Reference, int AttachmentsCount, string Status, DocRef? JournalEntry, bool PostingPending, string? VoidReason, DateTime CreatedAt, string RowVersion);

public sealed record ApArPaymentDetails(ApArPaymentResponse Payment, string AmountInWords, IReadOnlyList<HistoryEntry> History);

public sealed record ApArPaymentListQuery(
    string? Party, string? Document, DateOnly? From, DateOnly? To, string? Method, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record ApArPaymentPage(PagedResult<ApArPaymentResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ returns (FR-126)

/// <summary>A return of an amount before tax; the tax is the document's tax share, the total at most the document's remaining balance.</summary>
public sealed record ApArReturnRequest(string? DocumentId, DateOnly? ReturnDate, decimal? Amount, string? Reason, string? ClientId);

public sealed record ApArReturnResponse(
    string Id, string Number, string Kind, DocRef Document, Ref Party, DateOnly ReturnDate, decimal Amount, decimal Tax, decimal Total, string Reason, string Status,
    DocRef? JournalEntry, bool PostingPending, string? EInvoiceStatus, string? VoidReason, DateTime CreatedAt, string RowVersion);

public sealed record ApArReturnDetails(ApArReturnResponse Return, string AmountInWords, IReadOnlyList<HistoryEntry> History);

public sealed record ApArReturnListQuery(
    string? Party, string? Document, DateOnly? From, DateOnly? To, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record ApArReturnPage(PagedResult<ApArReturnResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ ageing (FR-128, RPT-17, RPT-18)

/// <summary>Amounts due per age bucket as of a date: not due, 1–30, 31–60, 61–90 and over 90 days past due; unapplied payments separate (receivables).</summary>
public sealed record AgeingBuckets(decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90, decimal Total, decimal Unapplied);

/// <summary>One party: a transport customer (Type Customer), or an accounting party (Type ApArParty).</summary>
public sealed record AgeingRow(PartyResponse Party, AgeingBuckets Amounts, int Documents);

public sealed record AgeingReport(string Kind, DateOnly AsOf, IReadOnlyList<AgeingRow> Rows, AgeingBuckets Totals);
