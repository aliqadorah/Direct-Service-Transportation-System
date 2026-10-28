using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

/// <summary>
/// The text of a global search (FR-187) as the engine prepared it: <paramref name="Text"/> cleaned (ASCII digits, at most 100 characters),
/// <paramref name="Folded"/> with the Arabic spelling variants folded (<see cref="SqlText.FoldText"/>), <paramref name="Compact"/> without
/// spaces or dashes (codes, phones); at most <paramref name="PerType"/> hits per record type.
/// </summary>
public sealed record SearchQuery(string Text, string Folded, string Compact, int PerType);

/// <summary>
/// A module's records in the global search (design a.3). Each provider returns only the record types the caller may view, in its own order;
/// the engine lists the providers' hits in registration order (Stage 2 session 6, DT-313).
/// </summary>
public interface ISearchProvider
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQuery query, CancellationToken ct);
}
