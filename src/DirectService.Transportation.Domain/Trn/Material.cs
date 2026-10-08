// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Materials â€” Company-scoped.
/// </summary>
public class Material : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Material name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Unit of measure.</summary>
    public string Unit { get; set; } = "ton";
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
}
