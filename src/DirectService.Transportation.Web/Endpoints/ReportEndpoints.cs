using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Reports and dashboard (B10, M12): the dashboard (any member; each card by its own permission, FR-141), the standard reports catalog and the
/// reports computed here (reports.view or the module's view permission, checked per report), the report builder, saved reports and pins
/// (reports.view: "all reports, the report builder and saved reports", PRD 3.4; each data source also needs its view permission, FR-147).
/// </summary>
internal static class ReportEndpoints
{
    public static void MapReportEndpoints(this RouteGroupBuilder company)
    {
        var dash = company.MapGroup("/dashboard").WithTags("Dashboard");
        dash.MapGet("/", async (string? period, DashboardService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(period, ct).ConfigureAwait(false))).RequireMember();
        dash.MapGet("/pins", async (SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PinsAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        dash.MapPost("/pins", async (DashboardPinRequest req, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PinAsync(req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        dash.MapPut("/pins/{pin}", async (string pin, DashboardPinRequest req, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdatePinAsync(pin, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        dash.MapPost("/pins/order", async (PinOrderRequest req, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReorderPinsAsync(req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        dash.MapPost("/pins/{pin}/delete", async (string pin, SavedReportService svc, CancellationToken ct) =>
        {
            await svc.UnpinAsync(pin, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequirePermission(PermissionKeys.ReportsView);

        var r = company.MapGroup("/reports").WithTags("Reports");
        r.MapGet("/catalog", async (StandardReportService svc, CancellationToken ct) => TypedResults.Ok(await svc.CatalogAsync(ct).ConfigureAwait(false))).RequireMember();
        r.MapGet("/standard/{key}", async (string key, [AsParameters] StandardReportQuery q, StandardReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RunAsync(key, q, ct).ConfigureAwait(false))).RequireMember();
        r.MapGet("/standard/{key}/export", async (string key, [AsParameters] StandardReportQuery q, string? format, StandardReportService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(key, q, format, ct).ConfigureAwait(false))).RequireMember();

        var b = r.MapGroup("/builder").WithTags("Report builder");
        b.MapGet("/sources", async (ReportBuilderService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SourcesAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        b.MapPost("/run", async (ReportRunRequest req, ReportBuilderService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RunAsync(req.Source, req.Definition, req.From, req.To, null, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        b.MapPost("/export", async (ReportRunRequest req, string? format, ReportBuilderService svc, ReportExporter exporter, CancellationToken ct) =>
        {
            var result = await svc.RunAsync(req.Source, req.Definition, req.From, req.To, null, ct).ConfigureAwait(false);
            return File(await exporter.ExportAsync(StandardReportService.ToTable(result), format, "report", ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.ReportsView);

        var s = r.MapGroup("/saved").WithTags("Saved reports");
        s.MapGet("/", async ([AsParameters] SavedReportListQuery q, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        s.MapGet("/{report}", async (string report, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(report, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        s.MapPost("/", async (SavedReportRequest req, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Created((string?)null, await svc.CreateAsync(req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        s.MapPut("/{report}", async (string report, SavedReportRequest req, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(report, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        s.MapPost("/{report}/delete", async (string report, SavedReportService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(report, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequirePermission(PermissionKeys.ReportsView);
        s.MapPost("/{report}/run", async (string report, DateOnly? from, DateOnly? to, SavedReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RunAsync(report, from, to, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
        s.MapPost("/{report}/export", async (string report, DateOnly? from, DateOnly? to, string? format, SavedReportService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(report, from, to, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.ReportsView);
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
