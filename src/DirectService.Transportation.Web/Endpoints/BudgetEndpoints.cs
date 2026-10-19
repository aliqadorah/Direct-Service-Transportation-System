using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Budgets;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Budgets and Budget vs Actual (revision 6 part B, owner change request 15 Oct 2026): acc.budget.view (list, read, history, report, exports),
/// acc.budget.manage (create, copy from actuals, save, import, delete drafts), acc.budget.approve (approve, return to draft).
/// </summary>
internal static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this RouteGroupBuilder company)
    {
        var g = company.MapGroup("/accounting/budgets").WithTags("Budgets");
        g.MapGet("/", async (int? year, BudgetService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(year, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetView);
        g.MapPost("/", async (CreateBudgetRequest r, BudgetService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(r, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetManage);
        g.MapGet("/account-options", async (BudgetService svc, CancellationToken ct) => TypedResults.Ok(await svc.AccountOptionsAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetView);
        g.MapGet("/{budget}/import/template", async (string budget, BudgetImportService svc, CancellationToken ct) => File(await svc.TemplateAsync(budget, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetManage);
        g.MapGet("/{budget}", async (string budget, BudgetService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(budget, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetView);
        g.MapGet("/{budget}/history", async (string budget, BudgetService svc, CancellationToken ct) => TypedResults.Ok(await svc.HistoryAsync(budget, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.BudgetView);
        g.MapPut("/{budget}", async (string budget, BudgetRequest r, BudgetService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(budget, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.BudgetManage);
        g.MapPost("/{budget}/delete", async (string budget, RowVersionRequest r, BudgetService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(budget, r.RowVersion, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequirePermission(PermissionKeys.BudgetManage);
        g.MapPost("/{budget}/approve", async (string budget, RowVersionRequest r, BudgetService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ApproveAsync(budget, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.BudgetApprove);
        g.MapPost("/{budget}/return-to-draft", async (string budget, RowVersionRequest r, BudgetService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReturnToDraftAsync(budget, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.BudgetApprove);
        g.MapPost("/{budget}/import/preview", async (string budget, IFormFile file, BudgetImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewAsync(budget, s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.BudgetManage).DisableAntiforgery().Upload();
        g.MapPost("/{budget}/import", async (string budget, IFormFile file, BudgetImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportAsync(budget, s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.BudgetManage).DisableAntiforgery().Upload();

        var reports = company.MapGroup("/accounting/reports").WithTags("Budgets");
        reports.MapGet("/budget-vs-actual", async ([AsParameters] BudgetVsActualQuery q, BudgetReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReportAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.BudgetView);
        reports.MapGet("/budget-vs-actual/export", async ([AsParameters] BudgetVsActualQuery q, string? format, BudgetReportService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.BudgetView);
    }

    private const long UploadLimit = 11 * 1024 * 1024;

    /// <summary>Upload endpoints: the body is limited before the form is buffered (SR-B2-03); the file is checked again by the reader.</summary>
    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder b) => b
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimit))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = UploadLimit });

    private static Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
