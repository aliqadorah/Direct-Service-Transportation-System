// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Users â€” Tenant-scoped (TenantId NULL for provider staff).
/// </summary>
public class User : AuditableEntity, INullableTenantScoped
{
    /// <summary>Owning tenant; NULL for provider staff.</summary>
    public long? TenantId { get; set; }
    /// <summary>User's full name.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>Sign-in email.</summary>
    public string Email { get; set; } = string.Empty;
    /// <summary>Upper-cased email used for look-ups (Identity).</summary>
    public string NormalizedEmail { get; set; } = string.Empty;
    /// <summary>When the user verified their email address (sign-up link or accepted invitation).</summary>
    public DateTime? EmailConfirmedAt { get; set; }
    /// <summary>Salted password hash (Identity). Never shown or emailed.</summary>
    public string PasswordHash { get; set; } = string.Empty;
    /// <summary>Identity security stamp; changed on password/2FA change to invalidate sessions.</summary>
    public string SecurityStamp { get; set; } = string.Empty;
    /// <summary>1 for provider staff (no tenant).</summary>
    public bool IsPlatformStaff { get; set; }
    /// <summary>Encrypted authenticator-app key.</summary>
    public byte[]? TwoFactorSecret { get; set; }
    /// <summary>When mandatory 2FA was set up (first sign-in).</summary>
    public DateTime? TwoFactorEnabledAt { get; set; }
    /// <summary>Interface language.</summary>
    public string LanguageCode { get; set; } = "ar";
    /// <summary>Company opened after sign-in.</summary>
    public long? DefaultCompanyId { get; set; }
    /// <summary>Preferred date format.</summary>
    public string DateFormat { get; set; } = "dd/MM/yyyy";
    /// <summary>Account state.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>Consecutive failed sign-ins.</summary>
    public int FailedSignInCount { get; set; }
    /// <summary>Sign-in blocked until this UTC time.</summary>
    public DateTime? LockedUntil { get; set; }
    /// <summary>Last successful sign-in.</summary>
    public DateTime? LastSignInAt { get; set; }
    public Company? DefaultCompany { get; set; }
}
