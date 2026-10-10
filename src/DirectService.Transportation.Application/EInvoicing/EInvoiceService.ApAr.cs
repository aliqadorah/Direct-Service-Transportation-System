using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.EInvoicing;

/// <summary>
/// E-invoicing of Accounting receivable documents and their returns (D-15, design acc.ApArEInvoiceSubmissions): the same queue, counter
/// (one ICV for every module), connectors, validation, worker and resubmission rules as invoices and credit notes; a return is sent only
/// after its document was accepted; voids cancel documents not yet accepted and are refused once accepted or while the outcome is unknown.
/// </summary>
public sealed partial class EInvoiceService
{
    public const string ApArEntityType = "ACC.ApArEInvoiceSubmission";

    /// <summary>Queues a new receivable document when e-invoicing is on: Queued with the next ICV, inside the document's transaction.</summary>
    internal async Task QueueApArDocumentAsync(long documentId, CancellationToken ct)
    {
        if (await settings.EnabledAsync(ct).ConfigureAwait(false) is null) return;
        var icv = await store.NextIcvAsync(ct).ConfigureAwait(false);
        db.Set<ApArEInvoiceSubmission>().Add(new ApArEInvoiceSubmission { DocumentType = EInvoiceCodes.Invoice, ApArDocumentId = documentId, Status = EInvoiceCodes.Queued, Icv = icv });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        signal.Notify();
    }

    /// <summary>Queues a receivable return as a credit note when e-invoicing is on and its document is itself e-invoiced (as H2 of B7).</summary>
    internal async Task QueueApArReturnAsync(long returnId, long documentId, CancellationToken ct)
    {
        if (!await db.Set<ApArEInvoiceSubmission>().AnyAsync(s => s.ApArDocumentId == documentId && s.Status != EInvoiceCodes.NotRequired, ct).ConfigureAwait(false)) return;
        if (await settings.EnabledAsync(ct).ConfigureAwait(false) is null) return;
        var icv = await store.NextIcvAsync(ct).ConfigureAwait(false);
        db.Set<ApArEInvoiceSubmission>().Add(new ApArEInvoiceSubmission { DocumentType = EInvoiceCodes.CreditNote, ApArReturnId = returnId, Status = EInvoiceCodes.Queued, Icv = icv });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        signal.Notify();
    }

