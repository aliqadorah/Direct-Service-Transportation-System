using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Accounting;

// Identifiers are always URL tokens (SEC-22); amounts are in the currency's major unit with its decimals (BR-020).

/// <summary>An account shown by code and name (Arabic name; English when present).</summary>
public sealed record AccountRef(string Id, string Code, string NameAr, string? NameEn);

// ---------------------------------------------------------------- chart of accounts (FR-107 … FR-110)

/// <summary>One account of the chart. The list is flat, ordered by code; <see cref="Level"/> and <see cref="ParentId"/> build the tree.</summary>
public sealed record AccountResponse(
    string Id, string Code, string NameAr, string? NameEn, string AccountType, string? ParentId, string? ParentCode, int Level,
    bool IsPostable, bool HasChildren, decimal OpeningDebit, decimal OpeningCredit, bool IsControlAccount, string? ControlPartyType,
    string Status, string RowVersion);

/// <summary>An active postable account for pick lists, with its type and whether lines on it need a party (control account).</summary>
public sealed record AccountOption(AccountRef Account, string AccountType, bool IsControlAccount, string? ControlPartyType);

public sealed record AccountRequest(
    string? Code, string? NameAr, string? NameEn, string? AccountType, string? ParentId, bool IsPostable,
    bool IsControlAccount, string? ControlPartyType, string? RowVersion);

public sealed record MoveAccountRequest(string? ParentId, string? RowVersion);

/// <summary>Reason for a soft delete (BR-041).</summary>
public sealed record DeleteRequest(string? Reason);

public sealed record StatusChangeRequest(string? RowVersion);

/// <summary>Accounts, cash box and posting keys created from the built-in transport template (FR-107, D-10).</summary>
public sealed record ChartTemplateResult(int Accounts, int CashBoxes, int PostingKeys);

// ---------------------------------------------------------------- imports (FR-109, FR-114; ADR-13)

/// <summary>Preview of an import: every row with its problems; nothing is saved while any row has a problem (strict mode).</summary>
public sealed record ImportPreview(int TotalRows, int ValidRows, int RejectedRows, IReadOnlyList<ImportRowResult> Rows);

/// <summary>
/// One row of an import (1-based row number of the sheet, header = row 1) with its problems in the user's language; <paramref name="Details"/>
/// gives each problem's column and how to fix it (owner feedback 13 Oct 2026).
/// </summary>
public sealed record ImportRowResult(int Row, string? Key, IReadOnlyList<string> Problems, IReadOnlyList<ImportProblem>? Details = null);

/// <summary>One problem of an import row: the column of the file (null when it concerns the whole row), the problem and how to fix it.</summary>
public sealed record ImportProblem(string Code, string? Column, string Message, string Fix);

public sealed record ImportResult(string BatchId, int ImportedRows, int TotalRows);

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

/// <summary>Banner state shown in the app shell (FR-094, FR-110, FR-101).</summary>
public sealed record AccountingStatusResponse(
    bool PostingReady, IReadOnlyList<string> MissingPostingKeys, bool OpeningDifferenceWarning, decimal OpeningDifferenceBalance,
    int PendingPostings);

// ---------------------------------------------------------------- cost centres, cash boxes, bank accounts (FR-129, FR-131)

public sealed record CostCentreResponse(string Id, string Code, string Name, string Status, string RowVersion);

public sealed record CostCentreRequest(string? Code, string? Name, string? RowVersion);

public sealed record CashBoxResponse(string Id, string Name, AccountRef Account, string Status, string RowVersion);

public sealed record CashBoxRequest(string? Name, string? AccountId, string? RowVersion);

/// <summary>Active cash boxes and bank accounts (pick lists of cash documents).</summary>
public sealed record MoneyAccountOptions(IReadOnlyList<MasterData.MasterOption> CashBoxes, IReadOnlyList<MasterData.MasterOption> BankAccounts, DateOnly Today);

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

public sealed record JournalLineRequest(string? AccountId, decimal Debit, decimal Credit, string? CostCentreId, PartyRequest? Party, string? Note);

/// <summary>A manual entry; <paramref name="Reference"/> is the free-text reference (المرجع, up to 100 characters, revision 6).</summary>
public sealed record ManualEntryRequest(DateOnly? EntryDate, string? Description, IReadOnlyList<JournalLineRequest>? Lines, string? RowVersion,
    string? Reference = null);

/// <summary>A journal draft (revision 6): no number, not posted; posted later through <c>POST …/journal-entries/drafts/{id}/post</c>.</summary>
public sealed record JournalDraftResponse(
    string Id, DateOnly? EntryDate, string? Description, string? Reference, IReadOnlyList<JournalDraftLineResponse> Lines,
    decimal TotalDebit, decimal TotalCredit, DateTime CreatedAt, string? CreatedBy, string RowVersion);

/// <summary>A draft line as entered (account, cost centre and party may still be missing).</summary>
public sealed record JournalDraftLineResponse(AccountRef? Account, decimal Debit, decimal Credit, Ref? CostCentre, PartyResponse? Party, string? Note);

public sealed record VoidRequest(string? Reason, string? RowVersion);

/// <summary>An action on a record that only needs its row version (optimistic concurrency).</summary>
public sealed record RowVersionRequest(string? RowVersion);

/// <summary>
/// Journal list filters (FR-102, FR-115). Months are YYYY-MM; status: Posted, Reversed, Void, or Draft (revision 6: drafts only; with no
/// status, drafts are listed first). <paramref name="Search"/> matches the number, the description or the reference.
/// </summary>
public sealed record JournalQuery(
    string? PeriodFrom, string? PeriodTo, string? Number, string? AccountId, string? CostCentreId, string? PartyType, string? PartyId,
    bool? Manual, string? Status, string? SourceType, int Page = 1, int PageSize = 50, string? Search = null);

/// <summary>A row of the journal list; a draft (Status "Draft") has no number and its <paramref name="Id"/> is the draft's.</summary>
public sealed record JournalEntrySummary(
    string Id, string Number, DateOnly EntryDate, string PeriodMonth, string Description, string SourceModule, string? SourceType,
    bool IsManual, string Status, decimal TotalDebit, decimal TotalCredit, string? ReversesEntryNumber, string? ReversedByEntryNumber,
    string? Reference = null);

public sealed record JournalEntryResponse(
    string Id, string Number, DateOnly EntryDate, string PeriodMonth, string Description, string SourceModule, string? SourceType,
    SourceDocumentRef? Source, bool IsManual, string Status, decimal TotalDebit, decimal TotalCredit,
    EntryLinkRef? ReversesEntry, EntryLinkRef? ReversedByEntry, string? VoidReason, DateTime? VoidedAt, string? VoidedBy,
    string? ExportBatchNo, DateTime CreatedAt, string? CreatedBy, IReadOnlyList<JournalLineResponse> Lines, string RowVersion,
    string? Reference = null);

public sealed record EntryLinkRef(string Id, string Number);

/// <summary>The document that posted an automatic entry (FR-097): its type, number and URL token for the link.</summary>
public sealed record SourceDocumentRef(string Module, string Type, string? Id, string? Number);

public sealed record JournalLineResponse(
    int LineNo, AccountRef Account, decimal Debit, decimal Credit, Ref? CostCentre, PartyResponse? Party, string? Note);

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

/// <summary>A generated file (export, voucher data) returned to the client.</summary>
public sealed record GeneratedFile(string FileName, string ContentType, byte[] Content);
