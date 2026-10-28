// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_CreditNoteLines â€” Company-scoped.
/// </summary>
[StoredName("TRN.CreditNoteLine")]
[TokenLabel("CreditNoteLine")]
public class CreditNoteLine : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Credit note.</summary>
    public long CreditNoteId { get; set; }
    /// <summary>Invoice line being returned.</summary>
    public long InvoiceLineId { get; set; }
    /// <summary>Trip returned (trip invoices).</summary>
    public long? TripId { get; set; }
    /// <summary>Quantity returned.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Revenue returned.</summary>
    public decimal Amount { get; set; }
    public CreditNote CreditNote { get; set; } = null!;
    public InvoiceLine InvoiceLine { get; set; } = null!;
    public Trip? Trip { get; set; }
}
