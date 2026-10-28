
namespace DirectService.Transportation.Web.Client.Services;

/// <summary>
/// The company's configured form of an entity (FR-179…184, <c>GET /forms/{entity}</c>): effective labels and required flags of the
/// standard fields, hidden fields, and the custom fields in order. Cached per company and entity for the session.
/// </summary>
public sealed class FormConfig(Api api, ShellState shell)
{
    public const string Trip = "TRN.Trip";
    public const string Driver = "TRN.Driver";
    public const string Customer = "TRN.Customer";

    private readonly Dictionary<string, FormConfigResponse> _cache = new(StringComparer.Ordinal);

    public async Task<FormView> GetAsync(string entity)
    {
        var key = (shell.Company?.Id ?? "") + "|" + entity;
        if (!_cache.TryGetValue(key, out var cfg))
        {
            cfg = await api.GetAsync<FormConfigResponse>(api.C("forms/" + Uri.EscapeDataString(entity))).ConfigureAwait(false);
            _cache[key] = cfg;
        }
        return new FormView(cfg);
    }

    /// <summary>Forgets cached forms after the field configuration changes (SCR-75).</summary>
    public void Invalidate() => _cache.Clear();
}

/// <summary>Read helpers over a configured form.</summary>
public sealed class FormView(FormConfigResponse config)
{
    private readonly Dictionary<string, FormField> _byKey = config.Fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);

    public static readonly FormView Empty = new(new FormConfigResponse("", []));

    /// <summary>The configured label of a standard field, or the given default.</summary>
    public string Label(string key, string fallback) => _byKey.TryGetValue(key, out var f) && !string.IsNullOrWhiteSpace(f.Label) ? f.Label : fallback;

    public bool Required(string key, bool fallback = false) => _byKey.TryGetValue(key, out var f) ? f.IsRequired : fallback;

    /// <summary>A standard field hidden by the company (never a core field) is not shown; its stored value is kept by the API.</summary>
    public bool Hidden(string key) => _byKey.TryGetValue(key, out var f) && f.IsHidden;

    public IReadOnlyList<FormField> Custom => config.Fields.Where(f => f.IsCustom && !f.IsHidden).OrderBy(f => f.SortOrder).ToList();

    public IReadOnlyList<FormField> CustomColumns => Custom.Where(f => f.ShowAsColumn).ToList();
}
