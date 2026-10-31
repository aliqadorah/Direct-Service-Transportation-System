using DirectService.Accounting.Contracts.Posting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>Pending-posting retries and journal links for approved invoices (FR-101), a handler of the posting contract (Stage 3 session 4b).</summary>
public sealed class InvoicePostingHandler(IAppDbContext db, InvoiceService invoices, IUrlTokenService tokens) : IPostingSourceHandler
{
    public string Module => AccountingCodes.ModuleTrn;
    public string Type => InvoiceService.SourceType;

    public async Task<PostingDocument?> RebuildAsync(long sourceId, CancellationToken ct)
    {
        var inv = await db.Set<Invoice>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        return inv is null || inv.JournalEntryId is not null ? null : invoices.BuildEntry(inv);
    }

    public async Task AttachAsync(long sourceId, long journalEntryId, CancellationToken ct)
    {
        var inv = await db.Set<Invoice>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        // A retry racing a void must not leave an entry on a void invoice or a second entry.
        if (inv.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.InvoiceIsVoid, inv.Number);
        if (inv.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        inv.JournalEntryId = journalEntryId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, PostingSourceInfo>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<Invoice>().AsNoTracking().Where(i => sourceIds.Contains(i.Id)).Select(i => new { i.Id, i.PublicId, i.Number }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(i => i.Id, i => new PostingSourceInfo(i.Number!, tokens.Encode<Invoice>(i.PublicId)));   // only approved invoices post: numbered
}

/// <summary>Pending-posting retries and journal links for credit notes (FR-101), a handler of the posting contract (Stage 3 session 4b).</summary>
public sealed class CreditNotePostingHandler(IAppDbContext db, CreditNoteService credits, IUrlTokenService tokens) : IPostingSourceHandler
{
    public string Module => AccountingCodes.ModuleTrn;
    public string Type => CreditNoteService.SourceType;

    public async Task<PostingDocument?> RebuildAsync(long sourceId, CancellationToken ct)
    {
        var cn = await db.Set<CreditNote>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        if (cn is null || cn.JournalEntryId is not null) return null;
        var inv = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == cn.InvoiceId, ct).ConfigureAwait(false);
        return credits.BuildEntry(cn, inv);
    }

    public async Task AttachAsync(long sourceId, long journalEntryId, CancellationToken ct)
    {
        var cn = await db.Set<CreditNote>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        if (cn.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.CreditIsVoid, cn.Number);
        if (cn.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        cn.JournalEntryId = journalEntryId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, PostingSourceInfo>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<CreditNote>().AsNoTracking().Where(c => sourceIds.Contains(c.Id)).Select(c => new { c.Id, c.PublicId, c.Number }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(c => c.Id, c => new PostingSourceInfo(c.Number, tokens.Encode<CreditNote>(c.PublicId)));
}
