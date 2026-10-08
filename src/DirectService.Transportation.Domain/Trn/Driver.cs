// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Drivers â€” Company-scoped.
/// </summary>
public class Driver : AuditableEntity, ITenantScoped, ICompanyScoped, IArchivable
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Driver full name.</summary>
    public string FullName { get; set; } = string.Empty;
    /// <summary>National ID number.</summary>
    public string NationalId { get; set; } = string.Empty;
    /// <summary>Main mobile number.</summary>
    public string Phone1 { get; set; } = string.Empty;
    /// <summary>Second mobile number.</summary>
    public string? Phone2 { get; set; }
    /// <summary>Postal / street address.</summary>
    public string? Address { get; set; }
    /// <summary>Driving licence number.</summary>
    public string LicenceNo { get; set; } = string.Empty;
    /// <summary>Licence category.</summary>
    public string LicenceType { get; set; } = string.Empty;
    /// <summary>Licence expiry date.</summary>
    public DateOnly LicenceExpiry { get; set; }
    /// <summary>How wages and advances are paid to the driver.</summary>
    public string PaymentMethod { get; set; } = "Cash";
    /// <summary>Name of the bank.</summary>
    public string? BankName { get; set; }
    /// <summary>IBAN for bank payments.</summary>
    public string? Iban { get; set; }
    /// <summary>CliQ alias for instant payments.</summary>
    public string? CliqAlias { get; set; }
    /// <summary>Truck plate number.</summary>
    public string PlateNumber { get; set; } = string.Empty;
    /// <summary>Truck type / model.</summary>
    public string? TruckType { get; set; }
    /// <summary>Trailer number.</summary>
    public string? TrailerNo { get; set; }
    /// <summary>Truck load capacity in tons.</summary>
    public decimal CapacityTons { get; set; }
    /// <summary>Ownership type of the truck (decides own vs external fleet).</summary>
    public long OwnershipTypeId { get; set; }
    /// <summary>Partner company that owns the truck, for partner drivers.</summary>
    public long? PartnerId { get; set; }
    /// <summary>Truck registration expiry.</summary>
    public DateOnly RegistrationExpiry { get; set; }
    /// <summary>Truck insurance expiry.</summary>
    public DateOnly InsuranceExpiry { get; set; }
    /// <summary>Truck inspection expiry.</summary>
    public DateOnly InspectionExpiry { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions).</summary>
    public string? CustomValues { get; set; }
    public OwnershipType OwnershipType { get; set; } = null!;
    public Partner? Partner { get; set; }
}
