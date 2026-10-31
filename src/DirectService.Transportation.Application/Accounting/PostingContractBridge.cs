using Contract = DirectService.Accounting.Contracts.Posting;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// The engine's request of a document still built by its service (receipts, advances, settlements, partner payments, AP/AR) as the contract's
/// document, for its <see cref="Contract.IPostingSourceHandler"/> (Stage 3 session 4b; goes when those documents switch, session 5).
/// </summary>
public static class PostingRequests
{
    public static Contract.PostingDocument ToDocument(this PostingRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (r.IsManual || r.SourceType is null || r.SourceId is null) throw new InvalidOperationException("Only an automatic entry of a document has a source handler.");
        return new(new Contract.DocumentRef(r.SourceModule, r.SourceType, r.SourceId.Value), r.EntryDate, r.Description, r.Reference,
            r.Lines.Select(l => new Contract.PostingLineSpec(l.PostingKey, l.AccountId, l.Debit, l.Credit, l.CostCentreId,
                l.Party is { } p ? new Contract.PartyRef(p.Module, p.Type, p.Id) : null, l.Note, l.Reference, l.TaxCodeId, l.TaxAccountId)).ToList());
    }
}

/// <summary>The transport module's posting keys (design c, E-12): the TRN keys of <see cref="PostingKeys"/>; the ACC keys are Accounting's.</summary>
public sealed class ProductPostingKeys(string productCode) : Contract.IPostingKeyProvider
{
    public string ProductCode => productCode;

    public IReadOnlyList<Contract.PostingKeyDef> Keys { get; } = PostingKeys.All.Where(d => d.Key.StartsWith(productCode + ".", StringComparison.Ordinal))
        .Select(d => new Contract.PostingKeyDef(d.Key, d.NameAr, d.NameEn, d.RequiredOnlyDuringSetup, d.Optional, ChartTemplate.Mapping.GetValueOrDefault(d.Key)))
        .ToList();
}
