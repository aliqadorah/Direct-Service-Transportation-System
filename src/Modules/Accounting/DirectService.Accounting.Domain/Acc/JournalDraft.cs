namespace DirectService.Accounting.Domain.Acc;

/// <summary>
/// acc.JournalDrafts — Company-scoped (revision 6, owner change request 15 Oct 2026). A manual journal entry saved as a draft: no JV number,
/// not posted, never part of any balance or report. Posting validates it in full, posts it through the posting engine (number taken then,
/// period lock checked then) and removes the draft in the same transaction.
/// </summary>
[StoredName("ACC.JournalDraft")]
[TokenLabel("JournalDraft")]
public class JournalDraft : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Intended entry date (may be empty while drafting).</summary>
    public DateOnly? EntryDate { get; set; }
    /// <summary>Entry narration (البيان).</summary>
    public string? Description { get; set; }
    /// <summary>Free-text reference (المرجع), up to 100 characters.</summary>
    public string? Reference { get; set; }
    /// <summary>The lines as entered (JSON array: account id, debit, credit, cost centre id, party type + id, note; internal ids).</summary>
    public string Lines { get; set; } = "[]";
    /// <summary>Σ line debits as entered (list display only).</summary>
    public decimal TotalDebit { get; set; }
    /// <summary>Σ line credits as entered (list display only).</summary>
    public decimal TotalCredit { get; set; }
}
