// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.Accounts â€” Company-scoped.
/// </summary>
public class Account : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Account code.</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Account name in Arabic.</summary>
    public string NameAr { get; set; } = string.Empty;
    /// <summary>Account name in English.</summary>
    public string? NameEn { get; set; }
    /// <summary>Account class.</summary>
    public string AccountType { get; set; } = string.Empty;
    /// <summary>Parent account; NULL = top level.</summary>
    public long? ParentId { get; set; }
    /// <summary>1 = leaf account that can receive entries.</summary>
    public bool IsPostable { get; set; } = true;
    /// <summary>Opening debit balance.</summary>
    public decimal OpeningDebit { get; set; }
    /// <summary>Opening credit balance.</summary>
    public decimal OpeningCredit { get; set; }
    /// <summary>1 = control account; every line needs a party (customer, driver, partner, supplier).</summary>
    public bool IsControlAccount { get; set; }
    /// <summary>Party kind required on lines of a control account.</summary>
    public string? ControlPartyType { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>1 = tax account: offered as the tax account of journal lines (DT-256).</summary>
    public bool IsTaxAccount { get; set; }
    public Account? Parent { get; set; }
    public ICollection<Account> Children { get; set; } = new List<Account>();
}
