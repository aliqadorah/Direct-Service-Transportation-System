// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_MonthlySiteRates â€” Company-scoped.
/// </summary>
[StoredName("TRN.MonthlySiteRate")]
[TokenLabel("MonthlySiteRate")]
public class MonthlySiteRate : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Month the rate applies to.</summary>
    public string RateMonth { get; set; } = string.Empty;
    /// <summary>Loading site.</summary>
    public long LoadingSiteId { get; set; }
    /// <summary>Rate per ton for own-fleet trucks.</summary>
    public decimal? OwnFleetRate { get; set; }
    /// <summary>Rate per ton for external-fleet trucks.</summary>
    public decimal? ExternalFleetRate { get; set; }
    public LoadingSite LoadingSite { get; set; } = null!;
}
