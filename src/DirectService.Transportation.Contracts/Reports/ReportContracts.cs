using DirectService.Shared.Contracts;

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

// ------------------------------------------------------------------ dashboard (FR-137 … FR-141)

// Saved reports and dashboard pins: Shared.Contracts (Stage 2 session 10, DT-333).

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
