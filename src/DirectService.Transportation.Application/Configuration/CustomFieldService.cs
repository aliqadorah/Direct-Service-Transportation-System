using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// Custom fields (FR-179 … FR-181, settings.manage) for trips, drivers and customers, and the validation of the values saved
/// in each record's CustomValues JSON (FR-180). Values are stored only when not empty (DT-41), so a key present in any
/// record means the field "has data": it can then only be hidden, its type is fixed and its used list options stay.
/// </summary>
public sealed partial class CustomFieldService(IAppDbContext db, IUrlTokenService tokens, IRequestContext ctx)
{
    public const int MaxFieldsPerEntity = 50;
    private const int TextMax = 200;
    private const int LongTextMax = 4000;

    private static readonly JsonSerializerOptions StoreJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    private readonly Dictionary<string, IReadOnlyList<CustomFieldDefinition>> _cache = new(StringComparer.Ordinal);

    // ============================================================ definitions

    public async Task<IReadOnlyList<CustomFieldResponse>> ListAsync(string entityType, CancellationToken ct)
    {
        EnsureEntity(entityType);
        var defs = await db.Set<CustomFieldDefinition>().AsNoTracking().Where(d => d.EntityType == entityType)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        var result = new List<CustomFieldResponse>(defs.Count);
        foreach (var d in defs) result.Add(ToResponse(d, await HasDataAsync(d.EntityType, d.FieldKey, ct).ConfigureAwait(false)));
        return result;
    }

