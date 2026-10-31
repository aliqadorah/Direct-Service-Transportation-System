
namespace DirectService.Accounting.Contracts.Accounting;

// Identifiers are always URL tokens (SEC-22); amounts are in the currency's major unit with its decimals (BR-020).

/// <summary>An account shown by code and name (Arabic name; English when present).</summary>
public sealed record AccountRef(string Id, string Code, string NameAr, string? NameEn);

// ---------------------------------------------------------------- chart of accounts (FR-107 … FR-110)

/// <summary>One account of the chart. The list is flat, ordered by code; <see cref="Level"/> and <see cref="ParentId"/> build the tree.</summary>
public sealed record AccountResponse(
    string Id, string Code, string NameAr, string? NameEn, string AccountType, string? ParentId, string? ParentCode, int Level,
    bool IsPostable, bool HasChildren, decimal OpeningDebit, decimal OpeningCredit, bool IsControlAccount, string? ControlPartyType,
    string Status, string RowVersion, bool IsTaxAccount = false, string? ProfitLossGroup = null);

/// <summary>An active postable account for pick lists, with its type, whether lines on it need a party (control account) and whether it is a
/// tax account (offered as the tax account of journal lines, DT-256).</summary>
public sealed record AccountOption(AccountRef Account, string AccountType, bool IsControlAccount, string? ControlPartyType, bool IsTaxAccount = false);

public sealed record AccountRequest(
    string? Code, string? NameAr, string? NameEn, string? AccountType, string? ParentId, bool IsPostable,
    bool IsControlAccount, string? ControlPartyType, string? RowVersion, bool IsTaxAccount = false, string? ProfitLossGroup = null);

/// <summary>
/// Sections of the profit and loss statement (DT-264): revenue is Sales or OtherIncome, expenses CostOfSales, Operating or OtherExpense. An
/// account without its own section takes the nearest ancestor's, else the default of its type (Sales, Operating).
/// </summary>
public static class ProfitLossGroups
{
    public const string Sales = "Sales";
    public const string CostOfSales = "CostOfSales";
    public const string Operating = "Operating";
    public const string OtherIncome = "OtherIncome";
    public const string OtherExpense = "OtherExpense";

    /// <summary>The statement order: sales, cost of sales, (gross profit), operating expenses, other income, other expenses, (net profit).</summary>
    public static readonly IReadOnlyList<string> Order = [Sales, CostOfSales, Operating, OtherIncome, OtherExpense];

    public static IReadOnlyList<string> For(string? accountType) => accountType switch
    {
        "Revenue" => [Sales, OtherIncome],
        "Expense" => [CostOfSales, Operating, OtherExpense],
        _ => [],
    };

    public static string? Default(string? accountType) => accountType switch { "Revenue" => Sales, "Expense" => Operating, _ => null };

    /// <summary>Revenue sections add to the profit, the others subtract.</summary>
    public static bool IsRevenue(string group) => group is Sales or OtherIncome;
}

public sealed record MoveAccountRequest(string? ParentId, string? RowVersion);

/// <summary>Reason for a soft delete (BR-041).</summary>
public sealed record DeleteRequest(string? Reason);

public sealed record StatusChangeRequest(string? RowVersion);

/// <summary>Accounts, cash box and posting keys created from the built-in transport template (FR-107, D-10).</summary>
public sealed record ChartTemplateResult(int Accounts, int CashBoxes, int PostingKeys);

/// <summary>
/// "Add the default tax accounts" (DT-256): VAT on sales (liability), VAT on purchases (asset) and VAT expenses (expense). For each kind without a
/// tax account, the template account with its code is marked when it is there, otherwise a new account is added with the next free code
/// under the right group. <paramref name="Accounts"/> are the tax accounts added or marked.
/// </summary>
public sealed record DefaultTaxAccountsResult(int Added, int Marked, IReadOnlyList<AccountRef> Accounts);

// ---------------------------------------------------------------- tax codes (owner request 25 Oct 2026, DT-257)

/// <summary>A tax code of Settings → Tax codes; <paramref name="InUse"/> = on posted journal lines (no delete, no rate change).</summary>
public sealed record TaxCodeResponse(string Id, string Code, decimal Rate, string NameAr, string? NameEn, string Status, int SortOrder, bool InUse, string RowVersion);

public sealed record TaxCodeRequest(string? Code, decimal? Rate, string? NameAr, string? NameEn, int? SortOrder, string? RowVersion);

/// <summary>A tax code on a line or in a pick list.</summary>
public sealed record TaxCodeRef(string Id, string Code, decimal Rate, string NameAr, string? NameEn);

// ---------------------------------------------------------------- opening balances (FR-110)

public sealed record OpeningBalanceLine(string AccountId, decimal Debit, decimal Credit);

public sealed record OpeningBalancesRequest(IReadOnlyList<OpeningBalanceLine>? Lines);

public sealed record OpeningBalancesResponse(
    IReadOnlyList<OpeningBalanceRow> Accounts, decimal TotalDebit, decimal TotalCredit, decimal Difference,
    bool SetupOpen, OpeningEntryInfo? PostedEntry, decimal DifferenceAccountBalance);

