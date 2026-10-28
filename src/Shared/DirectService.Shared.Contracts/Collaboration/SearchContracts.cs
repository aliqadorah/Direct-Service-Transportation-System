namespace DirectService.Shared.Contracts;

// Global search (FR-187): the shared engine in Shared.Infrastructure, hits contributed by the modules (Stage 2 session 6, DT-313).

/// <summary>A search hit: record type (trip, invoice, driver …), its token, a title and a detail line.</summary>
public sealed record SearchHit(string Type, string Id, string Title, string? Detail);

/// <summary>Hits grouped by type, at most a few per type; only types the caller may view.</summary>
public sealed record SearchResponse(string Query, IReadOnlyList<SearchHit> Hits);
