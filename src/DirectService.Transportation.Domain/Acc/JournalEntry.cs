// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.JournalEntries â€” Company-scoped.
/// </summary>
public class JournalEntry : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Entry date = source document date (BR-034).</summary>
    public DateOnly EntryDate { get; set; }
    /// <summary>Period of EntryDate (YYYY-MM).</summary>
    public string PeriodMonth { get; set; } = string.Empty;
    /// <summary>Entry narration.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Free-text reference (المرجع) next to the narration, up to 100 characters (revision 6; manual entries).</summary>
    public string? Reference { get; set; }
    /// <summary>Module that owns the source document.</summary>
    public string SourceModule { get; set; } = string.Empty;
    /// <summary>Type of source document within the module.</summary>
    public string? SourceType { get; set; }
    /// <summary>Internal id of the source document (no FK — the source may be in a project table).</summary>
    public long? SourceId { get; set; }
    /// <summary>1 = entered by a user; 0 = automatic.</summary>
    public bool IsManual { get; set; }
    /// <summary>Entry status.</summary>
    public string Status { get; set; } = "Posted";
    /// <summary>Entry this one reverses.</summary>
    public long? ReversesEntryId { get; set; }
    /// <summary>Entry that reversed this one.</summary>
    public long? ReversedByEntryId { get; set; }
    /// <summary>Σ line debits.</summary>
    public decimal TotalDebit { get; set; }
    /// <summary>Σ line credits.</summary>
    public decimal TotalCredit { get; set; }
    /// <summary>Export batch that included the entry.</summary>
    public long? ExportBatchId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    public JournalEntry? ReversesEntry { get; set; }
    public JournalEntry? ReversedByEntry { get; set; }
    public JournalExportBatch? ExportBatch { get; set; }
    public User? VoidedByUser { get; set; }
    public ICollection<EntryLine> Lines { get; set; } = new List<EntryLine>();
}
