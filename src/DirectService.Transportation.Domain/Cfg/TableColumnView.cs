// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.TableColumnViews â€” Company-scoped.
/// </summary>
[StoredName("CFG.TableColumnView")]
[TokenLabel("TableColumnView")]
public class TableColumnView : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Owner; NULL = company default.</summary>
    public long? UserId { get; set; }
    /// <summary>Namespaced list key.</summary>
    public string ViewKey { get; set; } = string.Empty;
    /// <summary>Visible columns, order and widths (JSON).</summary>
    public string Columns { get; set; } = string.Empty;
    public User? User { get; set; }
}
