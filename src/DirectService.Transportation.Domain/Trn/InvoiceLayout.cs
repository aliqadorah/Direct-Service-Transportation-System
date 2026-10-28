// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_InvoiceLayouts â€” Company-scoped.
/// </summary>
[StoredName("TRN.InvoiceLayout")]
[TokenLabel("InvoiceLayout")]
public class InvoiceLayout : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Which document the layout is for.</summary>
    public string LayoutType { get; set; } = string.Empty;
    /// <summary>Printed title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Text printed in the header band.</summary>
    public string? BandText { get; set; }
    /// <summary>How trips are grouped in the annex.</summary>
    public string Grouping { get; set; } = "LoadingSite";
    /// <summary>Columns shown in the trip annex, in order (JSON array).</summary>
    public string AnnexColumns { get; set; } = string.Empty;
    /// <summary>Custom column/label texts (JSON).</summary>
    public string? Labels { get; set; }
    /// <summary>Printed footer.</summary>
    public string? FooterText { get; set; }
    /// <summary>Signature boxes (JSON array of titles).</summary>
    public string? Signatures { get; set; }
    /// <summary>Print the company logo.</summary>
    public bool ShowLogo { get; set; } = true;
    /// <summary>Print the e-invoice QR code.</summary>
    public bool ShowQr { get; set; } = true;
}
