namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_PreviousBalanceItems — Company-scoped. Revision 6 §B15 (owner findings 27 Oct 2026, DT-276 / DT-277): the part of a driver's or
/// partner's balance from manual journal entries or the opening entry (DT-269) that a settlement (<see cref="SettlementId"/>) or a partner
/// payment / repayment (<see cref="PartnerPaymentId"/>) settled, per journal entry and account. The document's entry clears exactly these amounts on the same
/// account with the same party (Dr for an amount owed to the party, Cr for an amount the party owed), so the account is back to zero for the
/// party; what is still open on an account = the manual / opening lines naming the party − the items of active documents. Voiding the
/// document leaves its items with the voided document (no longer counted), so the balance is open again.
/// </summary>
[StoredName("TRN.PreviousBalanceItem")]
[TokenLabel("PreviousBalanceItem")]
public class PreviousBalanceItem : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The driver settlement that settled the amount (exactly one of this and <see cref="PartnerPaymentId"/>).</summary>
    public long? SettlementId { get; set; }
    /// <summary>The partner payment or repayment that settled the amount.</summary>
    public long? PartnerPaymentId { get; set; }
    /// <summary>The manual or opening journal entry whose lines naming the party are settled.</summary>
    public long JournalEntryId { get; set; }
    /// <summary>The account of those lines.</summary>
    public long AccountId { get; set; }
    /// <summary>Signed: above zero = owed to the driver / partner (credits on the account), below zero = owed by the driver / partner (debits).</summary>
    public decimal Amount { get; set; }
    public Settlement? Settlement { get; set; }
    public PartnerPayment? PartnerPayment { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
    public Account Account { get; set; } = null!;
}
