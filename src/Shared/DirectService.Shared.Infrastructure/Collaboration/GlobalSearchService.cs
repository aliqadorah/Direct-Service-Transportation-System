namespace DirectService.Shared.Infrastructure.Collaboration;

/// <summary>
/// Global search (FR-187, Ctrl+K) within the selected company: the engine cleans the text (ASCII digits, at most 100 characters), folds the
/// Arabic spelling variants (hamza forms of alef, taa marbuta, alef maqsura; diacritics and case by the collation) and asks every module's
/// <see cref="ISearchProvider"/>; each lists only the record types the caller may view (DT-118), at most <see cref="PerType"/> hits per type.
/// A shared engine since Stage 2 session 6 (DT-313).
/// </summary>
public sealed class GlobalSearchService(IEnumerable<ISearchProvider> providers)
{
    public const int PerType = 5;

    public async Task<SearchResponse> SearchAsync(string? q, CancellationToken ct)
    {
        var text = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(q));
        if (text is null || text.Length < 2) throw Fail.Field("q", SharedErrorCodes.ValidationRange, new FieldRef("q"));
        if (text.Length > 100) text = text[..100];
        var query = new SearchQuery(text, SqlText.FoldText(text), TextNormalizer.Compact(text) ?? text, PerType);
        var hits = new List<SearchHit>();
        foreach (var provider in providers)
            hits.AddRange(await provider.SearchAsync(query, ct).ConfigureAwait(false));
        return new SearchResponse(text, hits);
    }
}
