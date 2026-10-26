using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Ledger;

// ------------------------------------------------------------------ general ledger and account statement (FR-116, FR-120, FR-121)

/// <summary>
/// General ledger / account statement filters: an account (a parent account covers its descendants), the period (default: this month up to
/// today), an optional cost centre and an optional party (type Customer / Driver / Partner / ApArParty + its id).
/// </summary>
/// <summary>Ledger / statement filters; <paramref name="Reference"/> (DT-254) keeps the lines whose line reference or entry reference contains it.</summary>
public sealed record LedgerQuery(string? Account, DateOnly? From, DateOnly? To, string? CostCentre, string? PartyType, string? Party, int Page = 1, int PageSize = 200,
    string? Reference = null);

/// <summary>One movement with its running balance (debit − credit); drill-down to the entry and to its source document.</summary>
/// <summary>A ledger / statement line; <paramref name="Reference"/> is the line reference (مرجع السطر, DT-254; the entry's reference when the line has none).</summary>
public sealed record LedgerLine(
    DateOnly Date, EntryLinkRef Entry, string Description, SourceDocumentRef? Source, AccountRef Account, PartyResponse? Party, Ref? CostCentre, string? Note,
    decimal Debit, decimal Credit, decimal Balance, string? Reference = null);

/// <summary>Opening, movements (paged; running balance continues across pages), totals for the whole period and closing balance; balances are debit − credit.</summary>
public sealed record LedgerReport(
    AccountRef Account, string AccountType, PartyResponse? Party, Ref? CostCentre, DateOnly From, DateOnly To, decimal Opening, decimal TotalDebit, decimal TotalCredit,
    decimal Closing, PagedResult<LedgerLine> Lines);

// ------------------------------------------------------------------ account statement of several accounts (revision 6)

/// <summary>
/// Account statement of several accounts (revision 6, owner change request 15 Oct 2026): <paramref name="Accounts"/> = comma-separated
/// account ids (a group covers its sub-accounts; selections may not overlap), or a code range <paramref name="CodeFrom"/>–<paramref name="CodeTo"/>
/// (inclusive, accounts without sub-accounts), or <paramref name="AllAccounts"/>. Accounts with no opening and no movement are left out of a
/// range or of "all accounts" (<paramref name="HideEmpty"/>, default on).
/// </summary>
public sealed record AccountStatementsQuery(
    string? Accounts, string? CodeFrom, string? CodeTo, bool? AllAccounts, DateOnly? From, DateOnly? To, string? CostCentre, string? PartyType, string? Party,
    bool? HideEmpty = null, string? Reference = null);

/// <summary>One account of the statement: opening, every movement with its running balance, the subtotal (totals and closing).</summary>
public sealed record AccountStatementSection(
    AccountRef Account, string AccountType, decimal Opening, decimal TotalDebit, decimal TotalCredit, decimal Closing, IReadOnlyList<LedgerLine> Lines);

/// <summary>The sections in chart order and the grand total over all of them.</summary>
public sealed record AccountStatementsReport(
    DateOnly From, DateOnly To, PartyResponse? Party, Ref? CostCentre, IReadOnlyList<AccountStatementSection> Sections,
    decimal Opening, decimal TotalDebit, decimal TotalCredit, decimal Closing);

// ------------------------------------------------------------------ trial balance (FR-117)

/// <summary>Trial balance for a period at a tree level (1 = top accounts; default the deepest level); optionally hide rows with nothing in them.</summary>
/// <summary>Trial balance filters; <paramref name="Tree"/> (revision 6) adds every parent above the rows of the chosen level, in chart order.</summary>
public sealed record TrialBalanceQuery(DateOnly? From, DateOnly? To, int? Level, bool? HideZero, bool? Tree = null);

/// <summary>
/// An account at the chosen level (leaves above it shown as themselves): opening and closing as net debit or credit, movements gross. In the
/// tree (revision 6) a parent row (<paramref name="IsGroup"/>) is the sum of its children column by column; <paramref name="ParentId"/> links a
/// row to the parent row above it; the totals are those of the rows of the chosen level, so they balance as before.
/// </summary>
public sealed record TrialBalanceRow(
    AccountRef Account, string AccountType, int Level, bool IsLeaf, decimal OpeningDebit, decimal OpeningCredit, decimal MovementDebit, decimal MovementCredit,
    decimal ClosingDebit, decimal ClosingCredit, string? ParentId = null, bool IsGroup = false);

