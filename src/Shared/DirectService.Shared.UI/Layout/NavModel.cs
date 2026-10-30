namespace DirectService.Shared.UI.Layout;

/// <summary>
/// A menu entry: text key, icon (DT-235: every entry has its own), route (relative to the company, or absolute when it starts with "/"), and
/// the permissions that show it (any of). <see cref="Part"/> is the labelled group of a long section it belongs to (DT-238).
/// </summary>
public sealed record NavItem(string Key, string Icon, string Route, params string[] AnyOf)
{
    public string? Part { get; init; }
}

/// <summary>A menu group of the PRD (UX-11) with its icon; a group with a route and no items is a single entry.</summary>
public sealed record NavGroup(string Key, string Icon, string? Route, IReadOnlyList<NavItem> Items, params string[] AnyOf);

/// <summary>
/// The side menu of the approved board (SCR-00): PRD groups in order; entries the user's role cannot use are hidden (the API still
/// checks every request — SEC-05). "Setup checklist" shows only while setup is incomplete and "Tenant" only for core.tenant.manage (DT-66).
/// Screen routes are relative to <c>/c/{company}/</c>.
/// </summary>
public static class NavModel
{
    public const string Setup = "setup";

    /// <summary>The company menu (groups in order), given by the browser app (Stage 2 session 15: the menu stays static until it is composed
    /// per module in Stage 4, DT-369).</summary>
    public static IReadOnlyList<NavGroup> Company { get; private set; } = [];

    /// <summary>Provider console menu (separate shell, DT-66), given by the browser app.</summary>
    public static IReadOnlyList<(string Key, string Route, string Icon)> Console { get; private set; } = [];

    /// <summary>Sets the menu once at start-up (the browser app's composition, <c>ClientComposition</c>).</summary>
    public static void Use(IReadOnlyList<NavGroup> company, IReadOnlyList<(string Key, string Route, string Icon)> console)
    {
        Company = company;
        Console = console;
    }

    /// <summary>Entries of one labelled group inside a long section (DT-238: sections of more than about eight entries are split).</summary>
    public static NavItem[] Part(string key, params NavItem[] items) => [.. items.Select(i => i with { Part = key })];

    public static bool Allowed(IReadOnlyCollection<string> granted, string[] anyOf) => anyOf.Length == 0 || anyOf.Any(granted.Contains);

    /// <summary>The menu for a user: groups and entries filtered by permission; the setup entry only while setup is incomplete.</summary>
    public static IReadOnlyList<NavGroup> For(IReadOnlyCollection<string> granted, bool setupIncomplete) =>
        Company
            .Where(g => Allowed(granted, g.AnyOf) && (g.Route != Setup || setupIncomplete))
            .Select(g => g with { Items = g.Items.Where(i => Allowed(granted, i.AnyOf)).ToList() })
            .Where(g => g.Route is not null || g.Items.Count > 0)
            .ToList();

    /// <summary>
    /// Whether the screen at a company address is open to these permissions (UX-13): the menu entry with the longest matching route decides
    /// ("trips/new" before "trips"); addresses without an entry are open (the API still checks every request).
    /// </summary>
    public static bool RouteAllowed(IReadOnlyCollection<string> granted, string relativeUrl)
    {
        ArgumentNullException.ThrowIfNull(relativeUrl);
        var path = relativeUrl.Split('?', '#')[0].Trim('/');
        if (!path.StartsWith("c/", StringComparison.Ordinal)) return true;
        var rest = path.Split('/', 3) is { Length: 3 } parts ? parts[2] : string.Empty;
        var entry = Company.SelectMany(g => g.Items)
            .Where(i => !i.Route.StartsWith('/') && (rest == i.Route || rest.StartsWith(i.Route + "/", StringComparison.Ordinal)))
            .OrderByDescending(i => i.Route.Length).FirstOrDefault();
        return entry is null || Allowed(granted, entry.AnyOf);
    }

    /// <summary>The full path of a route for a company token.</summary>
    public static string Href(string company, string route) => route.StartsWith('/') ? route : route.Length == 0 ? $"/c/{company}" : $"/c/{company}/{route}";
}
