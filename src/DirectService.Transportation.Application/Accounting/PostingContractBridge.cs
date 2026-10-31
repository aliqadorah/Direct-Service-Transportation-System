using Contract = DirectService.Accounting.Contracts.Posting;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>The entry of a document for the posting contract (Stage 3 session 5): the source is the document's module, type and id.</summary>
public static class PostingEntry
{
    public static Contract.PostingDocument Of(DateOnly date, string description, string module, string type, long id, IReadOnlyList<Contract.PostingLineSpec> lines) =>
        new(new Contract.DocumentRef(module, type, id), date, description, null, lines);
}

/// <summary>The transport module's posting keys (design c, E-12): the TRN keys of <see cref="PostingKeys"/>; the ACC keys are Accounting's.</summary>
public sealed class ProductPostingKeys(string productCode) : Contract.IPostingKeyProvider
{
    public string ProductCode => productCode;

    public IReadOnlyList<Contract.PostingKeyDef> Keys { get; } = PostingKeys.All.Where(d => d.Key.StartsWith(productCode + ".", StringComparison.Ordinal))
        .Select(d => new Contract.PostingKeyDef(d.Key, d.NameAr, d.NameEn, d.RequiredOnlyDuringSetup, d.Optional, d.TemplateAccountCode))
        .ToList();
}
