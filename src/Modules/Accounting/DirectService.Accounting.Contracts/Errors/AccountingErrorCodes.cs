namespace DirectService.Accounting.Contracts.Errors;

/// <summary>
/// Problem codes (API-06) of Accounting: the posting engine (journal lines and balance, line tax, posting keys, control parties, periods, pending
/// postings; Stage 3 session 4a) and the chart of accounts, opening balances, periods and journal templates (session 6). Each value is defined
/// here only (the DT-310 pattern): <c>ErrorCodes</c> of the Transportation contracts names these constants under the same names, and the Arabic
/// and English messages stay in its error catalogue. A value never changes — it is the "code" field clients receive.
/// </summary>
public static class AccountingErrorCodes
{
    public const string JournalUnbalanced = "journal.unbalanced";
    public const string JournalUnbalancedWithTax = "journal.unbalancedWithTax";
    public const string JournalLineAmount = "journal.lineAmount";
    public const string JournalMinLines = "journal.minLines";
    public const string JournalNotManual = "journal.notManual";
    public const string JournalNotPosted = "journal.notPosted";
    public const string JournalIsReversal = "journal.isReversal";
    public const string ControlPartyRequired = "journal.partyRequired";
    public const string ControlPartyMismatch = "journal.partyMismatch";
    public const string AccountNotPostable = "account.notPostable";
    public const string TaxAccountRequired = "journal.taxAccountRequired";
    public const string TaxAccountInvalid = "journal.taxAccountInvalid";
    public const string TaxCodeInactive = "journal.taxCodeInactive";
    public const string TaxCodeInUse = "taxCode.inUse";
    public const string TaxAccountControl = "account.taxControl";
    public const string TaxAccountNoGroup = "account.taxNoGroup";
    public const string PostingKeysMissing = "posting.keysMissing";
    public const string PeriodClosed = "period.closed";
    public const string PendingNoHandler = "pending.noHandler";

    // The chart of accounts, opening balances, periods and journal templates (Stage 3 session 6).
    public const string AccountActiveChildren = "account.activeChildren";
    public const string AccountCycle = "account.cycle";
    public const string AccountHasChildren = "account.hasChildren";
    public const string AccountLinkTaken = "account.linkTaken";
    public const string AccountMapped = "account.linked";
    public const string AccountNotAsset = "account.notAsset";
    public const string AccountParentInUse = "account.parentInUse";
    public const string AccountTypeMismatch = "account.typeMismatch";
    public const string ChartNotEmpty = "chart.notEmpty";
    public const string OpeningEmpty = "opening.empty";
    public const string OpeningUnbalanced = "opening.unbalanced";
    public const string PeriodAlreadyClosed = "period.alreadyClosed";
    public const string PeriodChecksPending = "period.checksPending";
    public const string PeriodNotClosed = "period.notClosed";
    public const string TemplateNameTaken = "template.nameTaken";

    // The accounting maintenance (FR-135; Stage 3 session 6c).
    public const string MaintenanceConfirmation = "maintenance.confirmation";
    public const string MaintenanceClosedPeriod = "maintenance.closedPeriod";
    public const string MaintenanceLinked = "maintenance.linked";
    public const string MaintenanceNothing = "maintenance.nothing";
    public const string MaintenanceChartInUse = "maintenance.chartInUse";
    public const string MaintenanceResetBlocked = "maintenance.resetBlocked";
    public const string MaintenanceRefused = "maintenance.refused";
}
