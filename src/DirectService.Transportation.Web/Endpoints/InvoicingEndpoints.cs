using DirectService.Transportation.Application.Invoicing;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Invoicing (B6, M09): invoices, credit notes, layouts and printing. inv.view reads, lists and prints; inv.issue creates, edits,
/// approves and deletes drafts, issues credit notes and designs layouts; inv.void voids invoices and credit notes (PRD 3.4).
/// </summary>
internal static class InvoicingEndpoints
{
    public static void MapInvoicingEndpoints(this RouteGroupBuilder company)
    {
        var g = company.MapGroup("/invoices").WithTags("Invoices");
        g.MapGet("/", async ([AsParameters] InvoiceListQuery q, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapPost("/calculate", async (InvoiceCalculationRequest r, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CalculateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        g.MapGet("/export", async ([AsParameters] InvoiceListQuery q, string? format, InvoiceService svc, IReportExporter exporter, CancellationToken ct) =>
            File(await exporter.ExportAsync(await svc.ExportTableAsync(q, ct).ConfigureAwait(false), format, "invoices", ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapGet("/unbilled-trips", async ([AsParameters] UnbilledTripsQuery q, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UnbilledAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        g.MapGet("/{invoice}", async (string invoice, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(invoice, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapGet("/{invoice}/history", async (string invoice, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.HistoryAsync(invoice, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapGet("/{invoice}/print", async (string invoice, string? format, string? layout, InvoicePrintService svc, CancellationToken ct) =>
            File(await svc.InvoiceAsync(invoice, format, layout, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        g.MapPost("/", async (InvoiceRequest r, InvoiceService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(r, ct).ConfigureAwait(false);
            return created ? (Results<Created<InvoiceDetails>, Ok<InvoiceDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.InvIssue);
        g.MapPut("/{invoice}", async (string invoice, InvoiceRequest r, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateDraftAsync(invoice, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        g.MapPost("/{invoice}/approve", async (string invoice, StatusChangeRequest r, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ApproveAsync(invoice, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        g.MapPost("/{invoice}/delete", async (string invoice, InvoiceDeleteRequest r, InvoiceService svc, CancellationToken ct) =>
        { await svc.DeleteDraftAsync(invoice, r.Reason, r.RowVersion, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.InvIssue);
        g.MapPost("/{invoice}/void", async (string invoice, InvoiceVoidRequest r, InvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(invoice, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvVoid);

        var c = company.MapGroup("/credit-notes").WithTags("Credit notes");
        c.MapGet("/", async ([AsParameters] CreditNoteListQuery q, CreditNoteService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        c.MapPost("/calculate", async (CreditNoteRequest r, CreditNoteService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CalculateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        c.MapGet("/export", async ([AsParameters] CreditNoteListQuery q, string? format, CreditNoteService svc, IReportExporter exporter, CancellationToken ct) =>
            File(await exporter.ExportAsync(await svc.ExportTableAsync(q, ct).ConfigureAwait(false), format, "credit-notes", ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        c.MapGet("/{creditNote}", async (string creditNote, CreditNoteService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(creditNote, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        c.MapGet("/{creditNote}/print", async (string creditNote, string? format, InvoicePrintService svc, CreditNoteService credits, CancellationToken ct) =>
            File(await svc.CreditNoteAsync(creditNote, format, credits, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvView);
        c.MapPost("/", async (CreditNoteRequest r, CreditNoteService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(r, ct).ConfigureAwait(false);
            return created ? (Results<Created<CreditNoteDetails>, Ok<CreditNoteDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.InvIssue);
        c.MapPost("/{creditNote}/void", async (string creditNote, CreditNoteVoidRequest r, CreditNoteService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(creditNote, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvVoid);

        var l = company.MapGroup("/invoice-layouts").WithTags("Invoice layouts");
        l.MapGet("/{layoutType}", async (string layoutType, bool? builtIn, InvoiceLayoutService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(layoutType, ct, builtIn == true).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        l.MapPut("/{layoutType}", async (string layoutType, InvoiceLayoutRequest r, InvoiceLayoutService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SaveAsync(layoutType, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        l.MapGet("/{layoutType}/preview", async (string layoutType, InvoicePrintService svc, CancellationToken ct) =>
            File(await svc.LayoutPreviewAsync(layoutType, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue);
        l.MapPost("/{layoutType}/preview", async (string layoutType, InvoiceLayoutRequest r, InvoicePrintService svc, CancellationToken ct) =>
            File(await svc.LayoutPreviewAsync(layoutType, ct, r).ConfigureAwait(false))).RequirePermission(PermissionKeys.InvIssue).AllowWhenReadOnly();   // nothing is stored
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
