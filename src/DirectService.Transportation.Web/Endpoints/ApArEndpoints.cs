using DirectService.Transportation.Application.ApAr;
using DirectService.Transportation.Application.EInvoicing;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Accounts payable and receivable (B9, 7.6.4): accounting parties, supplier bills, receivable documents, payments and collections,
/// returns, ageing and the e-invoicing of receivables (D-15). acct.apar manages; acct.view reads (PRD 3.4); ageing also with
/// reports.view ("all reports").
/// </summary>
internal static class ApArEndpoints
{
    public static void MapApArEndpoints(this RouteGroupBuilder company)
    {
        var apar = company.MapGroup("/apar");
        string[] read = [PermissionKeys.AcctApar, PermissionKeys.AcctView];

        var p = apar.MapGroup("/parties").WithTags("Accounting parties");
        p.MapGet("/", async ([AsParameters] ApArPartyListQuery q, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        p.MapGet("/options", async (string? role, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.OptionsAsync(role, ct).ConfigureAwait(false)))
            .RequireAnyPermission(PermissionKeys.AcctApar, PermissionKeys.AcctView, PermissionKeys.AcctPost, PermissionKeys.ReportsView);   // journal and statement party pick lists (DT-147)
        p.MapGet("/{party}", async (string party, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(party, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        p.MapPost("/", async (ApArPartyRequest req, ApArPartyService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<ApArPartyResponse>, Ok<ApArPartyResponse>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.AcctApar);
        p.MapPut("/{party}", async (string party, ApArPartyRequest req, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(party, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);
        p.MapPost("/{party}/archive", async (string party, StatusChangeRequest req, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(party, false, req.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);
        p.MapPost("/{party}/restore", async (string party, StatusChangeRequest req, ApArPartyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetStatusAsync(party, true, req.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);
        p.MapPost("/{party}/delete", async (string party, DeleteRequest req, ApArPartyService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(party, req.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequirePermission(PermissionKeys.AcctApar);

        MapDocuments(apar.MapGroup("/bills").WithTags("Supplier bills"), "bills", read);
        MapDocuments(apar.MapGroup("/receivables").WithTags("Receivable documents"), "receivables", read);

        var pay = apar.MapGroup("/payments").WithTags("AP/AR payments and collections");
        pay.MapGet("/", async ([AsParameters] ApArPaymentListQuery q, string? kind, ApArPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(kind ?? "bills", q, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        pay.MapGet("/export", async ([AsParameters] ApArPaymentListQuery q, string? kind, string? format, ApArPaymentService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(kind ?? "bills", q, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        pay.MapGet("/{payment}", async (string payment, ApArPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(payment, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        pay.MapGet("/{payment}/print", async (string payment, string? format, ApArPrintService svc, CancellationToken ct) =>
            File(await svc.PaymentAsync(payment, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        pay.MapPost("/", async (ApArPaymentRequest req, ApArPaymentService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<ApArPaymentDetails>, Ok<ApArPaymentDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.AcctApar);
        pay.MapPost("/{payment}/void", async (string payment, CashVoidRequest req, ApArPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(payment, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);

        var ret = apar.MapGroup("/returns").WithTags("AP/AR returns");
        ret.MapGet("/", async ([AsParameters] ApArReturnListQuery q, string? kind, ApArReturnService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(kind ?? "bills", q, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        ret.MapGet("/export", async ([AsParameters] ApArReturnListQuery q, string? kind, string? format, ApArReturnService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(kind ?? "bills", q, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        ret.MapGet("/{return}", async (string @return, ApArReturnService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(@return, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        ret.MapGet("/{return}/print", async (string @return, string? format, ApArPrintService svc, CancellationToken ct) =>
            File(await svc.ReturnAsync(@return, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        ret.MapGet("/{return}/e-invoice", async (string @return, ApArReturnService returns, EInvoiceService svc, Application.MasterData.MasterDataSupport support, CancellationToken ct) =>
        {
            var x = await support.FindAsync<DirectService.Accounting.Domain.Acc.ApArReturn>(@return, ct).ConfigureAwait(false);
            return TypedResults.Ok(await svc.ForApArAsync(null, x.Id, ct).ConfigureAwait(false));
        }).RequireAnyPermission(read);
        ret.MapPost("/", async (ApArReturnRequest req, ApArReturnService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<ApArReturnDetails>, Ok<ApArReturnDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.AcctApar);
        ret.MapPost("/{return}/void", async (string @return, CashVoidRequest req, ApArReturnService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(@return, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);

        var ageing = apar.MapGroup("/ageing").WithTags("Ageing");
        string[] reports = [PermissionKeys.AcctApar, PermissionKeys.AcctView, PermissionKeys.ReportsView];
        ageing.MapGet("/{kind}", async (string kind, DateOnly? asOf, AgeingService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.AgeingAsync(kind, asOf, ct).ConfigureAwait(false))).RequireAnyPermission(reports);
        ageing.MapGet("/{kind}/export", async (string kind, DateOnly? asOf, string? format, AgeingService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(kind, asOf, format, ct).ConfigureAwait(false))).RequireAnyPermission(reports);

        var e = apar.MapGroup("/e-invoice-submissions").WithTags("Receivables e-invoicing");
        e.MapGet("/", async (string? status, DateOnly? from, DateOnly? to, int? page, int? pageSize, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListApArAsync(status, from, to, page ?? 1, pageSize ?? 50, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        e.MapGet("/{submission}", async (string submission, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetApArAsync(submission, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        e.MapPost("/{submission}/submit", async (string submission, EInvoiceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SubmitApArAsync(submission, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);
    }

    private static void MapDocuments(RouteGroupBuilder g, string kind, string[] read)
    {
        g.MapGet("/", async ([AsParameters] ApArDocumentListQuery q, ApArDocumentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(kind, q, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        g.MapGet("/export", async ([AsParameters] ApArDocumentListQuery q, string? format, ApArDocumentService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(kind, q, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        g.MapGet("/{document}", async (string document, ApArDocumentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(kind, document, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        g.MapGet("/{document}/print", async (string document, string? format, ApArPrintService svc, CancellationToken ct) =>
            File(await svc.DocumentAsync(kind, document, format, ct).ConfigureAwait(false))).RequireAnyPermission(read);
        g.MapPost("/", async (ApArDocumentRequest req, ApArDocumentService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(kind, req, ct).ConfigureAwait(false);
            return created ? (Results<Created<ApArDocumentDetails>, Ok<ApArDocumentDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.AcctApar);
        g.MapPost("/{document}/void", async (string document, CashVoidRequest req, ApArDocumentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(kind, document, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctApar);
        if (kind == "receivables")
        {
            g.MapGet("/{document}/e-invoice", async (string document, ApArDocumentService docs, EInvoiceService svc, CancellationToken ct) =>
            {
                var d = await docs.FindAsync(ApArCodes.Receivable, document, ct).ConfigureAwait(false);
                return TypedResults.Ok(await svc.ForApArAsync(d.Id, null, ct).ConfigureAwait(false));
            }).RequireAnyPermission(read);
        }
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
