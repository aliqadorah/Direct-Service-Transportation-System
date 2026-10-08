using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Cfg;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// Standard-field configuration (FR-182, settings.manage): rename, hide, require and reorder built-in fields of trips, drivers and
/// customers. Core trip fields can be renamed but never hidden; fields required by the business rules cannot be hidden or made
/// optional (design rule on cfg.StandardFieldConfigs, DT-43). Also builds the configured form (FR-180, FR-184 preview) and the
/// <see cref="FieldRules"/> the record services apply when saving.
/// </summary>
public sealed class StandardFieldService(IAppDbContext db, IRequestContext ctx, CustomFieldService customFields)
{
    private readonly Dictionary<string, FieldRules> _rules = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<StandardFieldResponse>> ListAsync(string entityType, CancellationToken ct)
    {
        EnsureEntity(entityType);
        var overrides = await OverridesAsync(entityType, tracked: false, ct).ConfigureAwait(false);
        return FieldCatalog.For(entityType).Select((f, i) =>
        {
            var o = overrides.GetValueOrDefault(f.Key);
            return new StandardFieldResponse(f.Key, f.LabelAr, f.LabelEn, o?.Label, f.IsCore, f.AlwaysRequired,
                o?.IsHidden ?? false, f.AlwaysRequired || (o?.IsRequired ?? false), o?.SortOrder ?? (i + 1) * 10);
        }).OrderBy(f => f.SortOrder).ToList();
    }

    public async Task<IReadOnlyList<StandardFieldResponse>> UpdateAsync(string entityType, StandardFieldsRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        EnsureEntity(entityType);
        var changes = r.Fields ?? [];
        if (changes.Count > FieldCatalog.For(entityType).Count) throw Fail.Field("fields", ErrorCodes.ValidationRange, "fields");
        var errors = new List<FieldError>();
        foreach (var c in changes)
        {
            var f = FieldCatalog.Find(entityType, c.FieldKey);
            if (f is null)
            {
                errors.Add(new FieldError("fields", ErrorCodes.FieldUnknown, [c.FieldKey]));
                continue;
            }
            var label = Common.TextNormalizer.CleanText(c.Label);
            if (label is { Length: > 100 }) errors.Add(new FieldError(f.Key, ErrorCodes.ValidationRange, [f.Key]));
            // An always-required field stays required whatever IsRequired says; only hiding it is refused.
            if (c.IsHidden && f.IsCore) throw Fail.RuleOn(f.Key, ErrorCodes.FieldCore, label ?? DefaultLabel(f));
            if (c.IsHidden && f.AlwaysRequired) throw Fail.RuleOn(f.Key, ErrorCodes.FieldAlwaysRequired, label ?? DefaultLabel(f));
            if (c.IsHidden && FieldCatalog.IsConditional(entityType, f.Key)) throw Fail.RuleOn(f.Key, ErrorCodes.FieldConditional, label ?? DefaultLabel(f));
            if (c.IsRequired && FieldCatalog.CannotRequire(entityType, f.Key)) throw Fail.RuleOn(f.Key, ErrorCodes.FieldCannotRequire, label ?? DefaultLabel(f));
        }
        if (changes.Select(c => c.FieldKey).Distinct(StringComparer.Ordinal).Count() != changes.Count)
            errors.Add(new FieldError("fields", ErrorCodes.ImportDuplicate, ["fieldKey"]));
        if (errors.Count > 0) throw Fail.Fields(errors);

        var overrides = await OverridesAsync(entityType, tracked: true, ct).ConfigureAwait(false);
        var catalog = FieldCatalog.For(entityType);
        foreach (var c in changes)
        {
            var f = FieldCatalog.Find(entityType, c.FieldKey)!;
            if (!overrides.TryGetValue(f.Key, out var o))
            {
                o = new StandardFieldConfig { EntityType = entityType, FieldKey = f.Key };
                db.Set<StandardFieldConfig>().Add(o);
            }
            o.Label = Common.TextNormalizer.CleanText(c.Label);
            o.IsHidden = c.IsHidden;
            o.IsRequired = f.AlwaysRequired || c.IsRequired;
            o.SortOrder = c.SortOrder ?? o.SortOrder;
            if (o.SortOrder == 0) o.SortOrder = (catalog.ToList().IndexOf(f) + 1) * 10; // default position until reordered
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _rules.Remove(entityType);
        return await ListAsync(entityType, ct).ConfigureAwait(false);
    }

    /// <summary>The configured form of an entity (FR-180, FR-184): standard and custom fields in display order.</summary>
    public async Task<FormConfigResponse> FormAsync(string entityType, bool includeHidden, CancellationToken ct)
    {
        EnsureEntity(entityType);
        var standard = await ListAsync(entityType, ct).ConfigureAwait(false);
        var custom = await customFields.DefinitionsAsync(entityType, ct).ConfigureAwait(false);
        var en = ctx.Language == "en";
        var fields = standard.Select(f => new FormField(f.FieldKey, false, f.Label ?? (en ? f.DefaultLabelEn : f.DefaultLabelAr), f.IsRequired,
                f.IsHidden, true, f.SortOrder, null, null, null, null))
            .Concat(custom.Select(d => new FormField(d.FieldKey, true, customFields.Label(d), d.IsRequired, d.IsHidden, d.ShowAsColumn,
                d.SortOrder, d.FieldType, CustomFieldService.ReadOptions(d.Options), d.DefaultValue, d.HelpText)))
            .Where(f => includeHidden || !f.IsHidden)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.IsCustom)
            .ToList();
        return new FormConfigResponse(entityType, fields);
    }

