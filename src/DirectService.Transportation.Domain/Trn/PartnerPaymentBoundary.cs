namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_PartnerPaymentBoundary (view, owner findings 27 Oct 2026, DT-271) — one row per partner with active trips: the partner's net
/// payments (payments − repayments, active only — the DT-230 rule) laid over its trips oldest first. Trips before <see cref="BoundaryDate"/>
/// are paid, trips after it open; on that day the trips are covered in receipt-number order from what is left after <see cref="CostBefore"/>.
/// No boundary date: every trip of the partner is paid. Read-only.
/// </summary>
public class PartnerPaymentBoundary : ITenantScoped, ICompanyScoped
{
    /// <summary>The partner.</summary>
    public long PartnerId { get; set; }
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The partner's net payments (all dates).</summary>
    public decimal Paid { get; set; }
    /// <summary>The first trip date the payments do not fully cover; null when they cover every trip.</summary>
    public DateOnly? BoundaryDate { get; set; }
    /// <summary>Partner cost of the trips before the boundary date (all paid).</summary>
    public decimal CostBefore { get; set; }
}
