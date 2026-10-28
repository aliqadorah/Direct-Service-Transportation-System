// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_EInvoiceSubmissions â€” Company-scoped.
/// </summary>
[StoredName("TRN.EInvoiceSubmission")]
[TokenLabel("EInvoiceSubmission")]
public class EInvoiceSubmission : AuditableEntity, ITenantScoped, ICompanyScoped, IEInvoiceSubmission
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Document submitted.</summary>
    public string DocumentType { get; set; } = string.Empty;
    /// <summary>Invoice submitted.</summary>
    public long? InvoiceId { get; set; }
    /// <summary>Credit note submitted.</summary>
    public long? CreditNoteId { get; set; }
    /// <summary>Submission state.</summary>
    public string Status { get; set; } = "Queued";
    /// <summary>Invoice counter value sent to the national system, taken from cfg.EInvoiceSettings.IcvCounter.</summary>
    public long? Icv { get; set; }
    /// <summary>Unique identifier returned by the national e-invoicing system.</summary>
    public Guid? UniqueId { get; set; }
    /// <summary>Document number assigned by the national system.</summary>
    public string? SystemNumber { get; set; }
    /// <summary>QR code content returned by the national system; printed on the invoice.</summary>
    public string? QrCode { get; set; }
    /// <summary>UBL 2.1 document sent (JSON/XML text) — kept for audit.</summary>
    public string? RequestPayload { get; set; }
    /// <summary>Full response received — kept for audit.</summary>
    public string? ResponsePayload { get; set; }
    /// <summary>Number of attempts made so far.</summary>
    public int Attempts { get; set; }
    /// <summary>Last error message returned.</summary>
    public string? LastError { get; set; }
    /// <summary>UTC time accepted by the national system.</summary>
    public DateTime? SubmittedAt { get; set; }
    public Invoice? Invoice { get; set; }
    public CreditNote? CreditNote { get; set; }
}
