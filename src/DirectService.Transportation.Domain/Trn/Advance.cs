// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Advances â€” Company-scoped.
/// </summary>
public class Advance : AuditableEntity, ITenantScoped, ICompanyScoped, ICashDocument
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Driver receiving the advance.</summary>
    public long DriverId { get; set; }
    /// <summary>Advance date.</summary>
    public DateOnly AdvanceDate { get; set; }
    /// <summary>Amount advanced.</summary>
    public decimal Amount { get; set; }
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
    /// <summary>Settlement that deducted the advance.</summary>
    public long? SettlementId { get; set; }
    /// <summary>
    /// Revision 6 (DT-228): the settlement whose unpaid rest this advance carries (payment method "Carried": no money moved, no journal
    /// entry of its own — the settlement's entry keeps it on driver advances). Voided with that settlement.
    /// </summary>
    public long? CarriedFromSettlementId { get; set; }
    /// <summary>The journal entry this document posted. After an edit (reverse + re-post) it points to the current active entry.</summary>
    public long? JournalEntryId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    public Driver Driver { get; set; } = null!;
    public CashBox? CashBox { get; set; }
    public BankAccount? BankAccount { get; set; }
    public Settlement? Settlement { get; set; }
    public Settlement? CarriedFromSettlement { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
}
