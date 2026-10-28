// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.UserRecoveryCodes â€” Tenant-scoped (TenantId NULL for provider staff).
/// </summary>
[StoredName("CORE.UserRecoveryCode")]
[TokenLabel("UserRecoveryCode")]
public class UserRecoveryCode : AuditableEntity, INullableTenantScoped
{
    /// <summary>Owning tenant; NULL for provider staff.</summary>
    public long? TenantId { get; set; }
    /// <summary>Owner of the code.</summary>
    public long UserId { get; set; }
    /// <summary>Hash of the recovery code.</summary>
    public string CodeHash { get; set; } = string.Empty;
    /// <summary>When the code was used; a used code can never be used again.</summary>
    public DateTime? UsedAt { get; set; }
    public User User { get; set; } = null!;
}
