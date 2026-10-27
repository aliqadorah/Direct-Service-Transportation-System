// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_PartnerPayments â€” Company-scoped.
/// </summary>
public class PartnerPayment : AuditableEntity, ITenantScoped, ICompanyScoped, ICashDocument
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Partner paid.</summary>
    public long PartnerId { get; set; }
    /// <summary>Payment date.</summary>
    public DateOnly PaymentDate { get; set; }
    /// <summary>Amount paid.</summary>
    public decimal Amount { get; set; }
    /// <summary>
    /// Revision 6 (DT-228): "Payment" (to the partner, the default) or "Repayment" (from a partner who owes the company: money received,
    /// Dr cash / bank, Cr partner payable). Same PPY numbering.
    /// </summary>
    public string Kind { get; set; } = "Payment";
    /// <summary>
    /// Revision 6 (DT-230): of the partner trips this payment settles, the part already paid earlier for a trip voided or made cheaper that
    /// the user chose to deduct from the next payment(s) — set off, no money moved (shown on the voucher and the statement).
    /// </summary>
    public decimal DeductedAmount { get; set; }
    /// <summary>
    /// Revision 6 §B15 (DT-277): the partner's balance from manual / opening journal entries settled with this payment / repayment (signed: above
    /// zero owed to the partner, below zero owed by the partner); per account in <see cref="PreviousBalanceItem"/>. The part for the partner's
    /// trips (DT-271 coverage) is Amount − PreviousBalance for a payment and Amount + PreviousBalance for a repayment.
    /// </summary>
    public decimal PreviousBalance { get; set; }
    /// <summary>How the money was paid or received.</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    /// <summary>Cash box the money came from or went to.</summary>
    public long? CashBoxId { get; set; }
    /// <summary>Bank account the money came from or went to.</summary>
    public long? BankAccountId { get; set; }
    /// <summary>External reference, e.g. bank transfer or cheque number.</summary>
    public string? Reference { get; set; }
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Status.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry.</summary>
    public long? JournalEntryId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    public Partner Partner { get; set; } = null!;
    public CashBox? CashBox { get; set; }
    public BankAccount? BankAccount { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
}