    /// <summary>On void of a receivable document or return: false when accepted; refused while the outcome is unknown; otherwise cancelled (NotRequired).</summary>
    internal async Task<bool> CancelApArAsync(long? documentId, long? returnId, string reason, CancellationToken ct)
    {
        await store.LockApArDocumentAsync(documentId, returnId, ct).ConfigureAwait(false);
        var rows = await db.Set<ApArEInvoiceSubmission>().Where(s => (documentId != null && s.ApArDocumentId == documentId) || (returnId != null && s.ApArReturnId == returnId))
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var r in rows) await db.Set<ApArEInvoiceSubmission>().Entry(r).ReloadAsync(ct).ConfigureAwait(false);
        if (rows.Any(r => r.Status == EInvoiceCodes.Submitted)) return false;
        if (rows.Any(r => r.Status is EInvoiceCodes.Queued or EInvoiceCodes.Rejected && OutcomeUnknown(r.LastError))) throw Fail.Rule(ErrorCodes.EInvoiceOutcomeUnknown);
        foreach (var r in rows.Where(r => r.Status is EInvoiceCodes.Queued or EInvoiceCodes.Rejected))
        {
            r.Status = EInvoiceCodes.NotRequired;
            r.LastError = Truncate(reason);
            changes.SetAction(r, "Cancel", reason);
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The document of an AP/AR submission, or null for a return whose document is not accepted yet.</summary>
    private async Task<EInvoiceDocument?> ApArDocumentAsync(ApArEInvoiceSubmission s, EInvoiceSettings config, CancellationToken ct)
    {
        if (s.ApArDocumentId is long documentId)
        {
            var doc = await db.Set<ApArDocument>().AsNoTracking().SingleAsync(d => d.Id == documentId, ct).ConfigureAwait(false);
            return await builder.ApArDocumentAsync(doc, s.Icv ?? 0, config, ct).ConfigureAwait(false);
        }
        var ret = await db.Set<ApArReturn>().AsNoTracking().SingleAsync(x => x.Id == s.ApArReturnId, ct).ConfigureAwait(false);
        var original = await db.Set<ApArDocument>().AsNoTracking().SingleAsync(d => d.Id == ret.DocumentId, ct).ConfigureAwait(false);
        var accepted = await db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(x => x.ApArDocumentId == original.Id && x.Status == EInvoiceCodes.Submitted)
            .Select(x => new { x.UniqueId }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (accepted is null) return null;
        return await builder.ApArReturnAsync(ret, original, accepted.UniqueId ?? original.PublicId, s.Icv ?? 0, config, ct).ConfigureAwait(false);
    }

    // ============================================================ manual submission and reads

    /// <summary>Manual submission or resubmission of an AP/AR submission (acct.apar).</summary>
    public async Task<EInvoiceSubmissionDetails> SubmitApArAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<ApArEInvoiceSubmission>(token, ct).ConfigureAwait(false);
        await ProcessApArAsync(s.Id, manual: true, ct).ConfigureAwait(false);
        return await ApArDetailsAsync(s.Id, ct).ConfigureAwait(false);
    }

    private sealed record ApArRow(ApArEInvoiceSubmission S, Guid DocPid, string Number, DateOnly Date, decimal Total, Guid PartyPid, string PartyName, Guid? OriginalPid,
        string? OriginalNumber);

    private static IQueryable<ApArRow> ApArRows(IQueryable<ApArEInvoiceSubmission> q) => q.Select(s => new ApArRow(s,
        s.ApArDocumentId != null ? s.ApArDocument!.PublicId : s.ApArReturn!.PublicId,
        s.ApArDocumentId != null ? s.ApArDocument!.Number : s.ApArReturn!.Number,
        s.ApArDocumentId != null ? s.ApArDocument!.DocumentDate : s.ApArReturn!.ReturnDate,
        s.ApArDocumentId != null ? s.ApArDocument!.Total : s.ApArReturn!.Total,
        s.ApArDocumentId != null ? s.ApArDocument!.Party.PublicId : s.ApArReturn!.Document.Party.PublicId,
        s.ApArDocumentId != null ? s.ApArDocument!.Party.Name : s.ApArReturn!.Document.Party.Name,
        s.ApArReturnId != null ? s.ApArReturn!.Document.PublicId : null,
        s.ApArReturnId != null ? s.ApArReturn!.Document.Number : null));

    /// <summary>AP/AR submissions with the same status filter as the invoice list (attention = rejected or queued with problems).</summary>
    public async Task<PagedResult<EInvoiceSubmissionResponse>> ListApArAsync(string? status, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct)
    {
        var query = db.Set<ApArEInvoiceSubmission>().AsNoTracking();
        query = (status?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "queued" => query.Where(s => s.Status == EInvoiceCodes.Queued),
            "submitted" => query.Where(s => s.Status == EInvoiceCodes.Submitted),
            "rejected" => query.Where(s => s.Status == EInvoiceCodes.Rejected),
            "notrequired" => query.Where(s => s.Status == EInvoiceCodes.NotRequired),
            "attention" => query.Where(s => s.Status == EInvoiceCodes.Rejected || (s.Status == EInvoiceCodes.Queued && s.LastError != null)),
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, "status"),
        };
        if (from is DateOnly f) query = query.Where(s => (s.ApArDocumentId != null ? s.ApArDocument!.DocumentDate : s.ApArReturn!.ReturnDate) >= f);
        if (to is DateOnly t) query = query.Where(s => (s.ApArDocumentId != null ? s.ApArDocument!.DocumentDate : s.ApArReturn!.ReturnDate) <= t);
        var paging = new PageRequest(page, pageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await ApArRows(query.OrderByDescending(s => s.Id).Skip(paging.Skip).Take(paging.SafePageSize)).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<EInvoiceSubmissionResponse>(rows.Select(ApArResponse).ToList(), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<EInvoiceSubmissionDetails> GetApArAsync(string token, CancellationToken ct)
    {
        var s = await support.FindAsync<ApArEInvoiceSubmission>(token, ct).ConfigureAwait(false);
        return await ApArDetailsAsync(s.Id, ct).ConfigureAwait(false);
    }

    /// <summary>The submission of a receivable document or return, or 404 when none.</summary>
    public async Task<EInvoiceSubmissionDetails> ForApArAsync(long? documentId, long? returnId, CancellationToken ct)
    {
        var id = await db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(s => (documentId != null && s.ApArDocumentId == documentId) || (returnId != null && s.ApArReturnId == returnId))
            .OrderByDescending(s => s.Id).Select(s => (long?)s.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        return await ApArDetailsAsync(id, ct).ConfigureAwait(false);
    }

    /// <summary>The QR returned for an accepted receivable document or return (printed on it).</summary>
    internal Task<string?> ApArQrAsync(long? documentId, long? returnId, CancellationToken ct) =>
        db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(s => ((documentId != null && s.ApArDocumentId == documentId) || (returnId != null && s.ApArReturnId == returnId))
            && s.Status == EInvoiceCodes.Submitted).OrderByDescending(s => s.Id).Select(s => s.QrCode).FirstOrDefaultAsync(ct);

    private async Task<EInvoiceSubmissionDetails> ApArDetailsAsync(long id, CancellationToken ct)
    {
        var row = await ApArRows(db.Set<ApArEInvoiceSubmission>().AsNoTracking().Where(s => s.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        return new EInvoiceSubmissionDetails(ApArResponse(row), row.S.RequestPayload, row.S.ResponsePayload,
            row.OriginalPid is Guid g ? new DocRef(support.Token<ApArDocument>(g), row.OriginalNumber) : null);
    }

    private EInvoiceSubmissionResponse ApArResponse(ApArRow r)
    {
        var problems = Problems(r.S.LastError);
        var document = r.S.ApArDocumentId != null ? new DocRef(support.Token<ApArDocument>(r.DocPid), r.Number) : new DocRef(support.Token<ApArReturn>(r.DocPid), r.Number);
        return new EInvoiceSubmissionResponse(support.Token<ApArEInvoiceSubmission>(r.S.PublicId), r.S.DocumentType, document, new Ref(support.Token<ApArParty>(r.PartyPid), r.PartyName),
            r.Date, r.Total, r.S.Status, r.S.Icv, r.S.UniqueId, r.S.SystemNumber, !string.IsNullOrEmpty(r.S.QrCode), r.S.Attempts,
            problems.Count > 0 ? null : Display(r.S.LastError), problems, problems.Any(p => p.Code == ErrorCodes.EInvoiceWaitingForInvoice),
            r.S.SubmittedAt, r.S.CreatedAt, r.S.UpdatedAt, Convert.ToBase64String(r.S.RowVersion));
    }
}
