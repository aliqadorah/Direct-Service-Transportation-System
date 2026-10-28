using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Pricing (B4, M05): customer and partner tariffs (FR-019 … FR-026), monthly loading-site driver rates (FR-027 … FR-037) and the
/// price quote of the trip form (FR-006). Access (PRD 3.4, DT-50): writes and imports need masters.manage; tariffs, monthly rates,
/// their history and exports are read by holders of masters.manage, trips.view, trips.edit or reports.view; the quote by holders of
/// trips.edit or masters.manage; the missing-rates warning (dashboard) by every company member.
/// </summary>
internal static class PricingEndpoints
{
    private static readonly string[] Readers = [PermissionKeys.MastersManage, PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.ReportsView];
    private static readonly string[] Quoters = [PermissionKeys.TripsEdit, PermissionKeys.MastersManage];

    public static void MapPricingEndpoints(this RouteGroupBuilder company)
    {
        MapTariffs(company.MapGroup("/tariffs").WithTags("Tariffs"));
        MapMonthlyRates(company.MapGroup("/monthly-rates").WithTags("Monthly site rates"));
        company.MapGet("/pricing/quote", async ([AsParameters] PriceQuoteQuery q, PricingResolver svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.QuoteAsync(q, ct).ConfigureAwait(false))).WithTags("Pricing").RequireAnyPermission(Quoters);
    }

    private static void MapTariffs(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] TariffListQuery q, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapGet("/export", async ([AsParameters] TariffListQuery q, string? format, PricingExchangeService svc, CancellationToken ct) =>
            File(await svc.ExportTariffsAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapGet("/import/template", async (DirectService.Shared.Infrastructure.Imports.ImportTemplateService svc, CancellationToken ct) =>
            File(await svc.TemplateAsync("tariffs", ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/import/preview", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping, PricingExchangeService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewTariffsAsync(s, file.FileName, file.Length, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.MastersManage).DisableAntiforgery().Upload(); // antiforgery header checked by AntiforgeryFilter
        g.MapPost("/import", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping, PricingExchangeService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportTariffsAsync(s, file.FileName, file.Length, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.MastersManage).DisableAntiforgery().Upload();
        g.MapGet("/{tariff}", async (string tariff, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(tariff, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapGet("/{tariff}/history", async (string tariff, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(tariff, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapPost("/", async (TariffRequest r, TariffService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{tariff}", async (string tariff, TariffRequest r, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(tariff, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{tariff}/archive", async (string tariff, StatusChangeRequest r, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(tariff, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{tariff}/restore", async (string tariff, StatusChangeRequest r, TariffService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(tariff, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
    }

    private static void MapMonthlyRates(RouteGroupBuilder g)
    {
        g.MapGet("/months", async (string? from, string? to, MonthlyRateService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.MonthsAsync(from, to, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapGet("/missing", async (string? month, MonthlyRateService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.MissingAsync(month, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{month}", async (string month, MonthlyRateService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(month, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapPut("/{month}", async (string month, MonthRatesRequest r, MonthlyRateService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SaveAsync(month, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapGet("/{month}/history", async (string month, string? loadingSite, MonthlyRateService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(month, loadingSite, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
        g.MapGet("/{month}/export", async (string month, string? format, PricingExchangeService svc, CancellationToken ct) =>
            File(await svc.ExportMonthAsync(month, format, ct).ConfigureAwait(false))).RequireAnyPermission(Readers);
    }

    private static Results<Created<T>, Ok<T>> Saved<T>((T Value, bool Created) r) =>
        r.Created ? TypedResults.Created((string?)null, r.Value) : TypedResults.Ok(r.Value);

    private const long UploadLimit = 11 * 1024 * 1024;

    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder b) => b
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimit))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = UploadLimit });

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
