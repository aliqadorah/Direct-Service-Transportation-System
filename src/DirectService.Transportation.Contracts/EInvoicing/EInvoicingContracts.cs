using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Contracts.EInvoicing;

/// <summary>Stored values of e-invoicing (design: cfg.EInvoiceSettings, trn.TRN_EInvoiceSubmissions).</summary>
public static class EInvoiceCodes
{
    public const string NotRequired = "NotRequired";
    public const string Queued = "Queued";
    public const string Submitted = "Submitted";
    public const string Rejected = "Rejected";
    public const string Invoice = "Invoice";
    public const string CreditNote = "CreditNote";
    public const string JoFotara = "JoFotara";
    public const string Simulator = "Simulator";
    public const string Test = "Test";
    public const string Production = "Production";
    /// <summary>Taxpayer registration types (JoFotara: income, general sales tax, special sales tax).</summary>
    public const string Income = "Income";
    public const string Sales = "Sales";
    public const string Special = "Special";
}

/// <summary>
/// E-invoicing settings (FR-059): switch, connector, seller tax number, registration type, income-source sequence, credentials and
/// environment. Credentials are write-only: an empty value keeps the stored one; they are never returned.
/// <c>SetTaxRateToZero</c> (income tax only, DT-226): the company's default tax rate becomes 0% in the same save; documents already
/// made keep their own rate.
/// </summary>
public sealed record EInvoiceSettingsRequest(
    bool IsEnabled, string? Connector, string? SellerTaxNumber, string? RegistrationType, string? IncomeSourceSequence,
    string? ClientId, string? SecretKey, string? Environment, string? RowVersion, bool SetTaxRateToZero = false);

/// <summary>
/// The settings without the credentials (only whether they are stored), the counter, the connectors available and the company's
/// default tax rate (the page offers 0% when income tax is chosen, DT-226).
/// </summary>
public sealed record EInvoiceSettingsResponse(
    bool IsEnabled, string Connector, string? SellerTaxNumber, string RegistrationType, string? IncomeSourceSequence, bool HasClientId, bool HasSecretKey,
    string Environment, long IcvCounter, IReadOnlyList<string> Connectors, string? RowVersion, decimal CompanyTaxRate);

/// <summary>A missing or invalid value that keeps a document from being submitted (FR-060), with its message.</summary>
public sealed record EInvoiceProblemResponse(string Field, string Code, string Message);

/// <summary>
/// A submission (FR-061 … FR-063): document, customer, date and total, status (Queued / Submitted / Rejected / NotRequired), counter,
/// the national system's identifiers, attempts, the reason of the last failure or the problems to fix, submission time.
/// </summary>
public sealed record EInvoiceSubmissionResponse(
    string Id, string DocumentType, DocRef Document, Ref Customer, DateOnly DocumentDate, decimal Total, string Status, long? Icv, Guid? UniqueId,
    string? SystemNumber, bool HasQr, int Attempts, string? LastError, IReadOnlyList<EInvoiceProblemResponse> Problems, bool WaitingForInvoice,
    DateTime? SubmittedAt, DateTime CreatedAt, DateTime? UpdatedAt, string RowVersion);

/// <summary>A submission with the document sent and the response received (audit), and a credit note's original invoice.</summary>
public sealed record EInvoiceSubmissionDetails(EInvoiceSubmissionResponse Submission, string? RequestPayload, string? ResponsePayload, DocRef? OriginalInvoice);

/// <summary>E-invoicing list and report filters (FR-061, FR-065): status, document type, customer, period (document date), number search.</summary>
public sealed record EInvoiceListQuery(
    string? Status, string? DocumentType, string? Customer, DateOnly? From, DateOnly? To, string? Q, string? Sort, int Page = 1, int PageSize = 50);

/// <summary>Count and document total per status for the filtered period (FR-065).</summary>
public sealed record EInvoiceStatusTotal(string Status, int Count, decimal Total);

public sealed record EInvoicePage(PagedResult<EInvoiceSubmissionResponse> Page, IReadOnlyList<EInvoiceStatusTotal> Totals);
