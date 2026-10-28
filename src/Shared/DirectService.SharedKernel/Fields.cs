using System.Globalization;
using System.Text.Json;
using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

// Custom and standard fields (FR-179 … FR-184): the engine is in Shared.Infrastructure; modules give their configurable entities through
// IFieldCatalogProvider and read the rules through IStandardFields / ICustomFields (Stage 2 session 7, DT-318).

/// <summary>A built-in field of a configurable entity (FR-182).</summary>
/// <param name="Key">Field key used in requests, cfg.StandardFieldConfigs.FieldKey and column views.</param>
/// <param name="IsCore">Core trip field (US-27): renamable, never hidden.</param>
/// <param name="AlwaysRequired">Required by the business rules (PRD 7.1.1, 7.8.1): cannot be hidden or made optional (design rule).</param>
public sealed record StandardField(string Key, string LabelAr, string LabelEn, bool IsCore, bool AlwaysRequired);

/// <summary>A module's configurable entities (design a.3): their stored type names (E-11), built-in fields and stored custom values.</summary>
public interface IFieldCatalogProvider
{
    /// <summary>The entity types (stored names, e.g. <c>TRN.Trip</c>) in display order.</summary>
    IReadOnlyList<string> Entities { get; }

    /// <summary>The built-in fields of one of <see cref="Entities"/>, in default display order.</summary>
    IReadOnlyList<StandardField> For(string entityType);

    /// <summary>An optional field a business rule requires in some cases: it cannot be hidden.</summary>
    bool IsConditional(string entityType, string key);

    /// <summary>An optional field a business rule forbids in some cases: never made always required.</summary>
    bool CannotRequire(string entityType, string key);

    /// <summary>The CustomValues JSON column of the entity's records (soft-deleted rows filtered), for the "has data" checks.</summary>
    IQueryable<string?> CustomValues(string entityType);
}

/// <summary>The company's standard-field rules of an entity for this request (labels, required, hidden).</summary>
public interface IStandardFields
{
    Task<FieldRules> RulesAsync(string entityType, CancellationToken ct);
}

/// <summary>The company's custom fields of an entity and the validation of the values a save sends (FR-180).</summary>
public interface ICustomFields
{
    /// <summary>The company's definitions of an entity (cached for the request).</summary>
    Task<IReadOnlyList<CustomFieldDefinition>> DefinitionsAsync(string entityType, CancellationToken ct);

    /// <summary>
    /// Validates the values of a save against the definitions and returns the JSON to store (null when empty). Unknown keys are refused;
    /// hidden fields keep their stored values; defaults fill absent fields on create; required visible fields must have a value.
    /// </summary>
    Task<string?> ValidateValuesAsync(string entityType, IReadOnlyDictionary<string, JsonElement>? input, string? storedJson, bool isNew,
        List<FieldError> errors, CancellationToken ct);

    /// <summary>The field's label in the reader's language.</summary>
    string Label(CustomFieldDefinition d);
}

/// <summary>Labels and required flags of an entity's standard fields as configured by the company (FR-182).</summary>
public sealed class FieldRules
{
    private readonly IReadOnlyList<StandardField> _fields;
    private readonly IReadOnlyDictionary<string, StandardFieldConfig> _overrides;
    private readonly bool _english;

    public FieldRules(IReadOnlyList<StandardField> fields, IReadOnlyDictionary<string, StandardFieldConfig> overrides, bool english)
    {
        _fields = fields;
        _overrides = overrides;
        _english = english;
    }

    private StandardField? Find(string key) => _fields.FirstOrDefault(f => f.Key == key);

    /// <summary>The label shown to the user: the company's replacement, else the default in the user's language.</summary>
    public string Label(string key)
    {
        var f = Find(key);
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
        var f = Find(key);
        if (f?.AlwaysRequired == true) return true;
        return _overrides.TryGetValue(key, out var o) && o.IsRequired && !o.IsHidden;
    }

