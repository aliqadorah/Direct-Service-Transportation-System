using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Accounting core and ledger integration (B2): chart of accounts, opening balances, posting keys, cost centres,
/// cash boxes, bank accounts, periods, journal (manual and automatic), pending postings and journal export.
/// Permission keys per PRD 3.4: acct.view / acct.setup / acct.post, je.view / je.export, settings.manage for the
/// mapping (FR-092) and periods (FR-132).
/// </summary>
internal static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this RouteGroupBuilder company)
    {
        var acc = company.MapGroup("/accounting");
        MapChart(acc.MapGroup("/accounts").WithTags("Chart of accounts"));
        MapOpenings(acc.MapGroup("/opening-balances").WithTags("Opening balances"));
        MapPostingKeys(acc);
        MapCostCentres(acc.MapGroup("/cost-centres").WithTags("Cost centres"));
        acc.MapGet("/money-accounts/options", async (CashBankService svc, CancellationToken ct) => TypedResults.Ok(await svc.MoneyOptionsAsync(ct).ConfigureAwait(false)))
            .WithTags("Cash boxes").RequireAnyPermission(PermissionKeys.RcptManage, PermissionKeys.DrvAdvance, PermissionKeys.DrvSettle, PermissionKeys.AcctApar, PermissionKeys.AcctView, PermissionKeys.AcctPost, PermissionKeys.ReportsView);
        MapCashBoxes(acc.MapGroup("/cash-boxes").WithTags("Cash boxes"));
        MapBankAccounts(acc.MapGroup("/bank-accounts").WithTags("Bank accounts"));
        MapPeriods(acc.MapGroup("/periods").WithTags("Periods"));
        MapJournal(acc.MapGroup("/journal-entries").WithTags("Journal"));
        MapLedger(company.MapGroup("/ledger").WithTags("Ledger integration"));
    }

    private static void MapChart(RouteGroupBuilder g)
    {
        g.MapGet("/", async (string? status, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(status, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/options", async (ChartOfAccountsService svc, CancellationToken ct) => TypedResults.Ok(await svc.OptionsAsync(ct).ConfigureAwait(false)))
            .RequireAnyPermission(PermissionKeys.AcctView, PermissionKeys.AcctPost, PermissionKeys.AcctApar, PermissionKeys.AcctSetup, PermissionKeys.SettingsManage, PermissionKeys.ReportsView);
        g.MapGet("/export", async (ChartImportService svc, CancellationToken ct) => File(await svc.ExportAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/{account}", async (string account, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(account, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapPost("/", async (AccountRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CreateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPut("/{account}", async (string account, AccountRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(account, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{account}/move", async (string account, MoveAccountRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.MoveAsync(account, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{account}/deactivate", async (string account, StatusChangeRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.DeactivateAsync(account, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{account}/activate", async (string account, StatusChangeRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ActivateAsync(account, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{account}/delete", async (string account, DeleteRequest r, ChartOfAccountsService svc, CancellationToken ct) =>
        { await svc.DeleteAsync(account, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); }).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/template", async (ChartOfAccountsService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ApplyTemplateAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/import/preview", async (IFormFile file, ChartImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewAsync(s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.AcctSetup).DisableAntiforgery().Upload();
        g.MapPost("/import", async (IFormFile file, ChartImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportAsync(s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.AcctSetup).DisableAntiforgery().Upload(); // antiforgery header checked by AntiforgeryFilter
    }

    private static void MapOpenings(RouteGroupBuilder g)
    {
        g.MapGet("/", async (OpeningBalanceService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapPut("/", async (OpeningBalancesRequest r, OpeningBalanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/post", async (PostOpeningRequest r, OpeningBalanceService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PostAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
    }

    private static void MapPostingKeys(RouteGroupBuilder acc)
    {
        var g = acc.MapGroup("/posting-keys").WithTags("Account mapping");
        g.MapGet("/", async (PostingKeyService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.SettingsManage);
        g.MapPut("/{postingKey}", async (string postingKey, PostingKeyRequest r, PostingKeyService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetAsync(postingKey, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        // Banner state for the app shell: every member sees the "complete account mapping" and opening-difference banners.
        acc.MapGet("/status", async (PostingKeyService svc, CancellationToken ct) => TypedResults.Ok(await svc.StatusAsync(ct).ConfigureAwait(false)))
            .WithTags("Account mapping").RequireAuthorization(PermissionPolicyProvider.Member).WithMetadata(new MemberEndpointMetadata());
    }

    private static void MapCostCentres(RouteGroupBuilder g)
    {
        g.MapGet("/options", async (CashBankService svc, CancellationToken ct) => TypedResults.Ok(await svc.CostCentreOptionsAsync(ct).ConfigureAwait(false)))
            .RequireAnyPermission(PermissionKeys.AcctView, PermissionKeys.AcctPost, PermissionKeys.AcctApar, PermissionKeys.ReportsView);
        g.MapGet("/", async (CashBankService svc, CancellationToken ct) => TypedResults.Ok(await svc.CostCentresAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapPost("/", async (CostCentreRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CreateCostCentreAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPut("/{costCentre}", async (string costCentre, CostCentreRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateCostCentreAsync(costCentre, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{costCentre}/archive", async (string costCentre, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetCostCentreStatusAsync(costCentre, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{costCentre}/restore", async (string costCentre, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetCostCentreStatusAsync(costCentre, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{costCentre}/delete", async (string costCentre, DeleteRequest r, CashBankService svc, CancellationToken ct) =>
        { await svc.DeleteCostCentreAsync(costCentre, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); })
            .RequirePermission(PermissionKeys.AcctSetup);
    }

    private static void MapCashBoxes(RouteGroupBuilder g)
    {
        g.MapGet("/", async (CashBankService svc, CancellationToken ct) => TypedResults.Ok(await svc.CashBoxesAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapPost("/", async (CashBoxRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CreateCashBoxAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPut("/{cashBox}", async (string cashBox, CashBoxRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateCashBoxAsync(cashBox, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{cashBox}/archive", async (string cashBox, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetCashBoxStatusAsync(cashBox, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{cashBox}/restore", async (string cashBox, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetCashBoxStatusAsync(cashBox, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{cashBox}/delete", async (string cashBox, DeleteRequest r, CashBankService svc, CancellationToken ct) =>
        { await svc.DeleteCashBoxAsync(cashBox, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); })
            .RequirePermission(PermissionKeys.AcctSetup);
    }

    private static void MapBankAccounts(RouteGroupBuilder g)
    {
        g.MapGet("/", async (CashBankService svc, CancellationToken ct) => TypedResults.Ok(await svc.BankAccountsAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapPost("/", async (BankAccountRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CreateBankAccountAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPut("/{bankAccount}", async (string bankAccount, BankAccountRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateBankAccountAsync(bankAccount, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{bankAccount}/archive", async (string bankAccount, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetBankAccountStatusAsync(bankAccount, false, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{bankAccount}/restore", async (string bankAccount, StatusChangeRequest r, CashBankService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SetBankAccountStatusAsync(bankAccount, true, r.RowVersion, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctSetup);
        g.MapPost("/{bankAccount}/delete", async (string bankAccount, DeleteRequest r, CashBankService svc, CancellationToken ct) =>
        { await svc.DeleteBankAccountAsync(bankAccount, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); })
            .RequirePermission(PermissionKeys.AcctSetup);
    }

    private static void MapPeriods(RouteGroupBuilder g)
    {
        g.MapGet("/", async (int? year, PeriodService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(year, ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/{month}/pre-close-check", async (string month, PeriodService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.PreCloseCheckAsync(month, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        g.MapPost("/{month}/close", async (string month, PeriodActionRequest r, PeriodService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CloseAsync(month, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
        g.MapPost("/{month}/reopen", async (string month, PeriodActionRequest r, PeriodService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ReopenAsync(month, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.SettingsManage);
    }

    private static void MapJournal(RouteGroupBuilder g)
    {
        g.MapGet("/", async ([AsParameters] JournalQuery q, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, automaticOnly: false, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/export", async ([AsParameters] JournalQuery q, string? format, JournalService svc, CancellationToken ct) =>
            File(await svc.ExportListAsync(q, format, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/{entry}", async (string entry, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(entry, automaticOnly: false, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapGet("/{entry}/voucher", async (string entry, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoucherAsync(entry, automaticOnly: false, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctView);
        g.MapPost("/", async (ManualEntryRequest r, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.CreateManualAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctPost);
        g.MapPut("/{entry}", async (string entry, ManualEntryRequest r, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.EditManualAsync(entry, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctPost);
        g.MapPost("/{entry}/void", async (string entry, VoidRequest r, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoidManualAsync(entry, r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.AcctPost);
        g.MapPost("/import/preview", async (IFormFile file, JournalImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.PreviewAsync(s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.AcctPost).DisableAntiforgery().Upload();
        g.MapPost("/import", async (IFormFile file, JournalImportService svc, CancellationToken ct) =>
        {
            await using var s = file.OpenReadStream();
            return TypedResults.Ok(await svc.ImportAsync(s, file.FileName, file.Length, ct).ConfigureAwait(false));
        }).RequirePermission(PermissionKeys.AcctPost).DisableAntiforgery().Upload();
    }

    private static void MapLedger(RouteGroupBuilder g)
    {
        g.MapGet("/entries", async ([AsParameters] JournalQuery q, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, automaticOnly: true, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeView);
        g.MapGet("/entries/{entry}", async (string entry, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.GetAsync(entry, automaticOnly: true, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeView);
        g.MapGet("/entries/{entry}/voucher", async (string entry, JournalService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.VoucherAsync(entry, automaticOnly: true, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeView);
        g.MapGet("/pending-postings", async (PendingPostingService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeView);
        g.MapPost("/pending-postings/{pending}/retry", async (string pending, PendingPostingService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RetryOneAsync(pending, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeExport);
        g.MapPost("/pending-postings/retry-all", async (PendingPostingService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RetryAllAsync(ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeExport);
        g.MapGet("/exports", async (JournalExportService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct).ConfigureAwait(false)))
            .RequirePermission(PermissionKeys.JeExport);
        g.MapPost("/exports", async (JournalExportRequest r, JournalExportService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(r, ct).ConfigureAwait(false))).RequirePermission(PermissionKeys.JeExport).AllowWhenReadOnly();
    }

    private const long UploadLimit = 11 * 1024 * 1024;

    /// <summary>Upload endpoints: the body is limited before the form is buffered (SR-B2-03); the file is checked again by the reader.</summary>
    private static RouteHandlerBuilder Upload(this RouteHandlerBuilder b) => b
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimit))
        .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestFormLimitsAttribute { MultipartBodyLengthLimit = UploadLimit });

    private static Microsoft.AspNetCore.Http.HttpResults.FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}
