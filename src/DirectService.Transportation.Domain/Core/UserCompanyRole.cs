// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.UserCompanyRoles â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.UserCompanyRole")]
[TokenLabel("UserCompanyRole")]
public class UserCompanyRole : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>User.</summary>
    public long UserId { get; set; }
    /// <summary>Company the role applies to.</summary>
    public long CompanyId { get; set; }
    /// <summary>Role held.</summary>
    public long RoleId { get; set; }
    public User User { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
