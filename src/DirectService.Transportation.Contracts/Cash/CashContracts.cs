using DirectService.Shared.Contracts.History;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Contracts.Cash;

/// <summary>Stored values of cash and bank documents (design: TRN_Receipts, TRN_Advances, TRN_Settlements, TRN_PartnerPayments).</summary>
public static class CashCodes
{
    public const string Cash = "Cash";
    public const string Bank = "Bank";
    public const string CliQ = "CliQ";
    public const string Active = "Active";
    public const string Void = "Void";
    public const string Shortage = "Shortage";
    public const string Fine = "Fine";
    public const string Other = "Other";
    public const string CashBox = "CashBox";
    public const string BankAccount = "BankAccount";
    /// <summary>Revision 6 (DT-228): method of an advance that carries a settlement's unpaid rest (no money moved).</summary>
    public const string Carried = "Carried";
    /// <summary>Revision 6 (DT-228): kinds of a partner payment — to the partner, or repaid by a partner who owes the company.</summary>
    public const string Payment = "Payment";
    public const string Repayment = "Repayment";
    /// <summary>DT-230: what is done with an amount already paid for a partner trip voided or made cheaper.</summary>
    public const string Return = "Return";
    public const string Deduct = "Deduct";
    public const string NotPaid = "NotPaid";
}

/// <summary>The cash box or bank account of a document.</summary>
public sealed record MoneyAccountRef(string Kind, string Id, string Name);

/// <summary>Void of a cash document: reason and the version last seen.</summary>
/// <summary>DT-230: <paramref name="ForTrip"/> — a partner payment voided because it was not actually made, for that trip (recorded with the trip).</summary>
public sealed record CashVoidRequest(string? Reason, string? RowVersion, string? ForTrip = null);

/// <summary>Count and amount of a filtered list.</summary>
/// <summary>Register totals: <see cref="Count"/> rows matching the filters (paging), <see cref="Amount"/> and <see cref="Included"/> of the documents in the total (active / posted, DT-155).</summary>
public sealed record CashListTotals(int Count, decimal Amount, int Included = 0);

// ------------------------------------------------------------------ receipts (7.4.1)

/// <summary>A customer receipt (FR-067, FR-068): against one of the customer's open invoices, or on account when no invoice is given.</summary>
/// <summary>
/// A receipt. Revision 6: <paramref name="Allocations"/> applies it to one or more of the customer's open invoices (each ≤ the invoice's open
/// balance, together ≤ the amount; the rest stays on account). <paramref name="InvoiceId"/> alone still means one invoice for the whole amount.
/// </summary>
public sealed record ReceiptRequest(
    string? CustomerId, DateOnly? ReceiptDate, string? InvoiceId, decimal? Amount, string? PaymentMethod, string? CashBoxId, string? BankAccountId,
    string? Reference, int? AttachmentsCount, string? Notes, string? ClientId, IReadOnlyList<ReceiptAllocationRequest>? Allocations = null);

public sealed record ReceiptAllocationRequest(string? InvoiceId, decimal? Amount);

/// <summary>Part of a receipt applied to an invoice.</summary>
public sealed record ReceiptAllocationResponse(DocRef Invoice, decimal Amount);

public sealed record ReceiptResponse(
    string Id, string Number, Ref Customer, DocRef? Invoice, DateOnly ReceiptDate, decimal Amount, string PaymentMethod, MoneyAccountRef Account, string? Reference,
    int AttachmentsCount, string? Notes, string Status, DocRef? JournalEntry, bool PostingPending, string? VoidReason, DateTime CreatedAt, string RowVersion,
    IReadOnlyList<ReceiptAllocationResponse>? Allocations = null, decimal Unallocated = 0);

public sealed record ReceiptDetails(ReceiptResponse Receipt, string AmountInWords, IReadOnlyList<HistoryEntry> History);

/// <summary>Receipts list filters (FR-071): customer, period, method, cash box / bank, applied (invoice) or on account, status, number search.</summary>
public sealed record ReceiptListQuery(
    string? Customer, DateOnly? From, DateOnly? To, string? Method, string? CashBox, string? BankAccount, string? Applied, string? Status, string? Q, string? Sort,
    int Page = 1, int PageSize = 50, string? Invoice = null);

