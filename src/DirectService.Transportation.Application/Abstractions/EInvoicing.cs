namespace DirectService.Transportation.Application.Abstractions;

/// <summary>A party on an e-invoice (seller or buyer) as the platform knows it; connectors map it to their format.</summary>
public sealed record EInvoiceParty(string Name, string? TaxNumber, string? Phone, string? City);

/// <summary>
/// One line as sent: quantity, unit price before tax, line discount, net amount after discount, tax rate and tax. Amounts are net of
/// tax and already in the currency's decimals; the lines add up exactly to the document's stored totals.
/// </summary>
public sealed record EInvoiceLine(int Number, string Description, decimal Quantity, decimal UnitPrice, decimal Discount, decimal NetAmount, decimal TaxRate, decimal Tax);

/// <summary>The original invoice of a credit note (FR-064): its number, unique identifier and total.</summary>
public sealed record EInvoiceReference(string Number, Guid Uuid, decimal Total);

/// <summary>
/// A document to submit, independent of the national format (FR-066): invoice or credit note, number, platform UUID (kept across
/// resubmissions), issue date, invoice counter (ICV), currency, payment method, seller registration, parties, lines and totals
/// (TaxExclusive = Σ quantity × unit price; Payable = TaxExclusive − Discount + Tax).
/// </summary>
public sealed record EInvoiceDocument(
    string Kind, string Number, Guid Uuid, DateOnly IssueDate, long Icv, string Currency, int Decimals, bool Receivable,
    string RegistrationType, string? IncomeSourceSequence, EInvoiceParty Seller, EInvoiceParty Buyer, IReadOnlyList<EInvoiceLine> Lines,
    decimal TaxExclusive, decimal Discount, decimal Tax, decimal Payable, EInvoiceReference? Original, string? Note);

/// <summary>A missing or invalid value found before submission (FR-060): the field and an error code of the catalog.</summary>
public sealed record EInvoiceProblem(string Field, string Code);

/// <summary>Decrypted API credentials and the target environment, used only for the call.</summary>
public sealed record EInvoiceCredentials(string ClientId, string SecretKey, string Environment)
{
    public override string ToString() => $"EInvoiceCredentials({Environment})";   // never print the secrets
}

public enum EInvoiceOutcomeKind
{
    /// <summary>Accepted by the national system (first time or already accepted).</summary>
    Accepted,
    /// <summary>Refused by the national system for the document's content; retrying unchanged will not help.</summary>
    Rejected,
    /// <summary>Technical failure (network, timeout, server error); retried automatically.</summary>
    Failed,
}

/// <summary>The national system's answer: identifiers and QR when accepted, the reason otherwise, and the raw response for audit.</summary>
/// <remarks><see cref="CredentialsRefused"/>: the system refused the company's credentials (HTTP 401/403) — a set-up problem, not the document's (DT-189).</remarks>
public sealed record EInvoiceOutcome(EInvoiceOutcomeKind Kind, Guid? UniqueId, string? SystemNumber, string? QrCode, string? Message, string? ResponsePayload,
    bool CredentialsRefused = false)
{
    /// <summary>How a refusal of the credentials starts (stored as the submission's last error; counted by the set-up notification).</summary>
    public const string CredentialsRefusedText = "JoFotara refused the credentials";
}

/// <summary>
/// A national e-invoicing connector (FR-066, INT-01): market rules, the document in the national format (stored as the request
/// payload) and the submission. The launch market is JoFotara (UBL 2.1); a simulator serves development and tests.
/// </summary>
public interface IEInvoiceConnector
{
    string Name { get; }

    /// <summary>Market-specific checks before submission (format of tax numbers, supported registration types …).</summary>
    IReadOnlyList<EInvoiceProblem> Validate(EInvoiceDocument document);

    /// <summary>The document in the national format.</summary>
    string Build(EInvoiceDocument document);

    Task<EInvoiceOutcome> SubmitAsync(string payload, EInvoiceCredentials credentials, CancellationToken ct);
}

/// <summary>Encrypts and decrypts stored credentials (SEC-09); values never leave the server in clear text.</summary>
public interface ISecretProtector
{
    byte[] Protect(string value);

    string Unprotect(byte[] value);
}

/// <summary>Row locks of the e-invoicing tables (raw SQL in Infrastructure, listed in BUILD_STATUS.md).</summary>
public interface IEInvoiceStore
{
    /// <summary>Next invoice counter of the company (one counter for every module), inside the document's transaction.</summary>
    Task<long> NextIcvAsync(CancellationToken ct);

    /// <summary>Claims a submission for processing (UPDLOCK, READPAST); false when another worker or user holds it.</summary>
    Task<bool> TryClaimAsync(long submissionId, CancellationToken ct);

    /// <summary>Waits for and locks one submission (storing an outcome after the call).</summary>
    Task LockAsync(long submissionId, CancellationToken ct);

    /// <summary>Waits for and locks the submissions of a document, so a void and a submission in progress serialise.</summary>
    Task LockDocumentAsync(long? invoiceId, long? creditNoteId, CancellationToken ct);

    /// <summary>As <see cref="TryClaimAsync"/> for acc.ApArEInvoiceSubmissions (receivable documents and returns, D-15).</summary>
    Task<bool> TryClaimApArAsync(long submissionId, CancellationToken ct);

    /// <summary>As <see cref="LockAsync"/> for acc.ApArEInvoiceSubmissions.</summary>
    Task LockApArAsync(long submissionId, CancellationToken ct);

    /// <summary>As <see cref="LockDocumentAsync"/> for a receivable document or return.</summary>
    Task LockApArDocumentAsync(long? documentId, long? returnId, CancellationToken ct);
}
