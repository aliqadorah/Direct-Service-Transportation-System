// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Invoices â€” Company-scoped.
/// </summary>
[StoredName("TRN.Invoice")]
[TokenLabel("Invoice")]
public class Invoice : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Invoice number, assigned at approval.</summary>
    public string? Number { get; set; }
    /// <summary>Trip-based or manual.</summary>
    public string InvoiceType { get; set; } = string.Empty;
    /// <summary>Customer invoiced.</summary>
    public long CustomerId { get; set; }
    /// <summary>Invoice date.</summary>
    public DateOnly InvoiceDate { get; set; }
    /// <summary>Due date; default invoice date + customer payment terms.</summary>
    public DateOnly DueDate { get; set; }
    /// <summary>Σ trip revenue, or Σ quantity × unit price for manual lines.</summary>
    public decimal Subtotal { get; set; }
    /// <summary>Discount amount.</summary>
    public decimal Discount { get; set; }
    /// <summary>Extra fees added.</summary>
    public decimal ExtraFees { get; set; }
    /// <summary>What the fees are for.</summary>
    public string? ExtraFeesDescription { get; set; }
    /// <summary>Subtotal − Discount + ExtraFees.</summary>
    public decimal BaseAmount { get; set; }
    /// <summary>Tax rate % copied from the company at issue.</summary>
    public decimal TaxRate { get; set; }
    /// <summary>round(BaseAmount × TaxRate) (BR-023).</summary>
    public decimal Tax { get; set; }
    /// <summary>BaseAmount + Tax.</summary>
    public decimal Total { get; set; }
    /// <summary>Total of active payments/receipts against the document (maintained by the server).</summary>
    public decimal PaidAmount { get; set; }
    /// <summary>Total of active returns/credit notes against the document (maintained by the server).</summary>
    public decimal ReturnedAmount { get; set; }
    /// <summary>Total − active receipts − active credit notes.</summary>
    public decimal RemainingAmount { get; set; }
    /// <summary>Invoice status.</summary>
    public string Status { get; set; } = "Draft";
    /// <summary>Print layout used.</summary>
    public string LayoutType { get; set; } = "Claim";
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>User who approved.</summary>
    public long? ApprovedBy { get; set; }
    /// <summary>Approval time.</summary>
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Revenue entry posted at approval.</summary>
    public long? JournalEntryId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    public Customer Customer { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
    public User? VoidedByUser { get; set; }
    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}
