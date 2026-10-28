// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.CashBoxes â€” Company-scoped.
/// </summary>
[StoredName("ACC.CashBox")]
[TokenLabel("CashBox")]
public class CashBox : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Cash box name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Ledger account of the cash box.</summary>
    public long AccountId { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    public Account Account { get; set; } = null!;
}
