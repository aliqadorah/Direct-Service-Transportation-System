using System.Globalization;

namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// State of a server-paged list (API-04): search text, saved view (status), sort (<c>field</c> / <c>-field</c>), page, page size and the
/// filter-row values; builds the query string. Filters are sent as-is under their keys (each list's API names them).
/// </summary>
public sealed class ListQuery
{
    public string? Q { get; set; }
    public string View { get; set; } = "active";
    public string? Sort { get; set; }
    public bool Desc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public Dictionary<string, string> Filters { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Extra parameters fixed by the screen (e.g. a period), sent with every request.</summary>
    public Dictionary<string, string> Fixed { get; } = new(StringComparer.Ordinal);

    public string? SortParam => Sort is null ? null : (Desc ? "-" : "") + Sort;

    /// <summary>Query string for the list; <paramref name="statusParam"/> names the status filter (null = none).</summary>
    public string ToQuery(string? statusParam = "status", bool paged = true)
    {
        var p = new List<string>();
        void Add(string k, string? v) { if (!string.IsNullOrWhiteSpace(v)) p.Add(Uri.EscapeDataString(k) + "=" + Uri.EscapeDataString(v)); }
        Add("q", Q);
        if (statusParam is not null) Add(statusParam, View);
        Add("sort", SortParam);
        if (paged)
        {
            Add("page", Page.ToString(CultureInfo.InvariantCulture));
            Add("pageSize", PageSize.ToString(CultureInfo.InvariantCulture));
        }
        foreach (var (k, v) in Fixed) Add(k, v);
        foreach (var (k, v) in Filters) Add(k, v);
        return p.Count == 0 ? string.Empty : "?" + string.Join('&', p);
    }

    public void SetSort((string Key, bool Desc) s)
    {
        Sort = s.Key;
        Desc = s.Desc;
        Page = 1;
    }
}
