using System.Text.Json;
using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Reports;

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

// ------------------------------------------------------------------ standard reports (FR-142, 7.7.2, 7.7.3)

/// <summary>
/// A standard report in the catalog: key, group (operational / financial), title, whether the caller may run it, and — for reports served by
/// their own module (trips list, statements, ageing, registers, e-invoicing, reconciliation) — the API path that serves it.
/// </summary>
/// <summary>A report of the catalogue: its filters (<paramref name="Parameters"/>) and those it cannot run without (<paramref name="Required"/>, checked by the screen first).</summary>
public sealed record ReportCatalogEntry(string Key, string Code, string Group, string Title, string? Path, IReadOnlyList<string> Parameters, IReadOnlyList<string>? Required = null);

/// <summary>
/// Filters of the standard reports (each report uses the ones it lists): period, as-of date, customer, driver, partner, loading and
/// unloading sites, material, grouping (margin analysis: customer / route / driver / month), party side (breach: customer / partner).
/// </summary>
public sealed record StandardReportQuery(
    DateOnly? From, DateOnly? To, DateOnly? AsOf, string? Customer, string? Driver, string? Partner, string? LoadingSite, string? UnloadingSite, string? Material,
    string? GroupBy, string? Side, string? Supplier = null);

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

public sealed record SavedReportRequest(string? Name, string? Source, ReportDefinition? Definition, bool IsShared, string? RowVersion);

public sealed record SavedReportResponse(
    string Id, string Name, string Source, ReportDefinition Definition, bool IsShared, Ref Owner, bool CanEdit, bool Pinned, DateTime? LastRunAt, DateTime CreatedAt,
    string RowVersion);

public sealed record SavedReportListQuery(string? Scope, string? Source, string? Q, int Page = 1, int PageSize = 50);

// ------------------------------------------------------------------ dashboard (FR-137 … FR-141)

/// <summary>A pin: saved report, measure (index of the definition's measures), warning and critical thresholds, order.</summary>
public sealed record DashboardPinRequest(string? SavedReportId, int? Measure, decimal? WarningThreshold, decimal? CriticalThreshold);

/// <summary>A pin as shown: the report, measure label, current value (grand total), state normal / warning / exceeded.</summary>
public sealed record DashboardPinResponse(
    string Id, Ref Report, int Measure, string MeasureLabel, decimal? Value, decimal? WarningThreshold, decimal? CriticalThreshold, string State, int SortOrder,
    string? Problem);

public sealed record PinOrderRequest(IReadOnlyList<string>? PinIds);

/// <summary>Today or month-to-date figures (FR-137); each null when the caller may not see it.</summary>
public sealed record DashboardFigures(
    DateOnly From, DateOnly To, int? Trips, decimal? Tonnage, decimal? Revenue, decimal? DriverWages, decimal? PartnerCosts, decimal? Margin, decimal? Invoiced,
    decimal? Collections);

/// <summary>An alert card (FR-138): key, severity (info, warning, error), count, value, detail text, the list it opens.</summary>
public sealed record DashboardAlert(string Key, string Severity, string Title, int Count, decimal? Value, string? Detail, DrillLink Link);

public sealed record DashboardCashCard(string Kind, string Id, string Name, decimal Balance);

/// <summary>The dashboard of the caller: figures, alerts, cash and bank balances, pinned reports — each part only with its permission (FR-141).</summary>
public sealed record DashboardResponse(
    string Period, DashboardFigures Figures, IReadOnlyList<DashboardAlert> Alerts, IReadOnlyList<DashboardCashCard>? CashAndBanks,
    IReadOnlyList<DashboardPinResponse> Pins, decimal? OpeningDifference, DateTime GeneratedAt);

/// <summary>Serializer options for report definitions stored as JSON.</summary>
public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
