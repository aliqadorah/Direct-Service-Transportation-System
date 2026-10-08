// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_InvoiceLines â€” Company-scoped.
/// </summary>
public class InvoiceLine : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Invoice.</summary>
    public long InvoiceId { get; set; }
    /// <summary>Trip billed by this line (trip invoices).</summary>
    public long? TripId { get; set; }
    /// <summary>Line text (manual lines).</summary>
    public string? Description { get; set; }
    /// <summary>Quantity (tons for trips).</summary>
    public decimal Quantity { get; set; }
    /// <summary>Price per unit.</summary>
    public decimal UnitPrice { get; set; }
    /// <summary>Quantity × UnitPrice, rounded half-up.</summary>
    public decimal Amount { get; set; }
    /// <summary>Display order (ascending).</summary>
    public int SortOrder { get; set; }
    public Invoice Invoice { get; set; } = null!;
    public Trip? Trip { get; set; }
}
