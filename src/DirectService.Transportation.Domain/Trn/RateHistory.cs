// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_RateHistory â€” Company-scoped.
/// </summary>
[StoredName("TRN.RateHistory")]
[TokenLabel("RateHistory")]
public class RateHistory : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Which table changed.</summary>
    public string SourceType { get; set; } = string.Empty;
    /// <summary>Id of the changed TRN_Tariffs or TRN_MonthlySiteRates row (no FK: polymorphic).</summary>
    public long SourceId { get; set; }
    /// <summary>Values before the change (JSON).</summary>
    public string? OldValues { get; set; }
    /// <summary>Values after the change (JSON).</summary>
    public string NewValues { get; set; } = string.Empty;
    /// <summary>User who made the change.</summary>
    public long ChangedBy { get; set; }
    /// <summary>UTC time of the change.</summary>
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public User ChangedByUser { get; set; } = null!;
}
