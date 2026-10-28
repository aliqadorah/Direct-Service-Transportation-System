namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_PartnerOverpayments — Company-scoped. Revision 6 (owner decision 21 Oct 2026 on DT-228, DT-230): a partner trip voided or made
/// cheaper after the partner was paid, with what the user chose for the amount already paid — "Return" (the partner owes the company until
/// a partner repayment), "Deduct" (set off against the next partner payment(s), see <see cref="PartnerPayment.DeductedAmount"/>) or
/// "NotPaid" (the payment was not actually made: <see cref="PartnerPaymentId"/> was voided for this trip).
/// </summary>
[StoredName("TRN.PartnerOverpayment")]
[TokenLabel("PartnerOverpayment")]
public class PartnerOverpayment : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The partner that was paid.</summary>
    public long PartnerId { get; set; }
    /// <summary>The trip voided or changed.</summary>
    public long TripId { get; set; }
    /// <summary>"NotPaid": the payment voided because it was not actually made.</summary>
    public long? PartnerPaymentId { get; set; }
    /// <summary>The amount paid that the change left uncovered (for "NotPaid": the payment's amount).</summary>
    public decimal Amount { get; set; }
    /// <summary>Return, Deduct or NotPaid.</summary>
    public string Handling { get; set; } = string.Empty;
    /// <summary>The user's reason for the change.</summary>
    public string Reason { get; set; } = string.Empty;
    public Partner Partner { get; set; } = null!;
    public Trip Trip { get; set; } = null!;
    public PartnerPayment? PartnerPayment { get; set; }
}
