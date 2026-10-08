// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.StandardFieldConfigs â€” Company-scoped.
/// </summary>
public class StandardFieldConfig : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced entity.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Built-in field key.</summary>
    public string FieldKey { get; set; } = string.Empty;
    /// <summary>Replacement label; NULL = default.</summary>
    public string? Label { get; set; }
    /// <summary>Hide the field.</summary>
    public bool IsHidden { get; set; }
    /// <summary>Make required.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Display order (ascending).</summary>
    public int SortOrder { get; set; }
}
