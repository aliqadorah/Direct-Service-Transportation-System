using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Reports;

// Report results, the report builder and ReportJson: Shared.Contracts (Stage 2 session 9, DT-330).

// ------------------------------------------------------------------ standard reports (FR-142, 7.7.2, 7.7.3)

/// <summary>
/// A standard report in the catalog: key, group (operational / financial), title, whether the caller may run it, and — for reports served by
/// their own module (trips list, statements, ageing, registers, e-invoicing, reconciliation) — the API path that serves it.
/// </summary>
/// <summary>
/// A report of the catalogue: its filters (<paramref name="Parameters"/>) and those it cannot run without (<paramref name="Required"/>, checked by
/// the screen first); a one-line <paramref name="Description"/> in the caller's language; the value each filter takes when left empty
/// (<paramref name="Defaults"/>: from, to, asOf as yyyy-MM-dd, side, groupBy), so the screen shows the value in use (DT-249).
/// </summary>
public sealed record ReportCatalogEntry(string Key, string Code, string Group, string Title, string? Path, IReadOnlyList<string> Parameters, IReadOnlyList<string>? Required = null,
    string? Description = null, IReadOnlyDictionary<string, string>? Defaults = null);

/// <summary>
/// Filters of the standard reports (each report uses the ones it lists): period, as-of date, customer, driver, partner, loading and
/// unloading sites, material, grouping (margin analysis: customer / route / driver / month), party side (breach: customer / partner).
/// </summary>
public sealed record StandardReportQuery(
    DateOnly? From, DateOnly? To, DateOnly? AsOf, string? Customer, string? Driver, string? Partner, string? LoadingSite, string? UnloadingSite, string? Material,
    string? GroupBy, string? Side, string? Supplier = null);

// ------------------------------------------------------------------ saved reports (FR-145)

public sealed record SavedReportRequest(string? Name, string? Source, ReportDefinition? Definition, bool IsShared, string? RowVersion);

public sealed record SavedReportResponse(
    string Id, string Name, string Source, ReportDefinition Definition, bool IsShared, Ref Owner, bool CanEdit, bool Pinned, DateTime? LastRunAt, DateTime CreatedAt,
    string RowVersion);

public sealed record SavedReportListQuery(string? Scope, string? Source, string? Q, int Page = 1, int PageSize = 50);

// ------------------------------------------------------------------ dashboard (FR-137 … FR-141)

/// <summary>
/// A pin: saved report, measure, warning and critical thresholds (both optional). DT-275: <paramref name="MeasureKey"/> — one of the report's
/// <see cref="PinMeasureOption"/> keys ("rows", a summary measure's index, "sum:&lt;field&gt;" for a detail column); without it the older
/// <paramref name="Measure"/> index of the definition's measures. <paramref name="WarnWhen"/> "Above" (default) or "Below".
/// </summary>
public sealed record DashboardPinRequest(string? SavedReportId, int? Measure, decimal? WarningThreshold, decimal? CriticalThreshold, string? MeasureKey = null,
    string? WarnWhen = null);

/// <summary>
/// The state of a pinned value (FR-140, DT-275), shared by the dashboard and the pin dialog's preview: with "Above" exceeded at or above the
/// critical threshold and warning at or above the warning threshold; with "Below" the same at or below them; otherwise normal.
/// </summary>
public static class PinStates
{
    public const string Above = "Above";
    public const string Below = "Below";

    public static string State(decimal? value, decimal? warning, decimal? critical, string? warnWhen)
    {
        if (value is not decimal v) return "normal";
        var below = string.Equals(warnWhen, Below, StringComparison.OrdinalIgnoreCase);
        bool Reached(decimal t) => below ? v <= t : v >= t;
        return critical is decimal c && Reached(c) ? "exceeded" : warning is decimal w && Reached(w) ? "warning" : "normal";
    }

    /// <summary>The critical threshold must be at or beyond the warning threshold in the chosen direction (either may be left empty).</summary>
    public static bool Ordered(decimal? warning, decimal? critical, string? warnWhen) =>
        warning is not decimal w || critical is not decimal c || (string.Equals(warnWhen, Below, StringComparison.OrdinalIgnoreCase) ? c <= w : c >= w);
}

/// <summary>A pin as shown: the report, measure label, current value (grand total), state normal / warning / exceeded.</summary>
public sealed record DashboardPinResponse(
    string Id, Ref Report, int Measure, string MeasureLabel, decimal? Value, decimal? WarningThreshold, decimal? CriticalThreshold, string State, int SortOrder,
    string? Problem, string MeasureKey = "", string ValueKind = "money", string WarnWhen = PinStates.Above);

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
