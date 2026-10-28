// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_OwnershipTypes â€” Company-scoped.
/// </summary>
[StoredName("TRN.OwnershipType")]
[TokenLabel("OwnershipType")]
public class OwnershipType : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Type name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Own or external fleet.</summary>
    public string FleetClass { get; set; } = string.Empty;
    /// <summary>1 for the built-in "Company" type (FleetClass Own); cannot be renamed or deleted.</summary>
    public bool IsBuiltIn { get; set; }
}
