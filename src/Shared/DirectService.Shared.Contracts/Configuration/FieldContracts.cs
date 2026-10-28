using System.Text.Json;

namespace DirectService.Shared.Contracts;

// Field configuration (PRD 7.9.3, FR-179 … FR-184): the shared engine in Shared.Infrastructure; the entities come from the modules
// (TRN.Trip, TRN.Driver, TRN.Customer) (Stage 2 session 7, DT-318).

/// <summary>A custom field definition (FR-179). <see cref="HasData"/>: the field can then only be hidden and its type is fixed (FR-181).</summary>
public sealed record CustomFieldResponse(
    string Id, string EntityType, string FieldKey, string LabelAr, string? LabelEn, string FieldType, IReadOnlyList<string> Options,
    string? DefaultValue, string? HelpText, bool IsRequired, bool ShowAsColumn, int SortOrder, bool IsHidden, bool HasData, string RowVersion);

/// <summary>
/// Create or update a custom field. <see cref="FieldKey"/> is set on create only. Types: Text, Number, Date, List, Checkbox, LongText.
/// </summary>
public sealed record CustomFieldRequest(
    string? FieldKey, string? LabelAr, string? LabelEn, string? FieldType, IReadOnlyList<string>? Options, string? DefaultValue,
    string? HelpText, bool IsRequired, bool ShowAsColumn, int? SortOrder, bool IsHidden, string? ClientId, string? RowVersion);

/// <summary>A built-in field with the company's overrides (FR-182). Core fields can be renamed but never hidden.</summary>
public sealed record StandardFieldResponse(
    string FieldKey, string DefaultLabelAr, string DefaultLabelEn, string? Label, bool IsCore, bool AlwaysRequired, bool IsHidden,
    bool IsRequired, int SortOrder);

public sealed record StandardFieldChange(string? FieldKey, string? Label, bool IsHidden, bool IsRequired, int? SortOrder);

/// <summary>Replaces the overrides of the listed fields of one entity (fields not listed keep theirs).</summary>
public sealed record StandardFieldsRequest(IReadOnlyList<StandardFieldChange>? Fields);

/// <summary>
/// The configured form of an entity (FR-180, FR-184 preview): standard and custom fields in display order with their
/// effective labels and required flags, hidden ones excluded unless asked for.
/// </summary>
public sealed record FormField(
    string Key, bool IsCustom, string Label, bool IsRequired, bool IsHidden, bool ShowAsColumn, int SortOrder, string? FieldType,
    IReadOnlyList<string>? Options, string? DefaultValue, string? HelpText);

public sealed record FormConfigResponse(string EntityType, IReadOnlyList<FormField> Fields);

/// <summary>Custom field types (cfg.CustomFieldDefinitions.FieldType, CHECK constraint values).</summary>
public static class CustomFieldTypes
{
    public const string Text = "Text";
    public const string Number = "Number";
    public const string Date = "Date";
    public const string List = "List";
    public const string Checkbox = "Checkbox";
    public const string LongText = "LongText";

    public static readonly IReadOnlyList<string> All = [Text, Number, Date, List, Checkbox, LongText];

    /// <summary>True when the JSON value is empty (null, undefined or blank text).</summary>
    public static bool IsEmpty(JsonElement value) =>
        value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        || value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString());
}
