// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.AuditLogs â€” Append-only log.
/// </summary>
[StoredName("CORE.AuditLog")]
[TokenLabel("AuditLog")]
public class AuditLog : INullableTenantScoped, IAppendOnly, IAuditRow
{
    /// <summary>Internal key.</summary>
    public long Id { get; set; }
    /// <summary>Tenant (NULL for platform-level actions).</summary>
    public long? TenantId { get; set; }
    /// <summary>Company (NULL for tenant/platform-level actions).</summary>
    public long? CompanyId { get; set; }
    /// <summary>Acting user (NULL for system jobs).</summary>
    public long? UserId { get; set; }
    /// <summary>UTC time of the action.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    /// <summary>Action performed.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Namespaced entity type, e.g. TRN.Trip.</summary>
    public string? EntityType { get; set; }
    /// <summary>Internal id of the affected row.</summary>
    public long? EntityId { get; set; }
    /// <summary>Values before the change (JSON).</summary>
    public string? OldValues { get; set; }
    /// <summary>Values after the change (JSON).</summary>
    public string? NewValues { get; set; }
    /// <summary>Client IP address (IPv4 or IPv6).</summary>
    public string? IpAddress { get; set; }
    public Tenant? Tenant { get; set; }
    public Company? Company { get; set; }
    public User? User { get; set; }
}
