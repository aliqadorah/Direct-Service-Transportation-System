// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Plans â€” Platform-wide.
/// </summary>
[StoredName("CORE.Plan")]
[TokenLabel("Plan")]
public class Plan : AuditableEntity
{
    /// <summary>Short unique plan code, e.g. BASIC, PRO.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Plan name in Arabic.</summary>
    public string NameAr { get; set; } = string.Empty;
    /// <summary>Plan name in English.</summary>
    public string NameEn { get; set; } = string.Empty;
    /// <summary>Price per billing cycle, excluding tax.</summary>
    public decimal Price { get; set; }
    /// <summary>Billing cycle of the price.</summary>
    public string BillingCycle { get; set; } = "Monthly";
    /// <summary>Maximum companies a tenant may create.</summary>
    public int MaxCompanies { get; set; }
    /// <summary>Maximum active (non-archived) users.</summary>
    public int MaxActiveUsers { get; set; }
    /// <summary>Maximum trips saved per calendar month.</summary>
    public int MaxTripsPerMonth { get; set; }
    /// <summary>Storage allowance for attachments and exports, in MB.</summary>
    public int MaxStorageMb { get; set; }
    /// <summary>Whether the plan can be chosen by new or upgrading tenants.</summary>
    public bool IsActive { get; set; } = true;
}
