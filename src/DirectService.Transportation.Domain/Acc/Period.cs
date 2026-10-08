// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.Periods â€” Company-scoped.
/// </summary>
public class Period : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Calendar month.</summary>
    public string PeriodMonth { get; set; } = string.Empty;
    /// <summary>Open or closed.</summary>
    public string Status { get; set; } = "Open";
    /// <summary>User who closed/reopened.</summary>
    public long? ClosedBy { get; set; }
    /// <summary>When closed.</summary>
    public DateTime? ClosedAt { get; set; }
    /// <summary>Reason for closing or reopening.</summary>
    public string? Reason { get; set; }
    public User? ClosedByUser { get; set; }
}
