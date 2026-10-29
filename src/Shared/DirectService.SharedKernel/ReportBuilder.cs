using System.Linq.Expressions;
using DirectService.Shared.Contracts;

namespace DirectService.SharedKernel;

// The report builder (FR-143 … FR-147): the engine in Shared.Infrastructure (IReportBuilder); the data sources come from the modules
// (IReportSourceProvider, source and field keys unchanged, E-16) (Stage 2 session 9, DT-330).

/// <summary>Field types of the report builder.</summary>
public static class FieldTypes
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Money = "money";
    public const string Quantity = "quantity";
    public const string Date = "date";
    public const string Bool = "bool";
    public const string Ref = "ref";

    public static bool IsNumeric(string type) => type is Number or Money or Quantity;
}

/// <summary>
/// A field of a data source: key, labels, type, the value expression over the source row (for refs: the name) and, for refs, the public id
/// and the entity type of its token.
/// </summary>
public sealed record FieldDef(string Key, string Ar, string En, string Type, LambdaExpression Value, LambdaExpression? Pid = null, Type? Entity = null,
    bool Groupable = true, bool Custom = false, bool Nullable = false)
{
    public bool Measurable => FieldTypes.IsNumeric(Type);
}

/// <summary>
/// A data source of the report builder (FR-143): key, labels, the permissions that open it (any of them, FR-147), the queryable rows, the
/// fields, the main date (period override), the custom-field entity and JSON column, the default status filter and the row link.
/// </summary>
public sealed record SourceDef(string Key, string Ar, string En, string[] Permissions, Type Row, Func<IAppDbContext, IQueryable> Query, IReadOnlyList<FieldDef> Fields,
    string? DateField, string? CustomEntity, LambdaExpression? CustomJson, LambdaExpression? DefaultFilter, LambdaExpression RowPid, string RowLink);

/// <summary>A module's data sources for the report builder and the rules only it knows about them.</summary>
public interface IReportSourceProvider
{
    IReadOnlyList<SourceDef> Sources { get; }

    /// <summary>A run period with the module's defaults for an open end (e.g. the fiscal year).</summary>
    Task<(DateOnly From, DateOnly To)> PeriodAsync(DateOnly? from, DateOnly? to, CancellationToken ct);

    /// <summary>A stored text value of a field in the reader's language (e.g. a built-in name), null to show it as stored.</summary>
    string? Display(FieldDef field, string value, string language);

    /// <summary>The entity whose token links a detail row of the source (its own type, or e.g. the journal entry of a journal line).</summary>
    Type LinkEntity(SourceDef source);
}

/// <summary>The report builder (FR-143 … FR-147) for the saved reports, the dashboard pins and the exports.</summary>
public interface IReportBuilder
{
    bool CanUse(SourceDef s);

    /// <summary>The source by key, refused when unknown or not open to the caller.</summary>
    SourceDef Source(string? key);

    Task<IReadOnlyList<DataSourceInfo>> SourcesAsync(CancellationToken ct);

    /// <summary>Runs a definition (builder preview or saved report): grouped with measures, pivoted by the column grouping, or detail rows.</summary>
    Task<ReportResult> RunAsync(string? sourceKey, ReportDefinition? definition, DateOnly? from, DateOnly? to, string? title, CancellationToken ct);

    /// <summary>Checks a definition against its source without running it (saved reports).</summary>
    Task ValidateAsync(string sourceKey, ReportDefinition d, CancellationToken ct);

    /// <summary>The pin options of a definition with their current totals (pin dialog and its preview).</summary>
    Task<IReadOnlyList<PinMeasureOption>> PinMeasuresAsync(string sourceKey, ReportDefinition d, CancellationToken ct);

    /// <summary>A pin's value, name and kind by its key.</summary>
    Task<(decimal? Value, string Label, string Kind)> PinValueAsync(string sourceKey, ReportDefinition d, string key, CancellationToken ct);

    /// <summary>Checks that a pin key belongs to the definition (pin and edit).</summary>
    Task<bool> HasPinOptionAsync(string sourceKey, ReportDefinition d, string key, CancellationToken ct);
}

/// <summary>Limits of the current operation: background exports may return more rows than screens (FR-151).</summary>
public sealed class ExportLimits
{
    public bool Background { get; set; }
}
