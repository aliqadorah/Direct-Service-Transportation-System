using DirectService.Shared.Infrastructure.Configuration;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Master data (B3, M04) and field configuration (M03 part): drivers with truck, customers, partners, materials, loading and
/// unloading sites, ownership types; custom fields, standard fields, form configuration and column views.
/// Access (PRD 3.4, DT-36): writes need masters.manage; driver and partner records need drv.view or masters.manage; customer,
/// material, site and ownership-type lists and every pick list ("options") are open to company members; field configuration
/// needs settings.manage; personal column views belong to each member.
/// </summary>
internal static class MasterDataEndpoints
{
    private static readonly string[] DriverReaders = [PermissionKeys.DrvView, PermissionKeys.MastersManage];

    public static void MapMasterDataEndpoints(this RouteGroupBuilder company)
    {
        MapDrivers(company.MapGroup("/drivers").WithTags("Drivers"));
        MapCustomers(company.MapGroup("/customers").WithTags("Customers"));
        MapPartners(company.MapGroup("/partners").WithTags("Partners"));
        MapMaterials(company.MapGroup("/materials").WithTags("Materials"));
        MapSites<Domain.Trn.LoadingSite>(company.MapGroup("/loading-sites").WithTags("Loading sites"), "loading-sites");
        MapSites<Domain.Trn.UnloadingSite>(company.MapGroup("/unloading-sites").WithTags("Unloading sites"), "unloading-sites");
        MapOwnershipTypes(company.MapGroup("/ownership-types").WithTags("Ownership types"));
        MapFieldConfiguration(company);
    }

    private static Results<Created<T>, Ok<T>> Saved<T>((T Value, bool Created) r) =>
        r.Created ? TypedResults.Created((string?)null, r.Value) : TypedResults.Ok(r.Value);

