using DirectService.Accounting.Application.Books;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Ledger;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Financial reports (B9, 7.6.3) and the maintenance zone (7.6.7). The general ledger, trial balance, statements of profit and loss and
/// financial position and the cost-centre report need acct.view; the account statement, cash/bank statement and balances and the
/// sub-ledger reconciliation are also listed among the reports (7.7.3) and open to reports.view. Maintenance needs acct.maint.
/// </summary>
internal static class LedgerReportEndpoints
{
    public static void MapLedgerReportEndpoints(this RouteGroupBuilder company)
    {
        var r = company.MapGroup("/accounting/reports").WithTags("Financial reports");
        string[] listed = [PermissionKeys.AcctView, PermissionKeys.ReportsView];

        r.MapGet("/general-ledger", async ([AsParameters] LedgerQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.LedgerAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/general-ledger/export", async ([AsParameters] LedgerQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.LedgerAsync(q, format, false, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/account-statement", async ([AsParameters] LedgerQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.LedgerAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/account-statement/export", async ([AsParameters] LedgerQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.LedgerAsync(q, format, true, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        // Revision 6: the statement of several accounts (multi-select, code range or all) — replaces the general ledger screen.
        r.MapGet("/account-statements", async ([AsParameters] AccountStatementsQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.StatementsAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/account-statements/export", async ([AsParameters] AccountStatementsQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.StatementsAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/trial-balance", async ([AsParameters] TrialBalanceQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.TrialBalanceAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/trial-balance/export", async ([AsParameters] TrialBalanceQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.TrialBalanceAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/profit-loss", async ([AsParameters] ProfitLossQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ProfitLossAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/profit-loss/export", async ([AsParameters] ProfitLossQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.ProfitLossAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/balance-sheet", async ([AsParameters] BalanceSheetQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.BalanceSheetAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/balance-sheet/export", async ([AsParameters] BalanceSheetQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.BalanceSheetAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/cost-centres", async ([AsParameters] PeriodQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CostCentresAsync(q, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/cost-centres/export", async ([AsParameters] PeriodQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.CostCentresAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        r.MapGet("/cash-bank-statement", async ([AsParameters] CashBankStatementQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CashBankStatementAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/cash-bank-statement/export", async ([AsParameters] CashBankStatementQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.CashBankAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/cash-bank-balances", async (DateOnly? asOf, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CashBankBalancesAsync(asOf, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/reconciliation", async ([AsParameters] ReconciliationQuery q, LedgerReportService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReconciliationAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(listed);
        r.MapGet("/reconciliation/export", async ([AsParameters] ReconciliationQuery q, string? format, LedgerReportExports svc, CancellationToken ct) =>
            File(await svc.ReconciliationAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(listed);

        var m = company.MapGroup("/accounting/maintenance").WithTags("Maintenance zone");
        m.MapGet("/log", async (MaintenanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.LogAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctMaint);
        m.MapPost("/backup", async (MaintenanceService svc, CancellationToken ct) =>
            File(await svc.BackupAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctMaint);
        m.MapPost("/{action}/preview", async (string action, MaintenanceRequest req, MaintenanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PreviewAsync(action, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctMaint);
        m.MapPost("/{action}", async (string action, MaintenanceRequest req, MaintenanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RunAsync(action, req, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctMaint);
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
