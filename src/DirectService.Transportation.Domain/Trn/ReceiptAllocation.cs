namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_ReceiptAllocations — Company-scoped (revision 6, owner change request 15 Oct 2026). The part of a receipt applied to one of the
/// customer's open invoices; a receipt may be applied to several invoices, and what is not allocated stays on account. Existing receipts
/// against one invoice were migrated as one allocation of their full amount.
/// </summary>
[StoredName("TRN.ReceiptAllocation")]
[TokenLabel("ReceiptAllocation")]
public class ReceiptAllocation : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The receipt.</summary>
    public long ReceiptId { get; set; }
    /// <summary>The invoice the amount is applied to (same customer, approved, open when allocated).</summary>
    public long InvoiceId { get; set; }
    /// <summary>Amount applied (&gt; 0, ≤ the invoice's open balance when allocated).</summary>
    public decimal Amount { get; set; }
    public Receipt Receipt { get; set; } = null!;
    public Invoice Invoice { get; set; } = null!;
}
