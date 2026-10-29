namespace DirectService.Shared.Contracts;

// Saved reports (FR-145, FR-146) and dashboard pins (FR-140): the shared engine in Shared.Infrastructure (Stage 2 session 10, DT-333).

// ------------------------------------------------------------------ saved reports (FR-145)

public sealed record SavedReportRequest(string? Name, string? Source, ReportDefinition? Definition, bool IsShared, string? RowVersion);

public sealed record SavedReportResponse(
    string Id, string Name, string Source, ReportDefinition Definition, bool IsShared, Ref Owner, bool CanEdit, bool Pinned, DateTime? LastRunAt, DateTime CreatedAt,
    string RowVersion);

public sealed record SavedReportListQuery(string? Scope, string? Source, string? Q, int Page = 1, int PageSize = 50);

// ------------------------------------------------------------------ dashboard pins (FR-140, FR-141)

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
