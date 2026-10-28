namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_TripPartnerCoverage (view, owner findings 27 Oct 2026, DT-271) — how much of each active partner trip the partner's payments
/// cover (computed per day, then within the first day the payments do not cover). The partner's net payments (payments − repayments from the partner, active only — the DT-230 rule) are allocated to its active
/// trips oldest first (trip date, then receipt number); a trip is paid when nothing of its partner cost is left open. Read-only.
/// </summary>
[StoredName("TRN.TripPartnerCoverage")]
[TokenLabel("TripPartnerCoverage")]
public class TripPartnerCoverage : ITenantScoped, ICompanyScoped
{
    /// <summary>The partner trip.</summary>
    public long TripId { get; set; }
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The trip's partner.</summary>
    public long PartnerId { get; set; }
    /// <summary>The trip's partner cost.</summary>
    public decimal PartnerCost { get; set; }
    /// <summary>The partner's net payments (all dates).</summary>
    public decimal Paid { get; set; }
    /// <summary>Part of the partner cost not yet covered by the payments (0 = paid; equal to the cost = nothing paid).</summary>
    public decimal OpenAmount { get; set; }
    public Trip Trip { get; set; } = null!;
}
