namespace DirectService.Shared.Contracts;

// Background exports (FR-151): the shared engine in Shared.Infrastructure; the module kinds (standard reports, audit log, company copy) come
// from the modules' export providers (Stage 2 session 10, DT-334). The two queries an export request carries moved with it (same names and
// members): the standard-report filters and the audit-log filters.

/// <summary>What a background export produces: report builder, saved report, standard report or audit log, in a format.</summary>
public sealed record ExportRequest(string? Kind, string? Source, ReportDefinition? Definition, string? Report, string? Key, StandardReportQuery? Query, AuditLogQuery? Audit,
    DateOnly? From, DateOnly? To, string? Format);

/// <summary>A queued export: the token of the file once ready (sent with the notification).</summary>
public sealed record ExportJobResponse(string Id, string Status);

// ------------------------------------------------------------------ queries carried by an export request

/// <summary>
/// Filters of the standard reports (each report uses the ones it lists): period, as-of date, customer, driver, partner, loading and
/// unloading sites, material, grouping (margin analysis: customer / route / driver / month), party side (breach: customer / partner).
/// </summary>
public sealed record StandardReportQuery(
    DateOnly? From, DateOnly? To, DateOnly? AsOf, string? Customer, string? Driver, string? Partner, string? LoadingSite, string? UnloadingSite, string? Material,
    string? GroupBy, string? Side, string? Supplier = null);

/// <param name="Technical">Adds the stored values as they are (administrators with settings.manage only, DT-188).</param>
public sealed record AuditLogQuery(string? User, string? Action, string? EntityType, DateOnly? From, DateOnly? To, string? Q, int Page = 1, int PageSize = 50,
    bool Technical = false);