    public async Task<(CustomFieldResponse Value, bool Created)> CreateAsync(string entityType, CustomFieldRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        EnsureEntity(entityType);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (clientId is Guid g && await db.Set<CustomFieldDefinition>().AsNoTracking().SingleOrDefaultAsync(d => d.PublicId == g, ct).ConfigureAwait(false) is { } again)
            return (ToResponse(again, await HasDataAsync(again.EntityType, again.FieldKey, ct).ConfigureAwait(false)), false);

        var key = r.FieldKey?.Trim();
        new Validator().Require("fieldKey", key).Check(key is null || FieldKeyPattern().IsMatch(key), "fieldKey").ThrowIfInvalid();
        if (FieldCatalog.Find(entityType, key) is not null || await db.Set<CustomFieldDefinition>()
                .AnyAsync(d => d.EntityType == entityType && d.FieldKey == key, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, key!);
        var count = await db.Set<CustomFieldDefinition>().CountAsync(d => d.EntityType == entityType, ct).ConfigureAwait(false);
        if (count >= MaxFieldsPerEntity) throw Fail.Field("fieldKey", ErrorCodes.ValidationRange, new FieldRef("fieldKey"));

        var d = new CustomFieldDefinition { EntityType = entityType, FieldKey = key!, SortOrder = r.SortOrder ?? (count + 1) * 10 };
        if (clientId is Guid id) d.PublicId = id;
        Apply(d, r, hasData: false);
        db.Set<CustomFieldDefinition>().Add(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (ToResponse(d, false), true);
    }

    public async Task<CustomFieldResponse> UpdateAsync(string token, CustomFieldRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var d = await FindAsync(token, ct).ConfigureAwait(false);
        if (r.FieldKey is not null && r.FieldKey.Trim() != d.FieldKey) throw Fail.Field("fieldKey", ErrorCodes.ValidationFormat, new FieldRef("fieldKey"));
        var hasData = await HasDataAsync(d.EntityType, d.FieldKey, ct).ConfigureAwait(false);
        var oldOptions = ReadOptions(d.Options);
        db.ExpectRowVersion(d, r.RowVersion);
        Apply(d, r, hasData);
        if (hasData && d.FieldType == CustomFieldTypes.List)
        {
            var kept = ReadOptions(d.Options).ToHashSet(StringComparer.Ordinal);
            foreach (var removed in oldOptions.Where(o => !kept.Contains(o)))
            {
                if (await AnyValueAsync(d.EntityType, d.FieldKey, v => v.ValueKind == JsonValueKind.String && v.GetString() == removed, ct).ConfigureAwait(false))
                    throw Fail.RuleOn("options", ErrorCodes.CustomFieldOptionInUse, removed);
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _cache.Remove(d.EntityType);
        return ToResponse(d, hasData);
    }

    /// <summary>Soft-deletes a field without data; a field with data can only be hidden (FR-181, flow 9.14).</summary>
    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var d = await FindAsync(token, ct).ConfigureAwait(false);
        if (await HasDataAsync(d.EntityType, d.FieldKey, ct).ConfigureAwait(false))
            throw Fail.Rule(ErrorCodes.CustomFieldHasData, Label(d));
        d.DeleteReason = MasterIds.Reason(reason);
        db.Set<CustomFieldDefinition>().Remove(d);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _cache.Remove(d.EntityType);
    }

    private void Apply(CustomFieldDefinition d, CustomFieldRequest r, bool hasData)
    {
        var labelAr = TextNormalizer.CleanText(r.LabelAr);
        var labelEn = TextNormalizer.CleanText(r.LabelEn);
        var type = CustomFieldTypes.All.FirstOrDefault(t => string.Equals(t, r.FieldType?.Trim(), StringComparison.OrdinalIgnoreCase));
        var help = TextNormalizer.CleanText(r.HelpText);
        var options = (r.Options ?? []).Select(TextNormalizer.CleanText).Where(o => o is not null).Select(o => o!).ToList();
        new Validator()
            .Require("labelAr", labelAr).MaxLength("labelAr", labelAr, 100).MaxLength("labelEn", labelEn, 100)
            .Require("fieldType", r.FieldType).Check(r.FieldType is null || type is not null, "fieldType")
            .MaxLength("helpText", help, 300)
            .Check(options.All(o => o.Length <= 100) && options.Count <= 200, "options", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        if (hasData && d.FieldType.Length > 0 && type != d.FieldType) throw Fail.RuleOn("fieldType", ErrorCodes.CustomFieldTypeFixed, Label(d));
        if (type == CustomFieldTypes.List && (options.Count == 0 || options.Distinct(StringComparer.Ordinal).Count() != options.Count))
            throw Fail.RuleOn("options", ErrorCodes.CustomFieldOptions, labelAr);

        d.LabelAr = labelAr!;
        d.LabelEn = labelEn;
        d.FieldType = type!;
        d.Options = type == CustomFieldTypes.List ? JsonSerializer.Serialize(options, StoreJson) : null;
        d.HelpText = help;
        d.IsRequired = r.IsRequired;
        d.ShowAsColumn = r.ShowAsColumn;
        d.IsHidden = r.IsHidden;
        if (r.SortOrder is int order) d.SortOrder = order;

        var defaultText = TextNormalizer.CleanText(r.DefaultValue);
        if (defaultText is null) d.DefaultValue = null;
        else if (defaultText.Length > 200 || !TryParseCell(d, defaultText, out _)) throw Fail.Field("defaultValue", ErrorCodes.ValidationFormat, new FieldRef("defaultValue"));
        else d.DefaultValue = defaultText;
    }

    private async Task<CustomFieldDefinition> FindAsync(string token, CancellationToken ct)
    {
        var publicId = tokens.Decode<CustomFieldDefinition>(token) ?? throw Fail.NotFound();
        return await db.Set<CustomFieldDefinition>().SingleOrDefaultAsync(d => d.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    private CustomFieldResponse ToResponse(CustomFieldDefinition d, bool hasData) =>
        new(tokens.Encode<CustomFieldDefinition>(d.PublicId), d.EntityType, d.FieldKey, d.LabelAr, d.LabelEn, d.FieldType, ReadOptions(d.Options),
            d.DefaultValue, d.HelpText, d.IsRequired, d.ShowAsColumn, d.SortOrder, d.IsHidden, hasData, Convert.ToBase64String(d.RowVersion));

    private static void EnsureEntity(string entityType)
    {
        if (!FieldCatalog.IsEntity(entityType)) throw Fail.NotFound();
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,49}$")]
    private static partial Regex FieldKeyPattern();

    // ============================================================ values (FR-180)

    /// <summary>The company's definitions of an entity (cached for the request).</summary>
    public async Task<IReadOnlyList<CustomFieldDefinition>> DefinitionsAsync(string entityType, CancellationToken ct)
    {
        if (_cache.TryGetValue(entityType, out var defs)) return defs;
        defs = await db.Set<CustomFieldDefinition>().AsNoTracking().Where(d => d.EntityType == entityType)
            .OrderBy(d => d.SortOrder).ThenBy(d => d.Id).ToListAsync(ct).ConfigureAwait(false);
        _cache[entityType] = defs;
        return defs;
    }

    /// <summary>
    /// Validates the values of a save against the definitions and returns the JSON to store (null when empty).
    /// Unknown keys are refused; hidden fields keep their stored values; defaults fill absent fields on create;
    /// required visible fields must have a value. Problems are added to <paramref name="errors"/>.
    /// </summary>
    public async Task<string?> ValidateValuesAsync(string entityType, IReadOnlyDictionary<string, JsonElement>? input, string? storedJson, bool isNew,
        List<FieldError> errors, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var defs = await DefinitionsAsync(entityType, ct).ConfigureAwait(false);
        var byKey = defs.ToDictionary(d => d.FieldKey, StringComparer.Ordinal);
        var stored = Read(storedJson);
        var result = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        input ??= new Dictionary<string, JsonElement>();
        if (input.Count > MaxFieldsPerEntity)
        {
            errors.Add(new FieldError("customValues", ErrorCodes.ValidationRange, [new FieldRef("customValues")]));
            return storedJson;
        }

        foreach (var key in input.Keys.Where(k => !byKey.ContainsKey(k)))
            errors.Add(new FieldError("customValues." + key, ErrorCodes.FieldUnknown, [key]));

        foreach (var d in defs)
        {
            if (d.IsHidden)
            {
                if (stored.TryGetValue(d.FieldKey, out var keep)) result[d.FieldKey] = keep;
                continue;
            }
            JsonElement? value = null;
            if (input.TryGetValue(d.FieldKey, out var given) && !CustomFieldTypes.IsEmpty(given))
            {
                if (TryNormalise(d, given, out var v)) value = v;
                else errors.Add(new FieldError("customValues." + d.FieldKey, ErrorCodes.ValidationFormat, [Label(d)]));
            }
            else if (isNew && !input.ContainsKey(d.FieldKey) && d.DefaultValue is not null && TryParseCell(d, d.DefaultValue, out var def))
            {
                value = def;
            }
            if (value is JsonElement ok) result[d.FieldKey] = ok;
            else if (d.IsRequired && !errors.Any(e => e.Field == "customValues." + d.FieldKey))
                errors.Add(new FieldError("customValues." + d.FieldKey, ErrorCodes.ValidationRequired, [Label(d)]));
        }
        return result.Count == 0 ? null : JsonSerializer.Serialize(result, StoreJson);
    }

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
                if (!Accounting.ImportParse.TryDate(clean, out var date)) return false;
                value = JsonSerializer.SerializeToElement(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                return true;
            case CustomFieldTypes.Checkbox:
                var b = Accounting.ImportParse.Bool(clean);
                if (b is null) return false;
                value = JsonSerializer.SerializeToElement(b.Value);
                return true;
            default:
                if (!TryNormalise(d, JsonSerializer.SerializeToElement(clean), out var v)) return false;
                value = v;
                return true;
        }
    }

    private static bool TryNormalise(CustomFieldDefinition d, JsonElement given, out JsonElement value)
    {
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

    public string Label(CustomFieldDefinition d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return ctx.Language == "en" && d.LabelEn is not null ? d.LabelEn : d.LabelAr;
    }

    internal static IReadOnlyList<string> ReadOptions(string? json)
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

    // ============================================================ data checks

    public Task<bool> HasDataAsync(string entityType, string fieldKey, CancellationToken ct) =>
        AnyValueAsync(entityType, fieldKey, _ => true, ct);

    /// <summary>True when a record (not soft-deleted) of the entity stores a value for the key matching <paramref name="match"/>.</summary>
    private async Task<bool> AnyValueAsync(string entityType, string fieldKey, Func<JsonElement, bool> match, CancellationToken ct)
    {
        var needle = "\"" + fieldKey + "\":";
        IQueryable<string?> values = entityType switch
        {
            FieldCatalog.Trip => db.Set<Trip>().Select(t => t.CustomValues),
            FieldCatalog.Driver => db.Set<Driver>().Select(t => t.CustomValues),
            FieldCatalog.Customer => db.Set<Customer>().Select(t => t.CustomValues),
            _ => throw Fail.NotFound(),
        };
        // The text search narrows the rows; the JSON is then read to confirm the key (a text value could contain the same characters).
        await foreach (var json in values.Where(v => v != null && v.Contains(needle)).AsAsyncEnumerable().WithCancellation(ct).ConfigureAwait(false))
        {
            if (Read(json).TryGetValue(fieldKey, out var v) && match(v)) return true;
        }
        return false;
    }
}
