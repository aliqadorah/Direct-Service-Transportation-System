using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Contracts.Invoicing;

/// <summary>Stored values of invoices and credit notes.</summary>
public static class InvoiceCodes
{
    public const string Trip = "Trip";
    public const string Manual = "Manual";
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string Closed = "Closed";
    public const string Void = "Void";
    public const string Posted = "Posted";
    public const string Claim = "Claim";
    public const string TaxInvoice = "TaxInvoice";
}

/// <summary>A manual invoice line: description, quantity and unit price, each &gt; 0 (FR-039, 7.3.1).</summary>
public sealed record ManualLineRequest(string? Description, decimal? Quantity, decimal? UnitPrice);

/// <summary>
/// An invoice (7.3.1). Trip invoices list the customer's unbilled trips by token; manual invoices list lines. <see cref="Approve"/>
/// = "Save &amp; approve"; otherwise Save creates a draft while the company's invoice-approval setting is on (FR-041).
/// </summary>
public sealed record InvoiceRequest(
    string? CustomerId, string? InvoiceType, DateOnly? InvoiceDate, DateOnly? DueDate, IReadOnlyList<string>? TripIds, IReadOnlyList<ManualLineRequest>? Lines,
    decimal? Discount, decimal? ExtraFees, string? ExtraFeesDescription, string? LayoutType, string? Notes, bool Approve, string? ClientId, string? RowVersion);

/// <summary>
/// An invoice with its totals (8.3). <see cref="PaymentState"/>: Draft, Unpaid, PartiallyPaid, PartiallyReturned, Closed, Void (7.3.2);
/// <see cref="EInvoiceStatus"/>: NotRequired / Queued / Submitted / Rejected (B7).
/// </summary>
public sealed record InvoiceResponse(
    string Id, string? Number, string InvoiceType, Ref Customer, DateOnly InvoiceDate, DateOnly DueDate,
    decimal Subtotal, decimal Discount, decimal ExtraFees, string? ExtraFeesDescription, decimal BaseAmount, decimal TaxRate, decimal Tax, decimal Total,
    decimal PaidAmount, decimal ReturnedAmount, decimal RemainingAmount, string Status, string PaymentState, bool IsOverdue, string LayoutType, string? Notes,
    string? ApprovedBy, DateTime? ApprovedAt, DocRef? JournalEntry, bool PostingPending, string? VoidReason, string EInvoiceStatus, int LineCount,
    DateTime CreatedAt, string RowVersion);

/// <summary>An invoice line; trip lines carry the trip's annex fields (FR-056).</summary>
public sealed record InvoiceLineResponse(
    string Id, int SortOrder, Ref? Trip, string? Description, decimal Quantity, decimal UnitPrice, decimal Amount, decimal ReturnedQuantity,
    DateOnly? LoadingDate, DateOnly? UnloadingDate, string? TruckPlate, string? Driver, string? LoadingSite, string? UnloadingSite, string? Material,
    decimal? LoadedWeight, decimal? UnloadedWeight, IReadOnlyDictionary<string, string>? Custom = null);

/// <summary>A credit note shown on its invoice.</summary>
public sealed record CreditNoteSummary(string Id, string Number, DateOnly CreditDate, decimal Total, string Status);

/// <summary>Invoice view (FR-048): lines, credit notes, receipts (B8), amount in words, history.</summary>
public sealed record InvoiceDetails(InvoiceResponse Invoice, IReadOnlyList<InvoiceLineResponse> Lines, IReadOnlyList<CreditNoteSummary> CreditNotes,
    string AmountInWords, IReadOnlyList<HistoryEntry> History);

/// <summary>Invoice list filters (FR-047): status, customer, period, paid/unpaid, overdue, e-invoice status, number search, sort, paging.</summary>
public sealed record InvoiceListQuery(
    string? Status, string? Customer, DateOnly? From, DateOnly? To, string? Paid, bool? Overdue, string? EInvoice, string? Q, string? Sort, int Page = 1, int PageSize = 50);

/// <summary>Totals of the filtered invoice list.</summary>
public sealed record InvoiceListTotals(int Invoices, decimal Total, decimal Paid, decimal Returned, decimal Remaining);

public sealed record InvoicePage(PagedResult<InvoiceResponse> Page, InvoiceListTotals Totals);

/// <summary>Trip picker filters (FR-038): the customer's active, unbilled trips by period, loading site, unloading site, material.</summary>
public sealed record UnbilledTripsQuery(string? Customer, DateOnly? From, DateOnly? To, string? LoadingSite, string? UnloadingSite, string? Material);

/// <summary>The trip picker's trips and totals (select-all uses every trip listed).</summary>
public sealed record UnbilledTripsResponse(IReadOnlyList<TripResponse> Trips, int Count, decimal Quantity, decimal Revenue);

public sealed record InvoiceVoidRequest(string? Reason, string? RowVersion);

/// <summary>Deleting a draft: reason and the version last seen (review L7).</summary>
public sealed record InvoiceDeleteRequest(string? Reason, string? RowVersion);

/// <summary>A returned line: the invoice line and, for manual lines, the quantity (trip lines return the whole trip).</summary>
public sealed record CreditLineRequest(string? InvoiceLineId, decimal? Quantity);

/// <summary>A credit note from an approved invoice (FR-049): date, reason, returned lines.</summary>
public sealed record CreditNoteRequest(string? InvoiceId, DateOnly? CreditDate, string? Reason, IReadOnlyList<CreditLineRequest>? Lines, string? ClientId);

public sealed record CreditNoteResponse(
    string Id, string Number, DocRef Invoice, Ref Customer, DateOnly CreditDate, string Reason, decimal Subtotal, decimal DiscountShare, decimal FeesShare,
    decimal BaseAmount, decimal Tax, decimal Total, string Status, DocRef? JournalEntry, bool PostingPending, string? VoidReason, string EInvoiceStatus,
    DateTime CreatedAt, string RowVersion);

public sealed record CreditNoteLineResponse(string Id, string InvoiceLineId, Ref? Trip, string? Description, decimal Quantity, decimal Amount);

public sealed record CreditNoteDetails(CreditNoteResponse CreditNote, IReadOnlyList<CreditNoteLineResponse> Lines, string AmountInWords, IReadOnlyList<HistoryEntry> History);

public sealed record CreditNoteListQuery(string? Status, string? Customer, string? Invoice, DateOnly? From, DateOnly? To, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record CreditNoteVoidRequest(string? Reason, string? RowVersion);

/// <summary>
/// A print layout (FR-054 … FR-056): title, header band, grouping of the claim's first page, annex columns in order, labels, footer,
/// signature boxes, logo and QR.
/// </summary>
public sealed record InvoiceLayoutRequest(
    string? Title, string? BandText, string? Grouping, IReadOnlyList<string>? AnnexColumns, IReadOnlyDictionary<string, string>? Labels, string? FooterText,
    IReadOnlyList<string>? Signatures, bool ShowLogo, bool ShowQr, string? RowVersion);

public sealed record InvoiceLayoutResponse(
    string LayoutType, string Title, string? BandText, string Grouping, IReadOnlyList<string> AnnexColumns, IReadOnlyDictionary<string, string> Labels,
    string? FooterText, IReadOnlyList<string> Signatures, bool ShowLogo, bool ShowQr, IReadOnlyList<string> AvailableColumns, string? RowVersion);
