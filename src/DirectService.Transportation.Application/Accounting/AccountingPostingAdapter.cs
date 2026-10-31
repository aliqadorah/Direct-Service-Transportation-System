using DirectService.Transportation.Application.Configuration;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Contract = DirectService.Accounting.Contracts.Posting;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The posting contract of design c (<see cref="Contract.IAccountingPosting"/>) over today's <see cref="PostingEngine"/> (Stage 3 session 2).
/// It adds only what the contract promises beyond the engine — idempotency per source and reversal by source — and changes nothing for the
/// documents, which still call the engine until sessions 4–5 switch them; session 4 moves the engine and this class to Accounting.Application.
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
        if (lockFirst) await numbering.LockAsync(DocumentTypes.JournalEntry, ct).ConfigureAwait(false);
        return await db.Set<JournalEntry>()
            .Where(e => e.SourceModule == source.Module && e.SourceType == source.Type && e.SourceId == source.Id
                && e.Status == AccountingCodes.Posted && e.ReversedByEntryId == null && e.ReversesEntryId == null)
            .OrderByDescending(e => e.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
    }

    private Contract.PostedEntry Posted(JournalEntry e) => new(e.Id, e.Number, tokens.Encode<JournalEntry>(e.PublicId));

    internal static PostingRequest Request(Contract.PostingDocument d) =>
        new(d.EntryDate, d.Description, d.Source.Module, d.Source.Type, d.Source.Id, false,
            d.Lines.Select(l => new PostingLine(l.MappingKey, l.AccountId, l.Debit, l.Credit, l.CostCentreId,
                l.Party is { } p ? new PostingParty(p.Module, p.Type, p.Id) : null, l.Note, l.Reference, l.TaxCodeId, l.TaxAccountId)).ToList(),
            d.Reference);

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Posting must run inside the document's transaction (NFR-04).");
    }
}

/// <summary>
/// A handler of the contract (<see cref="Contract.IPostingSourceHandler"/>) seen by today's pending postings and source links
/// (<see cref="PostingSources"/>), so a module's handler works with the retry before the engine moves (Stage 3 session 2).
/// </summary>
public sealed class ContractPostingSourceHandler(Contract.IPostingSourceHandler inner) : IPostingSourceHandler
{
    public string SourceModule => inner.Module;

    public string SourceType => inner.Type;

    public async Task<PostingRequest?> BuildAsync(long sourceId, CancellationToken ct) =>
        await inner.RebuildAsync(sourceId, ct).ConfigureAwait(false) is { } d ? AccountingPostingAdapter.Request(d) : null;

    public Task AttachAsync(long sourceId, JournalEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return inner.AttachAsync(sourceId, entry.Id, ct);
    }

    public async Task<IReadOnlyDictionary<long, Contracts.Accounting.SourceDocumentRef>> DescribeAsync(IReadOnlyCollection<long> sourceIds, CancellationToken ct) =>
        (await inner.DescribeAsync(sourceIds, ct).ConfigureAwait(false))
            .ToDictionary(x => x.Key, x => new Contracts.Accounting.SourceDocumentRef(inner.Module, inner.Type, x.Value.Token, x.Value.Number));
}

/// <summary>The posting keys of each product (design c, E-12): TRN by the transport module, ACC by Accounting (moves there in session 4).</summary>
public sealed class ProductPostingKeys(string productCode) : Contract.IPostingKeyProvider
{
    public string ProductCode => productCode;

    public IReadOnlyList<Contract.PostingKeyDef> Keys { get; } = PostingKeys.All.Where(d => d.Key.StartsWith(productCode + ".", StringComparison.Ordinal))
        .Select(d => new Contract.PostingKeyDef(d.Key, d.NameAr, d.NameEn, d.RequiredOnlyDuringSetup, d.Optional, ChartTemplate.Mapping.GetValueOrDefault(d.Key)))
        .ToList();
}

public static class PostingContractRegistration
{
    /// <summary>Registers a module's handler of the posting contract (and its bridge to today's pending postings).</summary>
    public static IServiceCollection AddPostingSourceHandler<THandler>(this IServiceCollection services) where THandler : class, Contract.IPostingSourceHandler
    {
        services.AddScoped<THandler>();
        services.AddScoped<Contract.IPostingSourceHandler>(sp => sp.GetRequiredService<THandler>());
        services.AddScoped<IPostingSourceHandler>(sp => new ContractPostingSourceHandler(sp.GetRequiredService<THandler>()));
        return services;
    }
}
