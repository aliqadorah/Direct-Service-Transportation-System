// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.ApArParties â€” Company-scoped.
/// </summary>
[StoredName("ACC.ApArParty")]
[TokenLabel("ApArParty")]
public class ApArParty : AuditableEntity, ITenantScoped, ICompanyScoped, IArchivable
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Supplier or customer.</summary>
    public string PartyRole { get; set; } = string.Empty;
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Tax registration number.</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }
    /// <summary>Postal / street address.</summary>
    public string? Address { get; set; }
    /// <summary>Payable or receivable control account.</summary>
    public long ControlAccountId { get; set; }
    /// <summary>Credit days.</summary>
    public int PaymentTermsDays { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    public Account ControlAccount { get; set; } = null!;
}
