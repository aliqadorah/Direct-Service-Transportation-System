// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.SupportAccessGrants â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.SupportAccessGrant")]
[TokenLabel("SupportAccessGrant")]
public class SupportAccessGrant : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Provider staff user requesting access.</summary>
    public long RequestedBy { get; set; }
    /// <summary>Tenant Owner who approved or rejected.</summary>
    public long? ApprovedBy { get; set; }
    /// <summary>Why access is needed.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Access window start.</summary>
    public DateTime? StartsAt { get; set; }
    /// <summary>Access window end.</summary>
    public DateTime? EndsAt { get; set; }
    /// <summary>Request state.</summary>
    public string Status { get; set; } = "Requested";
    public User RequestedByUser { get; set; } = null!;
    public User? ApprovedByUser { get; set; }
}