public sealed record OpeningBalanceRow(AccountRef Account, string AccountType, decimal Debit, decimal Credit);

public sealed record OpeningEntryInfo(string Id, string Number, DateOnly EntryDate, decimal TotalDebit);

public sealed record PostOpeningRequest(DateOnly? EntryDate);

// ---------------------------------------------------------------- posting keys (FR-092 … FR-095)

public sealed record PostingKeyResponse(string Key, string NameAr, string NameEn, bool Required, AccountRef? Account, string Status);

public sealed record PostingKeyRequest(string? AccountId);

/// <summary>Banner state shown in the app shell (FR-094, FR-110, FR-101). <c>MissingPostingKeyNames</c> are the missing keys' names in the reader's
/// language, for display (DT-224); <c>MissingPostingKeys</c> are the codes, for programs.</summary>
public sealed record AccountingStatusResponse(
    bool PostingReady, IReadOnlyList<string> MissingPostingKeys, bool OpeningDifferenceWarning, decimal OpeningDifferenceBalance,
    int PendingPostings, IReadOnlyList<string>? MissingPostingKeyNames = null);

// ---------------------------------------------------------------- cost centres, cash boxes, bank accounts (FR-129, FR-131)

public sealed record CostCentreResponse(string Id, string Code, string Name, string Status, string RowVersion);

public sealed record CostCentreRequest(string? Code, string? Name, string? RowVersion);

public sealed record CashBoxResponse(string Id, string Name, AccountRef Account, string Status, string RowVersion);

public sealed record CashBoxRequest(string? Name, string? AccountId, string? RowVersion);

/// <summary>Active cash boxes and bank accounts (pick lists of cash documents).</summary>
public sealed record MoneyAccountOptions(IReadOnlyList<MasterOption> CashBoxes, IReadOnlyList<MasterOption> BankAccounts, DateOnly Today);

public sealed record BankAccountResponse(
    string Id, string Name, string BankName, string? AccountNumber, string? Iban, string CurrencyCode, AccountRef Account, string Status,
    string RowVersion);

public sealed record BankAccountRequest(
    string? Name, string? BankName, string? AccountNumber, string? Iban, string? CurrencyCode, string? AccountId, string? RowVersion);

// ---------------------------------------------------------------- periods (FR-132 … FR-134)

public sealed record PeriodResponse(string PeriodMonth, string Status, DateTime? ClosedAt, string? ClosedBy, string? Reason, string? RowVersion);

public sealed record PeriodActionRequest(string? Reason, bool Confirm);

public sealed record PreCloseCheckResponse(
    string PeriodMonth, int PendingPostings, int UnapprovedDrafts, IReadOnlyList<UnreconciledAccount> UnreconciledControlAccounts, bool Clean);

public sealed record UnreconciledAccount(AccountRef Account, decimal LinesWithoutParty);

// ---------------------------------------------------------------- journal (FR-096 … FR-104, FR-111 … FR-115)

/// <summary>A party on an entry line (FR-106): Customer, Driver, Partner (transport) or ApArParty (accounting).</summary>
public sealed record PartyRequest(string? Type, string? Id);

public sealed record PartyResponse(string Module, string Type, string? Id, string? Name);

/// <summary>
/// A manual journal line. Debit or credit is the amount <b>before</b> tax (DT-255). <paramref name="Reference"/> is the line reference (DT-254;
/// empty = the entry's Reference); <paramref name="TaxCodeId"/> a tax code; <paramref name="TaxAccountId"/> the tax account, required for a rate above 0 %
/// — posting adds the tax on the same side to that account as an automatic line.
/// </summary>
public sealed record JournalLineRequest(string? AccountId, decimal Debit, decimal Credit, string? CostCentreId, PartyRequest? Party, string? Note,
    string? Reference = null, string? TaxCodeId = null, string? TaxAccountId = null);

/// <summary>A manual entry; <paramref name="Reference"/> is the free-text reference (المرجع, up to 100 characters, revision 6).</summary>
public sealed record ManualEntryRequest(DateOnly? EntryDate, string? Description, IReadOnlyList<JournalLineRequest>? Lines, string? RowVersion,
    string? Reference = null);

/// <summary>A journal draft (revision 6): no number, not posted; posted later through <c>POST …/journal-drafts/{id}/post</c>.</summary>
public sealed record JournalDraftResponse(
    string Id, DateOnly? EntryDate, string? Description, string? Reference, IReadOnlyList<JournalDraftLineResponse> Lines,
    decimal TotalDebit, decimal TotalCredit, DateTime CreatedAt, string? CreatedBy, string RowVersion);

/// <summary>A draft line as entered (account, cost centre and party may still be missing).</summary>
public sealed record JournalDraftLineResponse(AccountRef? Account, decimal Debit, decimal Credit, Ref? CostCentre, PartyResponse? Party, string? Note,
    string? Reference = null, TaxCodeRef? TaxCode = null, AccountRef? TaxAccount = null);

public sealed record VoidRequest(string? Reason, string? RowVersion);