    /// <summary>Adds "Field «X» is required" (FR-182) when a required field is empty.</summary>
    public void Require(List<FieldError> errors, string key, bool isEmpty)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (isEmpty && IsRequired(key) && !errors.Any(e => e.Field == key))
            errors.Add(new FieldError(key, SharedErrorCodes.ValidationRequired, [Label(key)]));
    }

    /// <summary>Adds "Field «X» is required" when a rule makes the field required in this case (e.g. IBAN when paid by bank).</summary>
    public void RequireWhen(List<FieldError> errors, string key, bool missing)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (missing && !errors.Any(e => e.Field == key)) errors.Add(new FieldError(key, SharedErrorCodes.ValidationRequired, [Label(key)]));
    }

    /// <summary>Adds a format/range problem for a field, named by its label.</summary>
    public void Invalid(List<FieldError> errors, string key, string code = SharedErrorCodes.ValidationFormat)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (!errors.Any(e => e.Field == key)) errors.Add(new FieldError(key, code, [Label(key)]));
    }
}

/// <summary>Custom field values: parsing import cells, normalising request values, reading stored JSON, text for exports and history.</summary>
public static class CustomFieldValues
{
    private const int TextMax = 200;
    private const int LongTextMax = 4000;

    /// <summary>Parses an import cell (text) into a value of the field's type; empty text gives null.</summary>
    public static bool TryParseCell(CustomFieldDefinition d, string? text, out JsonElement? value)
    {
        ArgumentNullException.ThrowIfNull(d);
        value = null;
        var clean = TextNormalizer.CleanText(text);
        if (clean is null) return true;
        switch (d.FieldType)
        {
            case CustomFieldTypes.Number:
                var n = TextNormalizer.ToAsciiDigits(clean).Replace(",", string.Empty, StringComparison.Ordinal).Replace("٫", ".", StringComparison.Ordinal);
                if (!decimal.TryParse(n, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)) return false;
                value = JsonSerializer.SerializeToElement(number);
                return true;
            case CustomFieldTypes.Date:
                if (!ImportParse.TryDate(clean, out var date)) return false;
                value = JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;
            case CustomFieldTypes.Checkbox:
                var b = ImportParse.Bool(clean);
                if (b is null) return false;
                value = JsonSerializer.SerializeToElement(b.Value);
                return true;
            default:
                if (!TryNormalise(d, JsonSerializer.SerializeToElement(clean), out var v)) return false;
                value = v;
                return true;
        }
    }

    /// <summary>A request value normalised to the field's type (text cleaned, numbers, yyyy-MM-dd dates, list options, true/false).</summary>
    public static bool TryNormalise(CustomFieldDefinition d, JsonElement given, out JsonElement value)
    {
        ArgumentNullException.ThrowIfNull(d);
        value = default;
        switch (d.FieldType)
        {
            case CustomFieldTypes.Text or CustomFieldTypes.LongText:
                if (given.ValueKind != JsonValueKind.String) return false;
                var text = d.FieldType == CustomFieldTypes.Text ? TextNormalizer.CleanText(given.GetString()) : given.GetString()?.Trim();
                if (text is null || text.Length > (d.FieldType == CustomFieldTypes.Text ? TextMax : LongTextMax)) return false;
                value = JsonSerializer.SerializeToElement(text);
                return true;
            case CustomFieldTypes.Number:
                if (given.ValueKind == JsonValueKind.Number && given.TryGetDecimal(out var number))
                {
                    value = JsonSerializer.SerializeToElement(number);
                    return true;
                }
                if (given.ValueKind != JsonValueKind.String || !TryParseCell(d, given.GetString(), out var parsed) || parsed is not JsonElement p) return false;
                value = p;
                return true;
            case CustomFieldTypes.Date:
                if (given.ValueKind != JsonValueKind.String
                    || !DateOnly.TryParseExact(TextNormalizer.ToAsciiDigits(given.GetString()).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return false;
                value = JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;
            case CustomFieldTypes.List:
                var option = given.ValueKind == JsonValueKind.String ? TextNormalizer.CleanText(given.GetString()) : null;
                if (option is null || !ReadOptions(d.Options).Contains(option, StringComparer.Ordinal)) return false;
                value = JsonSerializer.SerializeToElement(option);
                return true;
            case CustomFieldTypes.Checkbox:
                if (given.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                value = given.Clone();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The stored values of a record (empty when none).</summary>
    public static IReadOnlyDictionary<string, JsonElement> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, JsonElement>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? new Dictionary<string, JsonElement>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    /// <summary>A value as text for exports and history (dates yyyy-MM-dd, numbers invariant, checkbox true/false).</summary>
    public static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText(),
    };

    /// <summary>The options of a list field (empty when none or unreadable).</summary>
    public static IReadOnlyList<string> ReadOptions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
