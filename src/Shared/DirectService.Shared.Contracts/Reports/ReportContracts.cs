using System.Text.Json;

namespace DirectService.Shared.Contracts;

// Report results and the report builder (FR-143 … FR-148): the shared engine in Shared.Infrastructure; the data sources come from the
// modules (Stage 2 session 9, DT-330).

// ------------------------------------------------------------------ results shared by standard reports and the builder

/// <summary>A result column: key, header in the reader's language and how values show (text, money, number, quantity, percent, date).</summary>
public sealed record ResultColumn(string Key, string Header, string Kind);

/// <summary>Where a row leads (FR-149): an entity of a type and its token, or a list with filters (e.g. trips?customer=…&amp;billing=unbilled).</summary>
public sealed record DrillLink(string Type, string? Id, IReadOnlyDictionary<string, string>? Filters);

public sealed record ResultRow(IReadOnlyList<object?> Values, DrillLink? Link);

/// <summary>A report result (FR-148): title, filters applied, columns, rows, totals row (null cells where a total means nothing), optional note.</summary>
public sealed record ReportResult(string Key, string Title, IReadOnlyList<ReportFilterValue> Filters, IReadOnlyList<ResultColumn> Columns, IReadOnlyList<ResultRow> Rows,
    IReadOnlyList<object?>? Totals, string? Note, bool Truncated);

public sealed record ReportFilterValue(string Label, string Value);

// ------------------------------------------------------------------ report builder (FR-143 … FR-147)

/// <summary>A field of a data source: key, label, type (text, number, money, quantity, date, bool, ref) and whether it can group, measure and filter.</summary>
public sealed record SourceField(string Key, string Label, string Type, bool Groupable, bool Measurable, bool Custom);

/// <summary>A data source available to the caller (FR-147) with its fields, custom fields included (FR-143).</summary>
public sealed record DataSourceInfo(string Key, string Label, IReadOnlyList<SourceField> Fields);

/// <summary>A grouping: a field and, for dates, the unit (day, month, quarter, year).</summary>
public sealed record ReportGrouping(string Field, string? DateUnit);

/// <summary>A measure: function (sum, count, avg, min, max) over a numeric field (count needs none).</summary>
public sealed record ReportMeasure(string Function, string? Field);

/// <summary>A filter: field, operator (eq, ne, gt, ge, lt, le, contains, in, between, empty, notEmpty) and values.</summary>
public sealed record ReportFilter(string Field, string Operator, IReadOnlyList<string>? Values);

/// <summary>Sort: by a group ("group") or a measure index ("measure:0"), descending or not.</summary>
public sealed record ReportSort(string By, bool Descending);

/// <summary>
/// A report definition (cfg.SavedReports.Definition): row groupings (up to 2), optional column grouping, measures (1–6), filters, sort,
/// top-N, or detail mode with its columns (one row per record).
/// </summary>
public sealed record ReportDefinition(
    IReadOnlyList<ReportGrouping>? Rows, ReportGrouping? Column, IReadOnlyList<ReportMeasure>? Measures, IReadOnlyList<ReportFilter>? Filters, ReportSort? Sort,
    int? Top, bool Detail, IReadOnlyList<string>? Columns);

/// <summary>Run a definition directly (builder preview) or a saved report with an optional period override.</summary>
public sealed record ReportRunRequest(string? Source, ReportDefinition? Definition, DateOnly? From, DateOnly? To);

/// <summary>A value a pin can show (DT-275): key, name in the reader's language, kind (number, money, quantity) and the current total.</summary>
public sealed record PinMeasureOption(string Key, string Label, string Kind, decimal? Value);

/// <summary>Serializer options for report definitions stored as JSON.</summary>
public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
