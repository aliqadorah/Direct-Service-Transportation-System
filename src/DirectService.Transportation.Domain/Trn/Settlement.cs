// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Settlements â€” Company-scoped.
/// </summary>
public class Settlement : AuditableEntity, ITenantScoped, ICompanyScoped, ICashDocument
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Driver settled.</summary>
    public long DriverId { get; set; }
    /// <summary>Settlement date.</summary>
    public DateOnly SettlementDate { get; set; }
    /// <summary>Σ wages of the included trips.</summary>
    public decimal GrossWages { get; set; }
    /// <summary>Σ included advances.</summary>
    public decimal AdvancesTotal { get; set; }
    /// <summary>Σ deduction lines.</summary>
    public decimal DeductionsTotal { get; set; }
    /// <summary>GrossWages − AdvancesTotal − DeductionsTotal.</summary>
    public decimal NetPayable { get; set; }
    /// <summary>
    /// Revision 6 (DT-228): when advances and deductions exceed the wages, the part the driver paid back with the settlement (cash box or
    /// bank account and reference in the payment columns). Net payable is then 0.
    /// </summary>
    public decimal RepaidAmount { get; set; }
    /// <summary>Revision 6 (DT-228): the unpaid rest after a partial repayment, carried as a new open advance to the next settlement.</summary>
    public decimal CarriedForward { get; set; }
    /// <summary>How the money was paid or received.</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    /// <summary>Cash box the money came from or went to.</summary>
    public long? CashBoxId { get; set; }
    /// <summary>Bank account the money came from or went to.</summary>
    public long? BankAccountId { get; set; }
    /// <summary>External reference, e.g. bank transfer or cheque number.</summary>
    public string? Reference { get; set; }
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
    public Driver Driver { get; set; } = null!;
    public CashBox? CashBox { get; set; }
    public BankAccount? BankAccount { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
    public ICollection<SettlementDeduction> Deductions { get; set; } = new List<SettlementDeduction>();
}