    private static void MapDrivers(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] MasterListQuery q, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/options", async (string? q, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.OptionsAsync(q, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/expiry-alerts", async (DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ExpiryAlertsAsync(ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/{driver}", async (string driver, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ProfileAsync(driver, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/{driver}/references", async (string driver, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(driver, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/{driver}/history", async (string driver, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(driver, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapPost("/", async (DriverRequest r, DriverService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{driver}", async (string driver, DriverRequest r, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(driver, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{driver}/archive", async (string driver, StatusChangeRequest r, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(driver, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{driver}/restore", async (string driver, StatusChangeRequest r, DriverService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(driver, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{driver}/delete", async (string driver, DeleteRequest r, DriverService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(driver, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, "drivers", DriverReaders);
    }

    private static void MapCustomers(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] MasterListQuery q, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/options", async (CustomerService svc, CancellationToken ct) => TypedResults.Ok(await svc.OptionsAsync(ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{customer}", async (string customer, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ProfileAsync(customer, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{customer}/references", async (string customer, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(customer, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{customer}/history", async (string customer, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(customer, ct).ConfigureAwait(false))).RequireMember();
        g.MapPost("/", async (CustomerRequest r, CustomerService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{customer}", async (string customer, CustomerRequest r, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(customer, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{customer}/archive", async (string customer, StatusChangeRequest r, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(customer, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{customer}/restore", async (string customer, StatusChangeRequest r, CustomerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(customer, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{customer}/delete", async (string customer, DeleteRequest r, CustomerService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(customer, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, "customers", null);
    }

    private static void MapPartners(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] MasterListQuery q, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/options", async (PartnerService svc, CancellationToken ct) => TypedResults.Ok(await svc.OptionsAsync(ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{partner}", async (string partner, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ProfileAsync(partner, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/{partner}/references", async (string partner, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(partner, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapGet("/{partner}/history", async (string partner, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(partner, ct).ConfigureAwait(false))).RequireAnyPermission(DriverReaders);
        g.MapPost("/", async (PartnerRequest r, PartnerService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{partner}", async (string partner, PartnerRequest r, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(partner, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{partner}/archive", async (string partner, StatusChangeRequest r, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(partner, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{partner}/restore", async (string partner, StatusChangeRequest r, PartnerService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(partner, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{partner}/delete", async (string partner, DeleteRequest r, PartnerService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(partner, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, "partners", DriverReaders);
    }

    private static void MapMaterials(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] MasterListQuery q, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/options", async (MaterialService svc, CancellationToken ct) => TypedResults.Ok(await svc.OptionsAsync(ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{material}", async (string material, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(material, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{material}/references", async (string material, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(material, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{material}/history", async (string material, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(material, ct).ConfigureAwait(false))).RequireMember();
        g.MapPost("/", async (MaterialRequest r, MaterialService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{material}", async (string material, MaterialRequest r, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(material, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{material}/archive", async (string material, StatusChangeRequest r, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(material, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{material}/restore", async (string material, StatusChangeRequest r, MaterialService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(material, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{material}/delete", async (string material, DeleteRequest r, MaterialService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(material, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, "materials", null);
    }

    private static void MapSites<TSite>(RouteGroupBuilder g, string kind) where TSite : AuditableEntity, Domain.Common.ISite, new()
    {
        g.MapGet("/", async ([AsParameters] MasterListQuery q, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/options", async (SiteService<TSite> svc, CancellationToken ct) => TypedResults.Ok(await svc.OptionsAsync(ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{site}", async (string site, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(site, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{site}/references", async (string site, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(site, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{site}/history", async (string site, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(site, ct).ConfigureAwait(false))).RequireMember();
        g.MapPost("/", async (SiteRequest r, SiteService<TSite> svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{site}", async (string site, SiteRequest r, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(site, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{site}/archive", async (string site, StatusChangeRequest r, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(site, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{site}/restore", async (string site, StatusChangeRequest r, SiteService<TSite> svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(site, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{site}/delete", async (string site, DeleteRequest r, SiteService<TSite> svc, CancellationToken ct) =>
        { await svc.DeleteAsync(site, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, kind, null);
    }

    private static void MapOwnershipTypes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (OwnershipTypeService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{ownershipType}", async (string ownershipType, OwnershipTypeService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(ownershipType, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{ownershipType}/references", async (string ownershipType, OwnershipTypeService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReferencesAsync(ownershipType, ct).ConfigureAwait(false))).RequireMember();
        g.MapGet("/{ownershipType}/history", async (string ownershipType, OwnershipTypeService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(ownershipType, ct).ConfigureAwait(false))).RequireMember();
        g.MapPost("/", async (OwnershipTypeRequest r, OwnershipTypeService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPut("/{ownershipType}", async (string ownershipType, OwnershipTypeRequest r, OwnershipTypeService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(ownershipType, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/{ownershipType}/delete", async (string ownershipType, DeleteRequest r, OwnershipTypeService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(ownershipType, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); })
            .RequirePermission(PermissionKeys.MastersManage);
        MapExchange(g, "ownership-types", null);
    }

    /// <summary>Export (readers of the list; members when <paramref name="readers"/> is null) and import (masters.manage) of a list (FR-164).</summary>
    private static void MapExchange(RouteGroupBuilder g, string kind, string[]? readers)
    {
        var export = g.MapGet("/export", async ([AsParameters] MasterListQuery q, string? format, MasterExchangeService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(kind, q, format, ct).ConfigureAwait(false)));
        if (readers is null) export.RequireMember();
        else export.RequireAnyPermission(readers);
        g.MapGet("/import/template", async (DirectService.Shared.Infrastructure.Imports.ImportTemplateService svc, CancellationToken ct) =>
            File(await svc.TemplateAsync(kind, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.MastersManage);
        g.MapPost("/import/preview", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping, MasterExchangeService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewAsync(kind, s, file.FileName, file.Length, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.MastersManage).DisableAntiforgery().Upload(); // antiforgery header checked by AntiforgeryFilter
        g.MapPost("/import", async (IFormFile file, [Microsoft.AspNetCore.Mvc.FromForm] string? mapping, MasterExchangeService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportAsync(kind, s, file.FileName, file.Length, mapping, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.MastersManage).DisableAntiforgery().Upload();
    }

    // ------------------------------------------------------------ field configuration (FR-179 … FR-184)

    private static void MapFieldConfiguration(RouteGroupBuilder company)
    {
        var custom = company.MapGroup("/custom-fields").WithTags("Field configuration");
        custom.MapGet("/{entityType}", async (string entityType, CustomFieldService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(entityType, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        custom.MapPost("/{entityType}", async (string entityType, CustomFieldRequest r, CustomFieldService svc, CancellationToken ct) =>
            Saved(await svc.CreateAsync(entityType, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        custom.MapPut("/field/{field}", async (string field, CustomFieldRequest r, CustomFieldService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(field, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        custom.MapPost("/field/{field}/delete", async (string field, DeleteRequest r, CustomFieldService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(field, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.SettingsManage);

        var standard = company.MapGroup("/standard-fields").WithTags("Field configuration");
        standard.MapGet("/{entityType}", async (string entityType, StandardFieldService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(entityType, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        standard.MapPut("/{entityType}", async (string entityType, StandardFieldsRequest r, StandardFieldService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(entityType, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);

        // The configured form (labels, required flags, custom fields) drives every form and list: any member reads it.
        company.MapGet("/forms/{entityType}", async (string entityType, bool? includeHidden, StandardFieldService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.FormAsync(entityType, includeHidden == true, ct).ConfigureAwait(false))).WithTags("Field configuration").RequireMember();

        var views = company.MapGroup("/column-views").WithTags("Column views");
        views.MapGet("/{viewKey}", async (string viewKey, ColumnViewService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(viewKey, ct).ConfigureAwait(false))).RequireMember();
        views.MapPut("/{viewKey}", async (string viewKey, ColumnViewRequest r, ColumnViewService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SavePersonalAsync(viewKey, r, ct).ConfigureAwait(false))).RequireMember();
        views.MapPost("/{viewKey}/reset", async (string viewKey, ColumnViewService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ResetPersonalAsync(viewKey, ct).ConfigureAwait(false))).RequireMember();
        views.MapPut("/{viewKey}/default", async (string viewKey, ColumnViewRequest r, ColumnViewService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SaveCompanyDefaultAsync(viewKey, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
    }

    private const long UploadLimit = 11 * 1024 * 1024;

    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder b) => b
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimit))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = UploadLimit });

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
