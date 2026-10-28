// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Companies â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.Company")]
[TokenLabel("Company")]
public class Company : AuditableEntity, ITenantScoped, ICompanyRoot
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Legal name (Arabic or main language).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Legal name in English.</summary>
    public string? NameEn { get; set; }
    /// <summary>Tax registration number.</summary>
    public string? TaxNumber { get; set; }
    /// <summary>Commercial registration number.</summary>
    public string? CommercialRegNo { get; set; }
    /// <summary>Postal / street address.</summary>
    public string? Address { get; set; }
    /// <summary>Phone number.</summary>
    public string? Phone { get; set; }
    /// <summary>Email address.</summary>
    public string? Email { get; set; }
    /// <summary>Company logo image shown on screens and printouts.</summary>
    public byte[]? LogoFile { get; set; }
    /// <summary>ISO 3166 country of the company; drives market formats.</summary>
    public string CountryCode { get; set; } = "JO";
    /// <summary>ISO 4217 currency.</summary>
    public string CurrencyCode { get; set; } = "JOD";
    /// <summary>Decimals shown and rounded to.</summary>
    public byte CurrencyDecimals { get; set; } = 3;
    /// <summary>Sales tax rate %.</summary>
    public decimal TaxRate { get; set; } = 16.00m;
    /// <summary>1 = prices include tax; 0 = tax added on top (default, BR-022).</summary>
    public bool TaxInclusive { get; set; }
    /// <summary>IANA time zone for dates and period cut-off.</summary>
    public string TimeZone { get; set; } = "Asia/Amman";
    /// <summary>When initial setup/data migration was marked complete. Until then an opening-balance difference is allowed with a warning (FR-110).</summary>
    public DateTime? MigrationCompletedAt { get; set; }
    /// <summary>Record status. An archived company is hidden from the company switcher and read-only; data kept.</summary>
    public string Status { get; set; } = "Active";
}
