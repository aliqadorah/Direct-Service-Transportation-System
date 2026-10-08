// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_CreditNotes â€” Company-scoped.
/// </summary>
public class CreditNote : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Credit note number.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Invoice being credited.</summary>
    public long InvoiceId { get; set; }
    /// <summary>Customer (same as the invoice).</summary>
    public long CustomerId { get; set; }
    /// <summary>Credit note date.</summary>
    public DateOnly CreditDate { get; set; }
    /// <summary>Why the credit note was issued.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Revenue of the returned trips/quantities.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>Proportional share of the invoice discount.</summary>
    public decimal DiscountShare { get; set; }
    /// <summary>Proportional share of the invoice extra fees.</summary>
    public decimal FeesShare { get; set; }
    /// <summary>Tax at the invoice's rate.</summary>
    public decimal Tax { get; set; }
    /// <summary>Subtotal − DiscountShare + FeesShare + Tax.</summary>
    public decimal Total { get; set; }
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
    public Invoice Invoice { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
    public ICollection<CreditNoteLine> Lines { get; set; } = new List<CreditNoteLine>();
}
