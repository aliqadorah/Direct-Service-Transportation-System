// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_LoadingSites â€” Company-scoped.
/// </summary>
[StoredName("TRN.LoadingSite")]
[TokenLabel("LoadingSite")]
public class LoadingSite : AuditableEntity, ITenantScoped, ICompanyScoped, ISite
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Site name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Area / region of the site.</summary>
    public string? Area { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
}
