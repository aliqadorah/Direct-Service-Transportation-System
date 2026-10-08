// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.DashboardPins â€” Company-scoped.
/// </summary>
public class DashboardPin : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Dashboard owner.</summary>
    public long UserId { get; set; }
    /// <summary>Pinned report.</summary>
    public long SavedReportId { get; set; }
    /// <summary>Measure shown on the card.</summary>
    public string Measure { get; set; } = string.Empty;
    /// <summary>Value at which the card turns to warning.</summary>
    public decimal? WarningThreshold { get; set; }
    /// <summary>Value at which the card shows exceeded.</summary>
    public decimal? CriticalThreshold { get; set; }
    /// <summary>Display order (ascending).</summary>
    public int SortOrder { get; set; }
    public User User { get; set; } = null!;
    public SavedReport SavedReport { get; set; } = null!;
}