public sealed record ReceiptPage(PagedResult<ReceiptResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ driver advances (7.4.2)

/// <summary>A driver advance (FR-072); edits (FR-073) send the row version.</summary>
public sealed record AdvanceRequest(
    string? DriverId, DateOnly? AdvanceDate, decimal? Amount, string? PaymentMethod, string? CashBoxId, string? BankAccountId, string? Reference, string? Notes,
    string? ClientId, string? RowVersion);

public sealed record AdvanceResponse(
    string Id, string Number, Ref Driver, DateOnly AdvanceDate, decimal Amount, string PaymentMethod, MoneyAccountRef Account, string? Reference, string? Notes,
    string Status, DocRef? Settlement, DocRef? JournalEntry, bool PostingPending, string? VoidReason, DateTime CreatedAt, string RowVersion,
    DocRef? CarriedFrom = null);

public sealed record AdvanceDetails(AdvanceResponse Advance, string AmountInWords, IReadOnlyList<HistoryEntry> History);

public sealed record AdvanceListQuery(
    string? Driver, DateOnly? From, DateOnly? To, string? Method, string? Settled, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record AdvancePage(PagedResult<AdvanceResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ settlements (7.4.3, 8.5)

/// <summary>An open trip of a driver: receipt, date, route, material, wage quantity × rate = wage, shortage flag.</summary>
public sealed record SettlementTripItem(
    string Id, string ReceiptNo, DateOnly TripDate, string Route, string Material, decimal WageQty, decimal DriverRate, decimal DriverWage, bool IsShortageFlagged,
    decimal? ShortageWeight);

public sealed record SettlementAdvanceItem(string Id, string Number, DateOnly AdvanceDate, decimal Amount);

/// <summary>A suggested shortage deduction from a flagged trip (FR-078, DT-96): shortage beyond tolerance × the trip's price per ton.</summary>
public sealed record SuggestedDeduction(string TripId, string ReceiptNo, decimal ShortageWeight, decimal ExcessWeight, decimal Amount);

/// <summary>
/// DT-276 / DT-277: a manual or opening journal entry naming the driver / partner that is part of its previous balance (still open, or settled by
/// a document): the entry, its date and description, and the amount (above zero owed to the driver / partner, below zero owed by it).
/// </summary>
public sealed record PreviousBalanceEntry(DocRef Entry, DateOnly Date, string Description, decimal Amount);

/// <summary>
/// Open items of a driver up to a date, pre-selected for a settlement (FR-076, FR-077), with the 8.5 totals. DT-276: <paramref name="PreviousBalance"/>
/// — the driver's balance from manual / opening journal entries up to the date (<paramref name="PreviousBalanceEntries"/>), part of the net:
/// Net = Gross − AdvancesTotal + PreviousBalance.
/// </summary>
public sealed record SettlementPreview(
    Ref Driver, string? TruckPlate, DateOnly Date, IReadOnlyList<SettlementTripItem> Trips, IReadOnlyList<SettlementAdvanceItem> Advances,
    IReadOnlyList<SuggestedDeduction> Suggestions, decimal Gross, decimal AdvancesTotal, decimal Net, decimal PreviousBalance = 0,
    IReadOnlyList<PreviousBalanceEntry>? PreviousBalanceEntries = null);

public sealed record SettlementDeductionRequest(string? Type, decimal? Amount, string? Note);

/// <summary>
/// A settlement (FR-077 … FR-080): driver, date, the trips and advances included (omitted = every open item up to the date), deductions,
/// method and cash box / bank for the net. Revision 6 (DT-228): when advances and deductions exceed the wages, <paramref name="RepaidAmount"/>
/// (&gt; 0, at most the shortfall) is what the driver pays back now with that method and cash box / bank (reference required for bank
/// transfer and CliQ); the rest is carried to the next settlement as a new open advance. DT-276: the driver's previous balance from manual /
/// opening entries up to the date is settled too unless <paramref name="IncludePreviousBalance"/> is false; a settlement may then have no trips.
/// </summary>
public sealed record SettlementRequest(
    string? DriverId, DateOnly? SettlementDate, IReadOnlyList<string>? TripIds, IReadOnlyList<string>? AdvanceIds, IReadOnlyList<SettlementDeductionRequest>? Deductions,
    string? PaymentMethod, string? CashBoxId, string? BankAccountId, string? Reference, string? ClientId, decimal? RepaidAmount = null,
    bool? IncludePreviousBalance = null);

public sealed record SettlementDeductionResponse(string Id, string Type, decimal Amount, string? Note);

public sealed record SettlementResponse(
    string Id, string Number, Ref Driver, DateOnly SettlementDate, decimal GrossWages, decimal AdvancesTotal, decimal DeductionsTotal, decimal NetPayable,
    string PaymentMethod, MoneyAccountRef Account, string? Reference, string Status, DocRef? JournalEntry, bool PostingPending, string? VoidReason,
    DateTime CreatedAt, string RowVersion, decimal RepaidAmount = 0, decimal CarriedForward = 0, DocRef? CarriedAdvance = null, decimal PreviousBalance = 0);

public sealed record SettlementDetails(
    SettlementResponse Settlement, IReadOnlyList<SettlementTripItem> Trips, IReadOnlyList<SettlementAdvanceItem> Advances,
    IReadOnlyList<SettlementDeductionResponse> Deductions, string AmountInWords, IReadOnlyList<HistoryEntry> History,
    IReadOnlyList<PreviousBalanceEntry>? PreviousBalanceEntries = null);

public sealed record SettlementListQuery(string? Driver, DateOnly? From, DateOnly? To, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record SettlementPage(PagedResult<SettlementResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ partner payments (7.4.4)

/// <summary>
/// A partner payment (FR-085): never above the partner's current balance due. Revision 6 (DT-228): <paramref name="Kind"/> "Repayment" records
/// money received from a partner who owes the company (balance below zero), never above what the partner owes. DT-277: the partner's previous
/// balance from manual / opening entries is settled with the payment (a repayment: only what the partner owes) unless
/// <paramref name="IncludePreviousBalance"/> is false; a payment that only sets off what the partner owes against its trips may be 0.
/// </summary>
public sealed record PartnerPaymentRequest(
    string? PartnerId, DateOnly? PaymentDate, decimal? Amount, string? PaymentMethod, string? CashBoxId, string? BankAccountId, string? Reference, string? Notes,
    string? ClientId, string? Kind = null, bool? IncludePreviousBalance = null);

public sealed record PartnerPaymentResponse(
    string Id, string Number, Ref Partner, DateOnly PaymentDate, decimal Amount, string PaymentMethod, MoneyAccountRef Account, string? Reference, string? Notes,
    string Status, DocRef? JournalEntry, bool PostingPending, string? VoidReason, DateTime CreatedAt, string RowVersion, string Kind = CashCodes.Payment,
    decimal DeductedAmount = 0, decimal PreviousBalance = 0);

public sealed record PartnerPaymentDetails(PartnerPaymentResponse Payment, string AmountInWords, IReadOnlyList<HistoryEntry> History,
    IReadOnlyList<PreviousBalanceEntry>? PreviousBalanceEntries = null);

public sealed record PartnerPaymentListQuery(
    string? Partner, DateOnly? From, DateOnly? To, string? Method, string? Status, string? Q, string? Sort, int Page = 1, int PageSize = 50);

public sealed record PartnerPaymentPage(PagedResult<PartnerPaymentResponse> Page, CashListTotals Totals);

// ------------------------------------------------------------------ accounts and statements (FR-076, FR-083, FR-084, FR-088)

/// <summary>
/// Driver account (FR-076): open trips and advances, gross / advances / net due, balance owed to the driver, settlement history. DT-276: the
/// previous balance from manual / opening entries still open (part of NetDue) and its entries.
/// </summary>
public sealed record DriverAccountResponse(
    Ref Driver, string? TruckPlate, IReadOnlyList<SettlementTripItem> OpenTrips, IReadOnlyList<SettlementAdvanceItem> OpenAdvances, decimal Gross,
    decimal AdvancesTotal, decimal NetDue, decimal Balance, IReadOnlyList<SettlementResponse> Settlements, decimal PreviousBalance = 0,
    IReadOnlyList<PreviousBalanceEntry>? PreviousBalanceEntries = null);

/// <summary>Partner account (FR-084): partner cost of active trips, payments, balance due.</summary>
/// <summary>DT-230: <paramref name="ToDeduct"/> — of what the partner owes, the part to be deducted from the next payment(s) (the rest is to be returned).</summary>
/// <summary>DT-277: <paramref name="Paid"/> — what payments paid for the trips; <paramref name="PreviousBalance"/> — the open balance from manual / opening entries (part of BalanceDue).</summary>
public sealed record PartnerAccountResponse(Ref Partner, int Trips, decimal Cost, decimal Paid, decimal BalanceDue, IReadOnlyList<PartnerPaymentResponse> RecentPayments,
    decimal ToDeduct = 0, decimal PreviousBalance = 0, IReadOnlyList<PreviousBalanceEntry>? PreviousBalanceEntries = null);

/// <summary>
/// A statement line: date, kind (Trip, Advance, Deduction, Settlement, Payment), document, description, amounts that increase or
/// decrease what the company owes the driver / partner, running balance; shipment details for trips (FR-088).
/// </summary>
public sealed record StatementLine(
    DateOnly Date, string Kind, DocRef Document, string Description, decimal Increase, decimal Decrease, decimal Balance,
    string? Route = null, decimal? Weight = null, decimal? Rate = null);

/// <summary>A driver or partner statement for a period (FR-083, FR-088): opening, lines with running balance, totals, closing.</summary>
public sealed record StatementResponse(
    Ref Party, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<StatementLine> Lines, decimal TotalIncrease, decimal TotalDecrease, decimal Closing);