    /// <summary>The save rules of an entity for this request (labels and required flags).</summary>
    public async Task<FieldRules> RulesAsync(string entityType, CancellationToken ct)
    {
        if (_rules.TryGetValue(entityType, out var rules)) return rules;
        var overrides = await OverridesAsync(entityType, tracked: false, ct).ConfigureAwait(false);
        rules = new FieldRules(entityType, overrides, ctx.Language == "en");
        _rules[entityType] = rules;
        return rules;
    }

    private async Task<Dictionary<string, StandardFieldConfig>> OverridesAsync(string entityType, bool tracked, CancellationToken ct)
    {
        var q = db.Set<StandardFieldConfig>().Where(s => s.EntityType == entityType);
        if (!tracked) q = q.AsNoTracking();
        return await q.ToDictionaryAsync(s => s.FieldKey, StringComparer.Ordinal, ct).ConfigureAwait(false);
    }

    private string DefaultLabel(StandardField f) => ctx.Language == "en" ? f.LabelEn : f.LabelAr;

    private static void EnsureEntity(string entityType)
    {
        if (!FieldCatalog.IsEntity(entityType)) throw Fail.NotFound();
    }
}

/// <summary>Labels and required flags of an entity's standard fields as configured by the company (FR-182).</summary>
public sealed class FieldRules
{
    private readonly string _entityType;
    private readonly IReadOnlyDictionary<string, StandardFieldConfig> _overrides;
    private readonly bool _english;

    internal FieldRules(string entityType, IReadOnlyDictionary<string, StandardFieldConfig> overrides, bool english)
    {
        _entityType = entityType;
        _overrides = overrides;
        _english = english;
    }

    /// <summary>The label shown to the user: the company's replacement, else the default in the user's language.</summary>
    public string Label(string key)
    {
        var f = FieldCatalog.Find(_entityType, key);
        if (_overrides.TryGetValue(key, out var o) && o.Label is not null) return o.Label;
        return f is null ? key : _english ? f.LabelEn : f.LabelAr;
    }

    /// <summary>Hidden by the company: the form does not show it, so saves keep the stored value (review M1).</summary>
    public bool IsHidden(string key) => _overrides.TryGetValue(key, out var o) && o.IsHidden;

    /// <summary>The value to save for an optional field: the stored one while the field is hidden on an existing record.</summary>
    public T Keep<T>(string key, bool isNew, T requested, T stored) => !isNew && IsHidden(key) ? stored : requested;

    /// <summary>Required by the business rules, or made required by the company (and not hidden).</summary>
    public bool IsRequired(string key)
    {
        var f = FieldCatalog.Find(_entityType, key);
        if (f?.AlwaysRequired == true) return true;
        return _overrides.TryGetValue(key, out var o) && o.IsRequired && !o.IsHidden;
    }

    /// <summary>Adds "Field «X» is required" (FR-182) when a required field is empty.</summary>
    public void Require(List<FieldError> errors, string key, bool isEmpty)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (isEmpty && IsRequired(key) && !errors.Any(e => e.Field == key))
            errors.Add(new FieldError(key, ErrorCodes.ValidationRequired, [Label(key)]));
    }

    /// <summary>Adds "Field «X» is required" when a rule makes the field required in this case (e.g. IBAN when paid by bank).</summary>
    public void RequireWhen(List<FieldError> errors, string key, bool missing)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (missing && !errors.Any(e => e.Field == key)) errors.Add(new FieldError(key, ErrorCodes.ValidationRequired, [Label(key)]));
    }

    /// <summary>Adds a format/range problem for a field, named by its label.</summary>
    public void Invalid(List<FieldError> errors, string key, string code = ErrorCodes.ValidationFormat)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (!errors.Any(e => e.Field == key)) errors.Add(new FieldError(key, code, [Label(key)]));
    }
}
