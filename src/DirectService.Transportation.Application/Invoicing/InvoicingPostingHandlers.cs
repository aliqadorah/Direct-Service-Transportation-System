using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Invoicing;

/// <summary>Pending-posting retries and journal links for approved invoices (FR-101).</summary>
public sealed class InvoicePostingHandler(IAppDbContext db, InvoiceService invoices, IUrlTokenService tokens) : IPostingSourceHandler
{
    public string SourceModule => AccountingCodes.ModuleTrn;
    public string SourceType => InvoiceService.SourceType;

    public async Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct)
    {
        var inv = await db.Set<Invoice>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        return inv is null || inv.JournalEntryId is not null ? null : invoices.BuildEntry(inv);
    }

    public async Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var inv = await db.Set<Invoice>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        // A retry racing a void must not leave an entry on a void invoice or a second entry.
        if (inv.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.InvoiceIsVoid, inv.Number);
        if (inv.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        inv.JournalEntryId = entry.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<Invoice>().AsNoTracking().Where(i => sourceIds.Contains(i.Id)).Select(i => new { i.Id, i.PublicId, i.Number }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(i => i.Id, i => new SourceDocumentRef(SourceModule, SourceType, tokens.Encode<Invoice>(i.PublicId), i.Number));
}

/// <summary>Pending-posting retries and journal links for credit notes (FR-101).</summary>
public sealed class CreditNotePostingHandler(IAppDbContext db, CreditNoteService credits, IUrlTokenService tokens) : IPostingSourceHandler
{
    public string SourceModule => AccountingCodes.ModuleTrn;
    public string SourceType => CreditNoteService.SourceType;

    public async Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct)
    {
        var cn = await db.Set<CreditNote>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        if (cn is null || cn.JournalEntryId is not null) return null;
        var inv = await db.Set<Invoice>().AsNoTracking().SingleAsync(i => i.Id == cn.InvoiceId, ct).ConfigureAwait(false);
        return credits.BuildEntry(cn, inv);
    }

    public async Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var cn = await db.Set<CreditNote>().SingleAsync(x => x.Id == sourceId, ct).ConfigureAwait(false);
        if (cn.Status == InvoiceCodes.Void) throw Fail.Rule(ErrorCodes.CreditIsVoid, cn.Number);
        if (cn.JournalEntryId is not null) throw new AppException(ErrorCodes.ConcurrencyConflict, 409);
        cn.JournalEntryId = entry.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await db.Set<CreditNote>().AsNoTracking().Where(c => sourceIds.Contains(c.Id)).Select(c => new { c.Id, c.PublicId, c.Number }).ToListAsync(ct).ConfigureAwait(false))
        .ToDictionary(c => c.Id, c => new SourceDocumentRef(SourceModule, SourceType, tokens.Encode<CreditNote>(c.PublicId), c.Number));
}
