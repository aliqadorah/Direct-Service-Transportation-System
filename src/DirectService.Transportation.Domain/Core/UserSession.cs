// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.UserSessions â€” Tenant-scoped (TenantId NULL for provider staff).
/// </summary>
[StoredName("CORE.UserSession")]
[TokenLabel("UserSession")]
public class UserSession : AuditableEntity, INullableTenantScoped
{
    /// <summary>Owning tenant; NULL for provider staff.</summary>
    public long? TenantId { get; set; }
    /// <summary>Signed-in user.</summary>
    public long UserId { get; set; }
    /// <summary>Hash of the session/refresh token.</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Last request time.</summary>
    public DateTime LastSeenAt { get; set; }
    /// <summary>Absolute expiry.</summary>
    public DateTime ExpiresAt { get; set; }
    /// <summary>Client IP address (IPv4 or IPv6).</summary>
    public string? IpAddress { get; set; }
    /// <summary>Browser user-agent string.</summary>
    public string? UserAgent { get; set; }
    /// <summary>When revoked (sign-out, password or 2FA reset).</summary>
    public DateTime? RevokedAt { get; set; }
    public User User { get; set; } = null!;
}
