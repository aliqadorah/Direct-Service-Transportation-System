// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Invitations â€” Tenant-scoped.
/// </summary>
public class Invitation : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Company the invitee will join.</summary>
    public long CompanyId { get; set; }
    /// <summary>Invitee email.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Role granted on acceptance.</summary>
    public long RoleId { get; set; }
    /// <summary>Hash of the invitation token in the link.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Expiry = sent time + 7 days.</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>When accepted.</summary>
    public DateTime? AcceptedAt { get; set; }
    /// <summary>User who sent the invitation.</summary>
    public long InvitedBy { get; set; }
    public Company Company { get; set; } = null!;
    public Role Role { get; set; } = null!;
    public User InvitedByUser { get; set; } = null!;
}
