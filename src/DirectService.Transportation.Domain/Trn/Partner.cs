// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Partners â€” Company-scoped.
/// </summary>
public class Partner : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Partner name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Tax registration number.</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }
    /// <summary>Postal / street address.</summary>
    public string? Address { get; set; }
    /// <summary>Main contact person.</summary>
    public string? ContactPerson { get; set; }
    /// <summary>Name of the bank.</summary>
    public string? BankName { get; set; }
    /// <summary>IBAN for bank payments.</summary>
    public string? Iban { get; set; }
    /// <summary>CliQ alias for instant payments.</summary>
    public string? CliqAlias { get; set; }
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
}
