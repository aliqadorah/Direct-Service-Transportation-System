using DirectService.Transportation.Application.Trips;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Trips (B5, M06): list with filters and totals, export, last ten, quick-entry defaults, details and history (trips.view);
/// create, edit, clear Needs review and import (trips.edit); void (trips.void). Manual driver rates and tariff deviations under
/// the tariff lock are checked inside the service against masters.manage (BR-004).
/// </summary>
internal static class TripEndpoints
{
    public static void MapTripEndpoints(this RouteGroupBuilder company)
    {
        var g = company.MapGroup("/trips").WithTags("Trips");
        g.MapGet("/", async ([AsParameters] TripListQuery q, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsView);
        g.MapGet("/export", async ([AsParameters] TripListQuery q, string? format, TripExportService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsView);
        g.MapGet("/recent", async (TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RecentAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsEdit);
        g.MapGet("/quick-entry", async (TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.QuickEntryAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsEdit);
        g.MapPost("/calculate", async (TripCalculationRequest r, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CalculateAsync(r, ct).ConfigureAwait(false))).RequireAnyPermission([PermissionKeys.TripsEdit, PermissionKeys.TripsView]).AllowWhenReadOnly();
        g.MapGet("/{trip}", async (string trip, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(trip, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsView);
        g.MapGet("/{trip}/history", async (string trip, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(trip, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsView);
        g.MapPost("/", async (TripRequest r, TripService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(r, ct).ConfigureAwait(false);
            return created ? (Results<Created<TripSaveResult>, Ok<TripSaveResult>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.TripsEdit);
        g.MapPut("/{trip}", async (string trip, TripRequest r, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(trip, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsEdit);
        g.MapPost("/{trip}/void", async (string trip, TripVoidRequest r, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(trip, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsVoid);
        g.MapPost("/{trip}/clear-review", async (string trip, TripReviewRequest r, TripService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ClearReviewAsync(trip, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsEdit);
        g.MapGet("/import/template", async (Application.Imports.ImportTemplateService svc, CancellationToken ct) =>
            File(await svc.TemplateAsync("trips", ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.TripsEdit);
        g.MapPost("/import/preview", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mode, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping,
            TripImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewAsync(s, file.FileName, file.Length, mode, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.TripsEdit).DisableAntiforgery().Upload(); // antiforgery header checked by AntiforgeryFilter
        g.MapPost("/import", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mode, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping,
            TripImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportAsync(s, file.FileName, file.Length, mode, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.TripsEdit).DisableAntiforgery().Upload();
    }

    private const long UploadLimit = 11 * 1024 * 1024;

    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder b) => b
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimit))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = UploadLimit });

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
