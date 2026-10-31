using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;
using Contract = DirectService.Accounting.Contracts.Posting;

namespace DirectService.Accounting.Application.Posting;

/// <summary>
/// The posting contract of design c (<see cref="Contract.IAccountingPosting"/>) over the <see cref="PostingEngine"/> (Stage 3 session 2; moved to
/// Accounting.Application with the engine in session 4a). It adds only what the contract promises beyond the engine — idempotency per source and
/// reversal by source.
/// </summary>
public sealed class AccountingPostingAdapter(IAppDbContext db, PostingEngine engine, INumberingService numbering, IUrlTokenService tokens) : Contract.IAccountingPosting
{
    public Task EnsureReadyAsync(string productCode, CancellationToken ct) =>
        // Today every company has the TRN and ACC keys; the check per product comes with the products of Stage 5 (design c).
        engine.EnsurePostingReadyAsync(ct);

    public async Task<Contract.PostedEntry?> PostOrPendAsync(Contract.PostingDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireTransaction();
        if (await CurrentAsync(document.Source, lockFirst: true, ct).ConfigureAwait(false) is { } existing) return Posted(existing);
        return await engine.PostOrPendAsync(Request(document), ct).ConfigureAwait(false) is { } entry ? Posted(entry) : null;
    }

    public async Task<Contract.PostedEntry> PostAsync(Contract.PostingDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        RequireTransaction();
        if (await CurrentAsync(document.Source, lockFirst: true, ct).ConfigureAwait(false) is { } existing) return Posted(existing);
        return Posted(await engine.PostAsync(Request(document), ct).ConfigureAwait(false));
    }

    public async Task<Contract.PostedEntry?> ReverseAsync(Contract.DocumentRef source, string description, string? voidReason, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        RequireTransaction();
        var original = await CurrentAsync(source, lockFirst: false, ct).ConfigureAwait(false);
        return original is null ? null : Posted(await engine.ReverseAsync(original, description, voidReason, ct).ConfigureAwait(false));
    }

    public Task<int> CurrencyDecimalsAsync(CancellationToken ct) => engine.CurrencyDecimalsAsync(ct);

    /// <summary>The source's posted, un-reversed entry (tracked). For a post, under the journal-number lock, so two posts of one document cannot both pass.</summary>
    private async Task<JournalEntry?> CurrentAsync(Contract.DocumentRef source, bool lockFirst, CancellationToken ct)
    {
        if (lockFirst) await numbering.LockAsync(AccountingDocumentTypes.JournalEntry, ct).ConfigureAwait(false);
        return await db.Set<JournalEntry>()
            .Where(e => e.SourceModule == source.Module && e.SourceType == source.Type && e.SourceId == source.Id
                && e.Status == AccountingCodes.Posted && e.ReversedByEntryId == null && e.ReversesEntryId == null)
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private Contract.PostedEntry Posted(JournalEntry e) => new(e.Id, e.Number, tokens.Encode<JournalEntry>(e.PublicId));

    /// <summary>The engine's request for a document of the contract (automatic, never manual).</summary>
    public static PostingRequest Request(Contract.PostingDocument d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new(d.EntryDate, d.Description, d.Source.Module, d.Source.Type, d.Source.Id, false,
            d.Lines.Select(l => new PostingLine(l.MappingKey, l.AccountId, l.Debit, l.Credit, l.CostCentreId,
                l.Party is { } p ? new PostingParty(p.Module, p.Type, p.Id) : null, l.Note, l.Reference, l.TaxCodeId, l.TaxAccountId)).ToList(),
            d.Reference);
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Posting must run inside the document's transaction (NFR-04).");
    }
}