public sealed record TrialBalanceTotals(decimal OpeningDebit, decimal OpeningCredit, decimal MovementDebit, decimal MovementCredit, decimal ClosingDebit, decimal ClosingCredit);

/// <summary>The rows, totals, whether every pair of columns balances (a mismatch is highlighted), and the opening-balance difference still to clear (FR-110).</summary>
public sealed record TrialBalanceReport(
    DateOnly From, DateOnly To, int Level, int MaxLevel, IReadOnlyList<TrialBalanceRow> Rows, TrialBalanceTotals Totals, bool Balanced, decimal? OpeningDifference);

// ------------------------------------------------------------------ profit and loss, balance sheet (FR-118, FR-119)

/// <summary>A line of a statement in tree order: group accounts carry the total of their descendants.</summary>
public sealed record StatementRow(AccountRef Account, string AccountType, int Level, bool IsLeaf, decimal Amount, decimal? CompareAmount);

/// <summary>
/// Profit and loss for a period, optionally for one cost centre (a token, or <c>none</c> = lines without a cost centre) and compared with a
/// second period. <paramref name="ColumnsBy"/> (DT-266): <c>none</c> (one amount), <c>costCentre</c>, <c>month</c>, <c>quarter</c> or
/// <c>year</c>, always with a total; <paramref name="HideZero"/> (default true) leaves out accounts without amounts.
/// </summary>
public sealed record ProfitLossQuery(DateOnly? From, DateOnly? To, string? CostCentre, DateOnly? CompareFrom, DateOnly? CompareTo, string? ColumnsBy = null,
    bool? HideZero = null);

/// <summary>
/// Profit and loss. <see cref="Revenue"/> / <see cref="Expenses"/> and the totals are the statement by account type (as before DT-266);
/// <see cref="Sections"/> is the statement by section — sales, cost of sales, gross profit, operating expenses, other income, other
/// expenses, net profit — with one amount per column of <see cref="Columns"/> and a total.
/// </summary>
public sealed record ProfitLossReport(
    DateOnly From, DateOnly To, Ref? CostCentre, DateOnly? CompareFrom, DateOnly? CompareTo, IReadOnlyList<StatementRow> Revenue, IReadOnlyList<StatementRow> Expenses,
    decimal TotalRevenue, decimal TotalExpenses, decimal NetProfit, decimal? CompareTotalRevenue, decimal? CompareTotalExpenses, decimal? CompareNetProfit,
    string ColumnsBy = "none", IReadOnlyList<ProfitLossColumn>? Columns = null, IReadOnlyList<ProfitLossSection>? Sections = null,
    ProfitLossFigure? GrossProfit = null, ProfitLossFigure? Net = null);

/// <summary>
/// A column of the profit and loss (DT-266): <paramref name="Key"/> is the period (<c>2026-01</c>, <c>2026-Q1</c>, <c>2026</c>), the cost
/// centre's token or <c>none</c> (no cost centre), or <c>total</c>; <paramref name="From"/> / <paramref name="To"/> the dates it covers and
/// <paramref name="CostCentre"/> the cost centre it is for (token or <c>none</c>) — what an amount's account statement is filtered by.
/// </summary>
public sealed record ProfitLossColumn(string Key, string? Name, DateOnly From, DateOnly To, string? CostCentre);

/// <summary>An account of a section: one amount per column, the total, and the comparison period's amount (columns "none" only).</summary>
public sealed record ProfitLossRow(AccountRef Account, string AccountType, int Level, bool IsLeaf, string? ParentId, IReadOnlyList<decimal> Amounts, decimal Total,
    decimal? CompareAmount = null);

/// <summary>A section (<see cref="ProfitLossGroups"/>): its accounts in tree order (groups total their sub-accounts of the section) and its total.</summary>
public sealed record ProfitLossSection(string Group, IReadOnlyList<ProfitLossRow> Rows, ProfitLossFigure Total);

/// <summary>A total line: one amount per column, the total, and the comparison period's amount.</summary>
public sealed record ProfitLossFigure(IReadOnlyList<decimal> Amounts, decimal Total, decimal? Compare = null);

public sealed record BalanceSheetQuery(DateOnly? AsOf);

