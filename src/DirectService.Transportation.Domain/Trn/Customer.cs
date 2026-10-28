// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Customers â€” Company-scoped.
/// </summary>
[StoredName("TRN.Customer")]
[TokenLabel("Customer")]
public class Customer : AuditableEntity, ITenantScoped, ICompanyScoped, IArchivable
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Customer name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Tax registration number.</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Commercial registration or national ID of the customer.</summary>
    public string? RegistrationNo { get; set; }
    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }
    /// <summary>Email address.</summary>
    public string? Email { get; set; }
    /// <summary>Postal / street address.</summary>
    public string? Address { get; set; }
    /// <summary>City.</summary>
    public string? City { get; set; }
    /// <summary>Main contact person.</summary>
    public string? ContactPerson { get; set; }
    /// <summary>Credit days; invoice due date = invoice date + this value.</summary>
    public int PaymentTermsDays { get; set; }
    /// <summary>Optional link to the same company kept as a party in shared Accounting.</summary>
    public long? ApArPartyId { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions).</summary>
    public string? CustomValues { get; set; }
    public ApArParty? ApArParty { get; set; }
}
