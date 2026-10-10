using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Trn;
using DirectService.Transportation.Web.Pipeline;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// E-invoicing (B7, M10 + settings in M03): settings (settings.manage), the submissions list and report (inv.view or reports.view),
/// a document's submission (inv.view), manual submission and resubmission (inv.issue — PRD 3.4 "submit e-invoices").
/// </summary>
internal static class EInvoicingEndpoints
{
    public static void MapEInvoicingEndpoints(this RouteGroupBuilder company)
    {
        var s = company.MapGroup("/settings/e-invoicing").WithTags("E-invoicing");
        s.MapGet("/", async (EInvoiceSettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        s.MapPut("/", async (EInvoiceSettingsRequest r, EInvoiceSettingsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SaveAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);

        var g = company.MapGroup("/einvoice-submissions").WithTags("E-invoicing");
        g.MapGet("/", async ([AsParameters] EInvoiceListQuery q, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(PermissionKeys.InvView, PermissionKeys.ReportsView);
        g.MapGet("/export", async ([AsParameters] EInvoiceListQuery q, string? format, EInvoiceService svc, CancellationToken ct) =>
        {
            var f = await svc.ExportAsync(q, format, ct).ConfigureAwait(false);
            return TypedResults.File(f.Content, f.ContentType, f.FileName);
        }).RequireAnyPermission(PermissionKeys.InvView, PermissionKeys.ReportsView);
        g.MapGet("/{submission}", async (string submission, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(submission, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapPost("/{submission}/submit", async (string submission, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SubmitAsync(submission, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);

        company.MapGet("/invoices/{invoice}/e-invoice", async (string invoice, MasterDataSupport support, EInvoiceService svc, CancellationToken ct) =>
        {
            var inv = await support.FindAsync<Invoice>(invoice, ct).ConfigureAwait(false);
            return TypedResults.Ok(await svc.ForDocumentAsync(inv.Id, null, ct).ConfigureAwait(false));
        }).WithTags("E-invoicing").RequirePermission(PermissionKeys.InvView);
        company.MapGet("/credit-notes/{creditNote}/e-invoice", async (string creditNote, MasterDataSupport support, EInvoiceService svc, CancellationToken ct) =>
        {
            var cn = await support.FindAsync<CreditNote>(creditNote, ct).ConfigureAwait(false);
            return TypedResults.Ok(await svc.ForDocumentAsync(null, cn.Id, ct).ConfigureAwait(false));
        }).WithTags("E-invoicing").RequirePermission(PermissionKeys.InvView);
    }
}
