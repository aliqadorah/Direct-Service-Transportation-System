// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Platform.Domain;

/// <summary>
/// core.RolePermissions â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.RolePermission")]
[TokenLabel("RolePermission")]
public class RolePermission : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Role.</summary>
    public long RoleId { get; set; }
    /// <summary>Permission granted.</summary>
    public long PermissionId { get; set; }
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
