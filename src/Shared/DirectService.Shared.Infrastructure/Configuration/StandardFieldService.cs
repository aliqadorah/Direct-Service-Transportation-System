using Microsoft.EntityFrameworkCore;

namespace DirectService.Shared.Infrastructure.Configuration;

/// <summary>
/// Standard-field configuration (FR-182, settings.manage): rename, hide, require and reorder built-in fields of trips, drivers and
/// customers. Core trip fields can be renamed but never hidden; fields required by the business rules cannot be hidden or made
/// optional (design rule on cfg.StandardFieldConfigs, DT-43). Also builds the configured form (FR-180, FR-184 preview) and the
/// <see cref="FieldRules"/> the record services apply when saving (<see cref="IStandardFields"/>). A shared engine: the entities and their
/// built-in fields come from the modules (<see cref="FieldCatalogs"/>, Stage 2 session 7, DT-318).
/// </summary>
public sealed class StandardFieldService(IAppDbContext db, IRequestContext ctx, CustomFieldService customFields, FieldCatalogs catalog) : IStandardFields
{
    private readonly Dictionary<string, FieldRules> _rules = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<StandardFieldResponse>> ListAsync(string entityType, CancellationToken ct)
    {
        EnsureEntity(entityType);
        var overrides = await OverridesAsync(entityType, tracked: false, ct).ConfigureAwait(false);
        return catalog.For(entityType).Select((f, i) =>
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
        if (changes.Count > catalog.For(entityType).Count) throw Fail.Field("fields", SharedErrorCodes.ValidationRange, new FieldRef("fields"));
        var errors = new List<FieldError>();
        foreach (var c in changes)
        {
            var f = catalog.Find(entityType, c.FieldKey);
            if (f is null)
            {
                errors.Add(new FieldError("fields", SharedErrorCodes.FieldUnknown, [c.FieldKey]));
                continue;
            }
            var label = TextNormalizer.CleanText(c.Label);
            if (label is { Length: > 100 }) errors.Add(new FieldError(f.Key, SharedErrorCodes.ValidationRange, [new FieldRef(f.Key)]));
            // An always-required field stays required whatever IsRequired says; only hiding it is refused.
            if (c.IsHidden && f.IsCore) throw Fail.RuleOn(f.Key, SharedErrorCodes.FieldCore, label ?? DefaultLabel(f));
            if (c.IsHidden && f.AlwaysRequired) throw Fail.RuleOn(f.Key, SharedErrorCodes.FieldAlwaysRequired, label ?? DefaultLabel(f));
            if (c.IsHidden && catalog.IsConditional(entityType, f.Key)) throw Fail.RuleOn(f.Key, SharedErrorCodes.FieldConditional, label ?? DefaultLabel(f));
            if (c.IsRequired && catalog.CannotRequire(entityType, f.Key)) throw Fail.RuleOn(f.Key, SharedErrorCodes.FieldCannotRequire, label ?? DefaultLabel(f));
        }
        if (changes.Select(c => c.FieldKey).Distinct(StringComparer.Ordinal).Count() != changes.Count)
            errors.Add(new FieldError("fields", SharedErrorCodes.ImportDuplicate, [new FieldRef("fieldKey")]));
        if (errors.Count > 0) throw Fail.Fields(errors);

        var overrides = await OverridesAsync(entityType, tracked: true, ct).ConfigureAwait(false);
        var builtIn = catalog.For(entityType);
        foreach (var c in changes)
        {
            var f = catalog.Find(entityType, c.FieldKey)!;
            if (!overrides.TryGetValue(f.Key, out var o))
            {
                o = new StandardFieldConfig { EntityType = entityType, FieldKey = f.Key };
                db.Set<StandardFieldConfig>().Add(o);
            }
            o.Label = TextNormalizer.CleanText(c.Label);
            o.IsHidden = c.IsHidden;
            o.IsRequired = f.AlwaysRequired || c.IsRequired;
            o.SortOrder = c.SortOrder ?? o.SortOrder;
            if (o.SortOrder == 0) o.SortOrder = (builtIn.ToList().IndexOf(f) + 1) * 10; // default position until reordered
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
                d.SortOrder, d.FieldType, CustomFieldValues.ReadOptions(d.Options), d.DefaultValue, d.HelpText)))
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
        rules = new FieldRules(catalog.For(entityType), overrides, ctx.Language == "en");
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

    private void EnsureEntity(string entityType)
    {
        if (!catalog.IsEntity(entityType)) throw Fail.NotFound();
    }
}
