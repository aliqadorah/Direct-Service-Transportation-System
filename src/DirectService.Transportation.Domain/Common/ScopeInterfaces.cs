namespace DirectService.Transportation.Domain.Common;

/// <summary>Loading or unloading site (two separate lists with the same fields, FR-160).</summary>
public interface ISite : IArchivable
{
    string Name { get; set; }
    string? Area { get; set; }
}

/// <summary>A numbered cash or bank document with a status and its journal entry (receipts, advances, settlements, partner payments).</summary>
public interface ICashDocument
{
    long Id { get; }
    Guid PublicId { get; }
    string Number { get; set; }
    string Status { get; set; }
    long? JournalEntryId { get; set; }
}

/// <summary>
/// A national e-invoicing submission row (trn.TRN_EInvoiceSubmissions for invoices and credit notes, acc.ApArEInvoiceSubmissions for
/// receivable documents and their returns): the queue and outcome columns shared by both tables.
/// </summary>
public interface IEInvoiceSubmission
{
    long Id { get; }
    Guid PublicId { get; }
    string DocumentType { get; set; }
    string Status { get; set; }
    long? Icv { get; set; }
    Guid? UniqueId { get; set; }
    string? SystemNumber { get; set; }
    string? QrCode { get; set; }
    string? RequestPayload { get; set; }
    string? ResponsePayload { get; set; }
    int Attempts { get; set; }
    string? LastError { get; set; }
    DateTime? SubmittedAt { get; set; }
    DateTime CreatedAt { get; }
    DateTime? UpdatedAt { get; }
    byte[] RowVersion { get; }
}
