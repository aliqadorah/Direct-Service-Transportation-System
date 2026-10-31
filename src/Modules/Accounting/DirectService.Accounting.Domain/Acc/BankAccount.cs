// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Accounting.Domain.Acc;

/// <summary>
/// acc.BankAccounts â€” Company-scoped.
/// </summary>
[StoredName("ACC.BankAccount")]
[TokenLabel("BankAccount")]
public class BankAccount : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Name of the bank.</summary>
    public string BankName { get; set; } = string.Empty;
    /// <summary>Bank account number.</summary>
    public string? AccountNumber { get; set; }
    /// <summary>IBAN for bank payments.</summary>
    public string? Iban { get; set; }
    /// <summary>Account currency.</summary>
    public string CurrencyCode { get; set; } = "JOD";
    /// <summary>Ledger account of the bank account.</summary>
    public long AccountId { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    public Account Account { get; set; } = null!;
}
