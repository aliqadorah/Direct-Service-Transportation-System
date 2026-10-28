// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.EInvoiceSettings â€” Company-scoped.
/// </summary>
[StoredName("CFG.EInvoiceSettings")]
[TokenLabel("EInvoiceSettings")]
public class EInvoiceSettings : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>E-invoicing switched on.</summary>
    public bool IsEnabled { get; set; }
    /// <summary>Connector used.</summary>
    public string Connector { get; set; } = "JoFotara";
    /// <summary>Seller tax number registered with the national system.</summary>
    public string? SellerTaxNumber { get; set; }
    /// <summary>Taxpayer registration type.</summary>
    public string? RegistrationType { get; set; }
    /// <summary>Income source sequence issued by the tax authority.</summary>
    public string? IncomeSourceSequence { get; set; }
    /// <summary>Encrypted API client id.</summary>
    public byte[]? ClientIdEncrypted { get; set; }
    /// <summary>Encrypted API secret.</summary>
    public byte[]? SecretKeyEncrypted { get; set; }
    /// <summary>Target environment.</summary>
    public string Environment { get; set; } = "Test";
    /// <summary>Last invoice counter value issued for the company.</summary>
    public long IcvCounter { get; set; }
}
