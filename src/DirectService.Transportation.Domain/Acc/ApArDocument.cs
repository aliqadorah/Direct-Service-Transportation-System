// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.ApArDocuments â€” Company-scoped.
/// </summary>
public class ApArDocument : AuditableEntity, ITenantScoped, ICompanyScoped, ICashDocument
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Kind of document.</summary>
    public string DocumentKind { get; set; } = string.Empty;
    /// <summary>Supplier or customer.</summary>
    public long PartyId { get; set; }
    /// <summary>Document date.</summary>
    public DateOnly DocumentDate { get; set; }
    /// <summary>Date payment is due.</summary>
    public DateOnly DueDate { get; set; }
    /// <summary>External reference, e.g. bank transfer or cheque number.</summary>
    public string? Reference { get; set; }
    /// <summary>Sum of line amounts before discount, fees and tax.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>Sales tax amount.</summary>
    public decimal Tax { get; set; }
    /// <summary>Total amount including tax.</summary>
    public decimal Total { get; set; }
    /// <summary>Total of active payments/receipts against the document (maintained by the server).</summary>
    public decimal PaidAmount { get; set; }
    /// <summary>Total of active returns/credit notes against the document (maintained by the server).</summary>
    public decimal ReturnedAmount { get; set; }
    /// <summary>Total − PaidAmount − ReturnedAmount (maintained by the server).</summary>
    public decimal RemainingAmount { get; set; }
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
    public ApArParty Party { get; set; } = null!;
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
    public ICollection<ApArDocumentLine> Lines { get; set; } = new List<ApArDocumentLine>();
}