/// <summary>An action on a record that only needs its row version (optimistic concurrency).</summary>
public sealed record RowVersionRequest(string? RowVersion);

/// <summary>
/// Journal list filters (FR-102, FR-115). Months are YYYY-MM; status: Posted, Reversed, Void, Cancelled (DT-279: Reversed or Void — every
/// cancelled entry), or Draft (revision 6: drafts only; with no status, drafts are listed first). <paramref name="Search"/> matches the number, the description, the entry's reference or a line reference (DT-254).
/// </summary>
public sealed record JournalQuery(
    string? PeriodFrom, string? PeriodTo, string? Number, string? AccountId, string? CostCentreId, string? PartyType, string? PartyId,
    bool? Manual, string? Status, string? SourceType, int Page = 1, int PageSize = 50, string? Search = null);

/// <summary>
/// A row of the journal list; a draft (Status "Draft") has no number and its <paramref name="Id"/> is the draft's. Owner findings 27 Oct 2026
/// (DT-279): <paramref name="CancelReason"/> — why a cancelled entry (Reversed / Void) was cancelled, in the reader's language.
/// </summary>
public sealed record JournalEntrySummary(
    string Id, string Number, DateOnly EntryDate, string PeriodMonth, string Description, string SourceModule, string? SourceType,
    bool IsManual, string Status, decimal TotalDebit, decimal TotalCredit, string? ReversesEntryNumber, string? ReversedByEntryNumber,
    string? Reference = null, string? CancelReason = null);

public sealed record JournalEntryResponse(
    string Id, string Number, DateOnly EntryDate, string PeriodMonth, string Description, string SourceModule, string? SourceType,
    SourceDocumentRef? Source, bool IsManual, string Status, decimal TotalDebit, decimal TotalCredit,
    EntryLinkRef? ReversesEntry, EntryLinkRef? ReversedByEntry, string? VoidReason, DateTime? VoidedAt, string? VoidedBy,
    string? ExportBatchNo, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<JournalLineResponse> Lines, string RowVersion,
    string? Reference = null, string? CancelReason = null);

public sealed record EntryLinkRef(string Id, string Number);

/// <summary>The document that posted an automatic entry (FR-097): its type, number and URL token for the link.</summary>
public sealed record SourceDocumentRef(string Module, string Type, string? Id, string? Number);

/// <summary>
/// An entry line. A taxed line (DT-255) has its <paramref name="TaxCode"/>, rate, tax amount and tax account (the amount is before tax; gross =
/// amount + tax); an automatic tax line has <paramref name="TaxForLineNo"/> = the line whose tax it carries, and the code and rate it used.
/// </summary>
public sealed record JournalLineResponse(
    int LineNo, AccountRef Account, decimal Debit, decimal Credit, Ref? CostCentre, PartyResponse? Party, string? Note,
    string? Reference = null, TaxCodeRef? TaxCode = null, decimal? TaxRate = null, decimal? TaxAmount = null, AccountRef? TaxAccount = null,
    int? TaxForLineNo = null);

/// <summary>Printable journal voucher data (FR-103): lines, totals, amount in words (BR-024) and signature captions.</summary>
public sealed record JournalVoucherResponse(
    string CompanyName, string? CompanyNameEn, string Number, DateOnly EntryDate, string Description, string Status,
    IReadOnlyList<JournalLineResponse> Lines, decimal TotalDebit, decimal TotalCredit, string CurrencyCode,
    string AmountInWordsAr, string AmountInWordsEn, IReadOnlyList<string> Signatures, string? Reference = null);

// ---------------------------------------------------------------- pending postings (FR-101)

public sealed record PendingPostingResponse(
    string Id, string SourceModule, string SourceType, SourceDocumentRef? Source, string Reason, int Attempts, DateTime? LastAttemptAt,
    DateTime CreatedAt);

public sealed record RetryResult(int Posted, int StillPending);

// ---------------------------------------------------------------- journal export (FR-104)

public sealed record JournalExportRequest(string? PeriodFrom, string? PeriodTo, string? Format);

public sealed record JournalExportBatchResponse(
    string Id, string BatchNo, string PeriodFrom, string PeriodTo, string Format, int EntryCount, decimal TotalDebit, decimal TotalCredit,
    string ExportedBy, DateTime ExportedAt);


/// <summary>
/// The code rule of accounts, cost centres and tax codes: 1–30 letters, digits, dot, dash or underscore (VARCHAR(30)); with the contracts since
/// Stage 3 session 6c, so the services that check it need no accounting service.
/// </summary>
public static partial class AccountingCodeRule
{
    public static bool IsCode(string code) => CodePattern().IsMatch(code);

    [System.Text.RegularExpressions.GeneratedRegex("^[A-Za-z0-9._-]{1,30}$")]
    private static partial System.Text.RegularExpressions.Regex CodePattern();
}

/// <summary>The default signature labels of printed vouchers and of a new company (accountant, finance manager); with the contracts since Stage 3 session 6c.</summary>
public static class VoucherSignatures
{
    public static readonly IReadOnlyList<string> Default = ["المحاسب", "المدير المالي"];
}
