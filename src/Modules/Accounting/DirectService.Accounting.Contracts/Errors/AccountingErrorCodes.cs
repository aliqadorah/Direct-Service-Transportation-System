namespace DirectService.Accounting.Contracts.Errors;

/// <summary>
/// Problem codes (API-06) thrown by the posting engine: journal lines and balance, line tax, posting keys, control parties, periods, pending
/// postings (Stage 3 session 4a, the DT-310 pattern). Each value is defined here only: <c>ErrorCodes</c> of the Transportation contracts names
/// these constants under the same names, and the Arabic and English messages stay in its error catalogue. A value never changes — it is the
/// "code" field clients receive. More accounting codes join with their services (session 6).
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
}
