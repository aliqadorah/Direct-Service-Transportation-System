// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.CostCentres â€” Company-scoped.
/// </summary>
[StoredName("ACC.CostCentre")]
[TokenLabel("CostCentre")]
public class CostCentre : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Cost-centre code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
}
