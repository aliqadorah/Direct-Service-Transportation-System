// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Accounting.Domain.Core;

/// <summary>
/// core.MaintenanceLogs â€” Append-only log.
/// </summary>
[StoredName("CORE.MaintenanceLog")]
[TokenLabel("MaintenanceLog")]
public class MaintenanceLog : ITenantScoped, ICompanyScoped, IAppendOnly
{
    /// <summary>Internal key.</summary>
    public long Id { get; set; }
    /// <summary>Tenant where the action ran.</summary>
    public long TenantId { get; set; }
    /// <summary>Company where the action ran.</summary>
    public long CompanyId { get; set; }
    /// <summary>Administrator who performed the action.</summary>
    public long UserId { get; set; }
    /// <summary>Action, e.g. BulkSoftDelete, Restore.</summary>
    public string Action { get; set; } = string.Empty;
    /// <summary>Rows affected per entity type (JSON).</summary>
    public string RecordCounts { get; set; } = string.Empty;
    /// <summary>UTC time.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Tenant Tenant { get; set; } = null!;
    public Company Company { get; set; } = null!;
    public User User { get; set; } = null!;
}
