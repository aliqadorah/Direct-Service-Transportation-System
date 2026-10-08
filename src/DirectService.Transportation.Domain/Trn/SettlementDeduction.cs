// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_SettlementDeductions â€” Company-scoped.
/// </summary>
public class SettlementDeduction : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Settlement.</summary>
    public long SettlementId { get; set; }
    /// <summary>Kind of deduction.</summary>
    public string DeductionType { get; set; } = string.Empty;
    /// <summary>Deduction amount.</summary>
    public decimal Amount { get; set; }
    /// <summary>Explanation.</summary>
    public string? Note { get; set; }
    public Settlement Settlement { get; set; } = null!;
}
