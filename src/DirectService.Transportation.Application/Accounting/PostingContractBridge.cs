using DirectService.Accounting.Domain.Acc;
using Microsoft.Extensions.DependencyInjection;
using Contract = DirectService.Accounting.Contracts.Posting;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// A handler of the contract (<see cref="Contract.IPostingSourceHandler"/>) seen by today's pending postings and source links
/// (<see cref="PostingSources"/>), so a module's handler works with the retry before the document handlers move to the contract (Stage 3
/// session 2; it goes when they have, session 4b).
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

/// <summary>The transport module's posting keys (design c, E-12): the TRN keys of <see cref="PostingKeys"/>; the ACC keys are Accounting's.</summary>
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
