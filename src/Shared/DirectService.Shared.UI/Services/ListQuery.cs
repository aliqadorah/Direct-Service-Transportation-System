using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace DirectService.Shared.UI.Services;

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

/// <summary>
/// Filtered lists keep their state in the address (UX-12): a filtered list can be bookmarked, shared and restored with Back.
/// <see cref="Write"/> replaces the current history entry (no new entry per filter change); <see cref="Read"/> restores on load.
/// </summary>
public static class ListUrl
{
    private const string Day = "yyyy-MM-dd";

    /// <summary>The address's query values (lists read them on first load).</summary>
    public static System.Collections.Specialized.NameValueCollection Read(NavigationManager nav) =>
        System.Web.HttpUtility.ParseQueryString(new Uri(nav.Uri).Query);

    /// <summary>Restores search, sort and page from the address.</summary>
    public static void ReadInto(this ListQuery q, System.Collections.Specialized.NameValueCollection query)
    {
        if (query["q"] is { Length: > 0 } text) q.Q = text;
        if (query["sort"] is { Length: > 1 } sort) { q.Desc = sort[0] == '-'; q.Sort = q.Desc ? sort[1..] : sort; }
        if (int.TryParse(query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out var page) && page > 1) q.Page = page;
    }

    public static DateOnly? Date(string? value) =>
        DateOnly.TryParseExact(value, Day, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string? Date(DateOnly? value) => value?.ToString(Day, CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes search, sort, page and the screen's own values into the address (null or empty removes a key; keys not named are kept).
    /// Does nothing when the address already says so.
    /// </summary>
    public static void Write(NavigationManager nav, ListQuery q, params (string Key, string? Value)[] values)
    {
        var p = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["q"] = q.Q,
            ["sort"] = q.SortParam,
            ["page"] = q.Page > 1 ? q.Page.ToString(CultureInfo.InvariantCulture) : null,
        };
        foreach (var (k, v) in values) p[k] = string.IsNullOrEmpty(v) ? null : v;
        var uri = nav.GetUriWithQueryParameters(p);
        if (string.Equals(uri, nav.Uri, StringComparison.Ordinal)) return;
        LastWritten = uri;
        nav.NavigateTo(uri, replace: true);
    }

    /// <summary>
    /// The address a list last wrote for itself: the layout keeps the page on that navigation, while any other move to the same path with a
    /// different query (a menu link, a drill-down) starts the page afresh so the new filters apply.
    /// </summary>
    public static string? LastWritten { get; private set; }
}
