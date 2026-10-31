// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Accounting.Domain.Acc;

/// <summary>
/// acc.ApArDocumentLines â€” Company-scoped.
/// </summary>
[StoredName("ACC.ApArDocumentLine")]
[TokenLabel("ApArDocumentLine")]
public class ApArDocumentLine : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document.</summary>
    public long DocumentId { get; set; }
    /// <summary>Line text.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Expense, asset or revenue account.</summary>
    public long AccountId { get; set; }
    /// <summary>Line amount before tax.</summary>
    public decimal Amount { get; set; }
    /// <summary>Tax rate %.</summary>
    public decimal TaxRate { get; set; }
    /// <summary>Deduction (الخصم) of a supplier-bill line (revision 6): 0 ≤ deduction ≤ amount; line payable = amount + tax − deduction, tax on the full amount.</summary>
    public decimal Deduction { get; set; }
    /// <summary>Cost centre.</summary>
    public long? CostCentreId { get; set; }
    public ApArDocument Document { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public CostCentre? CostCentre { get; set; }
}
