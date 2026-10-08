// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.ApArPayments â€” Company-scoped.
/// </summary>
public class ApArPayment : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document number taken from cfg.NumberSequences (prefix + zero-padded counter), assigned by the server when the document is issued.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>Document paid.</summary>
    public long DocumentId { get; set; }
    /// <summary>Date.</summary>
    public DateOnly PaymentDate { get; set; }
    /// <summary>Amount.</summary>
    public decimal Amount { get; set; }
    /// <summary>How the money was paid or received.</summary>
    public string PaymentMethod { get; set; } = string.Empty;
    /// <summary>Cash box the money came from or went to.</summary>
    public long? CashBoxId { get; set; }
    /// <summary>Bank account the money came from or went to.</summary>
    public long? BankAccountId { get; set; }
    /// <summary>External reference, e.g. bank transfer or cheque number.</summary>
    public string? Reference { get; set; }
    /// <summary>Number of paper attachments, printed on the voucher. No files are stored.</summary>
    public int AttachmentsCount { get; set; }
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
    public ApArDocument Document { get; set; } = null!;
    public CashBox? CashBox { get; set; }
    public BankAccount? BankAccount { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
}
