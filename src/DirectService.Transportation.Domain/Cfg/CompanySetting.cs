// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.CompanySettings â€” Company-scoped.
/// </summary>
public class CompanySetting : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced key.</summary>
    public string SettingKey { get; set; } = string.Empty;
    /// <summary>Value as text (parsed by the module).</summary>
    public string SettingValue { get; set; } = string.Empty;
}
