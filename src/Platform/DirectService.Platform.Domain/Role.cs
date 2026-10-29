// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Platform.Domain;

/// <summary>
/// core.Roles â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.Role")]
[TokenLabel("Role")]
public class Role : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Role name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>1 = built-in preset role (name and permissions fixed; cannot be deleted).</summary>
    public bool IsPreset { get; set; }
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
