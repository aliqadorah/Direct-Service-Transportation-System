namespace DirectService.Shared.Contracts;

/// <summary>Paged list response (API-04); shared by every module (Stage 2 session 10, DT-332).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Paging request; pageSize is capped at <see cref="MaxPageSize"/>.</summary>
public sealed record PageRequest(int Page = 1, int PageSize = 50)
{
    public const int MaxPageSize = 200;

    public int SafePage => Page < 1 ? 1 : Page;
    public int SafePageSize => PageSize is < 1 or > MaxPageSize ? (PageSize < 1 ? 50 : MaxPageSize) : PageSize;
    public int Skip => (SafePage - 1) * SafePageSize;
}

/// <summary>A related record shown by name, identified by its URL token.</summary>
public sealed record Ref(string Id, string Name);
