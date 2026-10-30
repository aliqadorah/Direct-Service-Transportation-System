namespace DirectService.Shared.UI.Layout;

/// <summary>
/// Screen addresses of records referenced by the API (design a.3, UI): search hits, document sources, history records, setup steps and
/// drill-downs. The browser app gives the resolver at start-up (Stage 2 session 15, DT-369); each module gives its own in Stage 4.
/// </summary>
public interface ILinkResolver
{
    string? ForSearchHit(string company, string type, string id);

    string? ForSource(string company, string? type, string? id);

    string? ForRecord(string company, string kind, string id);

    string ForSetupStep(string company, string link);

    string? ForDrill(string company, string type, string? id, IReadOnlyDictionary<string, string>? filters);
}

/// <summary>Screen addresses of records referenced by the API (search hits, sources, drill-downs), relative to the company.</summary>
public static class Links
{
    private static ILinkResolver? _resolver;

    /// <summary>Sets the resolver once at start-up (the browser app's composition, <c>ClientComposition</c>).</summary>
    public static void Use(ILinkResolver resolver) => _resolver = resolver;

    private static ILinkResolver Resolver => _resolver ?? throw new InvalidOperationException("The browser app has not set the link resolver (ClientComposition.Apply).");

    public static string? ForSearchHit(string company, string type, string id) => Resolver.ForSearchHit(company, type, id);

    /// <summary>The screen of the document that posted an entry (FR-097, FR-121), by its source type; null when it has no screen.</summary>
    public static string? ForSource(string company, string? type, string? id) => Resolver.ForSource(company, type, id);

    /// <summary>The screen of a record named by a history event (DT-188).</summary>
    public static string? ForRecord(string company, string kind, string id) => Resolver.ForRecord(company, kind, id);

    /// <summary>The screen of a setup-checklist step (the API names the area by its API path).</summary>
    public static string ForSetupStep(string company, string link) => Resolver.ForSetupStep(company, link);

    /// <summary>A drill-down link of the dashboard and reports (type, record, list filters) as a screen address with the filters in the query.</summary>
    public static string? ForDrill(string company, string type, string? id, IReadOnlyDictionary<string, string>? filters) => Resolver.ForDrill(company, type, id, filters);
}
