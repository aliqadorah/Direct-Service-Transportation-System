using DirectService.Transportation.Application.Cash;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Cash, drivers and partners (B8, M11): receipts (rcpt.manage; inv.view reads), driver advances (drv.advance; drv.view reads),
/// settlements and partner payments (drv.settle; drv.view reads), driver and partner accounts and statements (drv.view) — PRD 3.4.
/// </summary>
internal static class CashEndpoints
{
    public static void MapCashEndpoints(this RouteGroupBuilder company)
    {
        var r = company.MapGroup("/receipts").WithTags("Receipts");
        string[] rcptRead = [PermissionKeys.RcptManage, PermissionKeys.InvView];
        r.MapGet("/", async ([AsParameters] ReceiptListQuery q, ReceiptService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(rcptRead);
        r.MapGet("/export", async ([AsParameters] ReceiptListQuery q, string? format, ReceiptService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(rcptRead);
        r.MapGet("/{receipt}", async (string receipt, ReceiptService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(receipt, ct).ConfigureAwait(false))).RequireAnyPermission(rcptRead);
        r.MapGet("/{receipt}/print", async (string receipt, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.ReceiptAsync(receipt, format, ct).ConfigureAwait(false))).RequireAnyPermission(rcptRead);
        r.MapPost("/", async (ReceiptRequest req, ReceiptService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<ReceiptDetails>, Ok<ReceiptDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.RcptManage);
        r.MapPost("/{receipt}/void", async (string receipt, CashVoidRequest req, ReceiptService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(receipt, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.RcptManage);

        var a = company.MapGroup("/advances").WithTags("Driver advances");
        string[] advRead = [PermissionKeys.DrvView, PermissionKeys.DrvAdvance];
        a.MapGet("/", async ([AsParameters] AdvanceListQuery q, AdvanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(advRead);
        a.MapGet("/export", async ([AsParameters] AdvanceListQuery q, string? format, AdvanceService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(advRead);
        a.MapGet("/{advance}", async (string advance, AdvanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(advance, ct).ConfigureAwait(false))).RequireAnyPermission(advRead);
        a.MapGet("/{advance}/print", async (string advance, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.AdvanceAsync(advance, format, ct).ConfigureAwait(false))).RequireAnyPermission(advRead);
        a.MapPost("/", async (AdvanceRequest req, AdvanceService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<AdvanceDetails>, Ok<AdvanceDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.DrvAdvance);
        a.MapPut("/{advance}", async (string advance, AdvanceRequest req, AdvanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(advance, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvAdvance);
        a.MapPost("/{advance}/void", async (string advance, CashVoidRequest req, AdvanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(advance, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvAdvance);

        var s = company.MapGroup("/settlements").WithTags("Driver settlements");
        string[] stlRead = [PermissionKeys.DrvView, PermissionKeys.DrvSettle];
        s.MapGet("/", async ([AsParameters] SettlementListQuery q, SettlementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        s.MapGet("/preview", async (string? driver, DateOnly? date, SettlementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PreviewAsync(driver, date, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        s.MapGet("/{settlement}", async (string settlement, SettlementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(settlement, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        s.MapGet("/{settlement}/print", async (string settlement, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.SettlementAsync(settlement, format, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        s.MapPost("/", async (SettlementRequest req, SettlementService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<SettlementDetails>, Ok<SettlementDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.DrvSettle);
        s.MapPost("/{settlement}/void", async (string settlement, CashVoidRequest req, SettlementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(settlement, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvSettle);

        var p = company.MapGroup("/partner-payments").WithTags("Partner payments");
        p.MapGet("/", async ([AsParameters] PartnerPaymentListQuery q, PartnerPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        p.MapGet("/export", async ([AsParameters] PartnerPaymentListQuery q, string? format, PartnerPaymentService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        p.MapGet("/{payment}", async (string payment, PartnerPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(payment, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        p.MapGet("/{payment}/print", async (string payment, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.PartnerPaymentAsync(payment, format, ct).ConfigureAwait(false))).RequireAnyPermission(stlRead);
        p.MapPost("/", async (PartnerPaymentRequest req, PartnerPaymentService svc, CancellationToken ct) =>
        {
            var (value, created) = await svc.CreateAsync(req, ct).ConfigureAwait(false);
            return created ? (Results<Created<PartnerPaymentDetails>, Ok<PartnerPaymentDetails>>)TypedResults.Created((string?)null, value) : TypedResults.Ok(value);
        }).RequirePermission(PermissionKeys.DrvSettle);
        p.MapPost("/{payment}/void", async (string payment, CashVoidRequest req, PartnerPaymentService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidAsync(payment, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvSettle);

        var accounts = company.MapGroup("").WithTags("Driver and partner accounts");
        accounts.MapGet("/drivers/{driver}/account", async (string driver, AccountStatementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.DriverAccountAsync(driver, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
        accounts.MapGet("/drivers/{driver}/statement", async (string driver, DateOnly? from, DateOnly? to, AccountStatementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.DriverStatementAsync(driver, from, to, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
        accounts.MapGet("/drivers/{driver}/statement/export", async (string driver, DateOnly? from, DateOnly? to, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.DriverStatementAsync(driver, from, to, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
        accounts.MapGet("/partners/{partner}/account", async (string partner, AccountStatementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PartnerAccountAsync(partner, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
        accounts.MapGet("/partners/{partner}/statement", async (string partner, DateOnly? from, DateOnly? to, AccountStatementService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PartnerStatementAsync(partner, from, to, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
        accounts.MapGet("/partners/{partner}/statement/export", async (string partner, DateOnly? from, DateOnly? to, string? format, VoucherPrintService svc, CancellationToken ct) =>
            File(await svc.PartnerStatementAsync(partner, from, to, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.DrvView);
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
