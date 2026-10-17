// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Tenants â€” Platform-wide.
/// </summary>
public class Tenant : AuditableEntity
{
    /// <summary>Organisation name entered at sign-up.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Subscription lifecycle state.</summary>
    public string Status { get; set; } = "Trial";
    /// <summary>Current subscription plan.</summary>
    public long PlanId { get; set; }
    /// <summary>Billing cycle chosen by the tenant.</summary>
    public string BillingCycle { get; set; } = "Monthly";
    /// <summary>End of the free trial (UTC).</summary>
    public DateTime? TrialEndsAt { get; set; }
    /// <summary>End of the paid period (renewal date).</summary>
    public DateTime? CurrentPeriodEnd { get; set; }
    /// <summary>End of the grace period after a failed payment; after it the tenant is suspended.</summary>
    public DateTime? GraceEndsAt { get; set; }
    /// <summary>When the subscription was cancelled.</summary>
    public DateTime? CancelledAt { get; set; }
    /// <summary>Date data is kept until after cancellation, before the tenant is marked Deleted (soft).</summary>
    public DateTime? RetentionEndsAt { get; set; }
    /// <summary>Minimum password length for the tenant's users (FR-175).</summary>
    public byte PasswordMinLength { get; set; } = 8;
    /// <summary>Password must contain at least one letter (FR-175).</summary>
    public bool PasswordRequireLetter { get; set; } = true;
    /// <summary>Password must contain at least one digit (FR-175).</summary>
    public bool PasswordRequireDigit { get; set; } = true;
    /// <summary>Inactive sessions expire after this many minutes (FR-176).</summary>
    public short SessionIdleTimeoutMinutes { get; set; } = 30;
    /// <summary>Every user of the tenant must use two-step verification (revision 6; Owner only, default off).</summary>
    public bool RequireTwoFactor { get; set; }
    public Plan Plan { get; set; } = null!;
}