/// <summary>
/// Balance sheet as of a date: assets, liabilities and equity by the tree; within equity the result of the current year (1 January to the
/// date) and the results of earlier years not yet transferred; the opening-balance difference still to clear (FR-110).
/// </summary>
public sealed record BalanceSheetReport(
    DateOnly AsOf, IReadOnlyList<StatementRow> Assets, IReadOnlyList<StatementRow> Liabilities, IReadOnlyList<StatementRow> Equity, decimal CurrentYearResult,
    decimal PriorYearsResult, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, decimal TotalLiabilitiesAndEquity, bool Balanced, decimal? OpeningDifference);

// ------------------------------------------------------------------ cost centres, cash and banks (FR-130, FR-131)

public sealed record PeriodQuery(DateOnly? From, DateOnly? To);

/// <summary>Revenues (credit − debit) and expenses (debit − credit) per cost centre; the row without a cost centre has no reference.</summary>
public sealed record CostCentreRow(Ref? CostCentre, decimal Revenue, decimal Expenses, decimal Net);

public sealed record CostCentreReport(DateOnly From, DateOnly To, IReadOnlyList<CostCentreRow> Rows, CostCentreRow Totals);

public sealed record CashBankStatementQuery(string? CashBox, string? BankAccount, DateOnly? From, DateOnly? To, int Page = 1, int PageSize = 200);

/// <summary>Cash box or bank statement: opening, receipts (debits), payments (credits), running and closing balance.</summary>
public sealed record CashBankStatement(
    MoneyAccountRef Account, AccountRef LedgerAccount, DateOnly From, DateOnly To, decimal Opening, decimal Receipts, decimal Payments, decimal Closing,
    PagedResult<LedgerLine> Lines);

/// <summary>Balance of one cash box or bank account as of a date (cards and the banks list).</summary>
public sealed record CashBankBalance(MoneyAccountRef Account, AccountRef LedgerAccount, string Status, decimal Balance);

// ------------------------------------------------------------------ sub-ledger reconciliation (FR-105, RPT-23)

public sealed record ReconciliationQuery(DateOnly? AsOf);

/// <summary>A control-account line that no party balance explains: without a party, or with a party of another type.</summary>
public sealed record UnmatchedLine(DateOnly Date, EntryLinkRef Entry, string Description, SourceDocumentRef? Source, decimal Debit, decimal Credit, string Problem);

/// <summary>
/// An entry behind a documents difference (DT-262): a manual journal entry (<paramref name="Kind"/> "Manual") or the opening entry ("Opening") with
/// lines on the control account, and its net amount there (debit − credit). Voided / reversed entries and their reversals cancel out and are not listed.
/// </summary>
public sealed record DocumentGapEntry(DateOnly Date, EntryLinkRef Entry, string Description, string Kind, decimal Amount);

/// <summary>
/// Per control account: ledger balance, sum of the party balances on its lines, difference (must be 0.000), number of parties, unmatched lines,
/// the balance from documents and its difference to the ledger. DT-262: every control account has documents figures — rebuilt from the
/// documents where they can rebuild it (<paramref name="DocumentsRebuilt"/>), otherwise the amount posted by documents (automatic entries).
/// The documents difference = <paramref name="ManualAmount"/> (manual journal entries) + <paramref name="OpeningAmount"/> (the opening entry,
/// not a document) + <paramref name="Unexplained"/>; <paramref name="GapEntries"/> lists those entries (at most 200).
/// </summary>
public sealed record ReconciliationRow(
    AccountRef Account, string PartyType, decimal LedgerBalance, decimal PartyBalances, decimal Difference, int Parties, IReadOnlyList<UnmatchedLine> Unmatched,
    decimal? DocumentBalance, decimal? DocumentDifference, bool DocumentsRebuilt = true, decimal ManualAmount = 0, decimal OpeningAmount = 0, decimal Unexplained = 0,
    IReadOnlyList<DocumentGapEntry>? GapEntries = null);

/// <summary>
/// The report. DT-262: <paramref name="Reconciled"/> = every control account equals its party balances; <paramref name="Level"/> = "Ok" (no
/// difference at all), "Documents" (control accounts match the parties; some amounts come from manual entries or the opening entry, or differ
/// from the documents) or "Differences" (a control account differs from its party balances).
/// </summary>
public sealed record ReconciliationReport(DateOnly AsOf, IReadOnlyList<ReconciliationRow> Rows, bool Reconciled, int PendingPostings, string Level = ReconciliationLevels.Ok);

public static class ReconciliationLevels
{
    public const string Ok = "Ok";
    public const string Documents = "Documents";
    public const string Differences = "Differences";
}
