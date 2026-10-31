namespace DirectService.Accounting.Domain.Acc;

/// <summary>
/// acc.TaxCodes — Company-scoped (owner request 25 Oct 2026, DT-254). A tax code (رمز ضريبي) chosen on a manual journal line: code, rate,
/// Arabic and English description, status and display order. Seeded with the Jordanian VAT rates; edited in Settings → Tax codes. A code used
/// on a posted journal line cannot be deleted or change its rate (deactivate it and create a new one instead).
/// </summary>
[StoredName("ACC.TaxCode")]
[TokenLabel("TaxCode")]
public class TaxCode : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Code, unique per company (e.g. VAT_JO_16).</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Rate in percent, 0–100 with up to 2 decimals.</summary>
    public decimal Rate { get; set; }
    /// <summary>Description in Arabic.</summary>
    public string NameAr { get; set; } = string.Empty;
    /// <summary>Description in English.</summary>
    public string? NameEn { get; set; }
    /// <summary>Record status (Active / Archived): only active codes are offered and posted.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>Display order in the lists (then the code).</summary>
    public int SortOrder { get; set; }
}
