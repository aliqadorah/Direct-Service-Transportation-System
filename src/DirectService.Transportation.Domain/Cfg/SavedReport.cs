// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.SavedReports â€” Company-scoped.
/// </summary>
[StoredName("CFG.SavedReport")]
[TokenLabel("SavedReport")]
public class SavedReport : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Report owner.</summary>
    public long OwnerUserId { get; set; }
    /// <summary>Report name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Namespaced data source.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Columns, filters, grouping and sorting (JSON).</summary>
    public string Definition { get; set; } = string.Empty;
    /// <summary>Visible to other users with access to the source.</summary>
    public bool IsShared { get; set; }
    public User OwnerUser { get; set; } = null!;
}
