// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.ApArReturns â€” Company-scoped.
/// </summary>
public class ApArReturn : AuditableEntity, ITenantScoped, ICompanyScoped, ICashDocument
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Document returned against.</summary>
    public long DocumentId { get; set; }
    /// <summary>Date.</summary>
    public DateOnly ReturnDate { get; set; }
    /// <summary>Amount before tax.</summary>
    public decimal Amount { get; set; }
    /// <summary>Sales tax amount.</summary>
    public decimal Tax { get; set; }
    /// <summary>Amount + Tax.</summary>
    public decimal Total { get; set; }
    /// <summary>Reason text.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Status.</summary>
    public string Status { get; set; } = "Posted";
    /// <summary>The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry.</summary>
    public long? JournalEntryId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    public ApArDocument Document { get; set; } = null!;
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
}
