using System.Globalization;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// The transport operation settings of the company settings (core.settings.manage; FR-170): weight bases, shortage tolerance, tariff lock,
/// invoice approval, driver wage accrual, with the data-migration status shown beside them. Every change is audited old → new (FR-168) by
/// the interceptor. The module's section of the company settings since Stage 2 session 14 (the profile, logo, market, numbering and
/// data-migration status are the platform's <c>CompanySettingsService</c>); the module's services read their settings here.
/// </summary>
public sealed class OperationSettingsService(IAppDbContext db, IRequestContext ctx, IChangeContext changes)
{
    public async Task<OperationSettingsResponse> GetOperationsAsync(CancellationToken ct)
    {
        var s = await AllSettingsAsync(ct).ConfigureAwait(false);
        var migration = await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.MigrationCompletedAt).SingleAsync(ct).ConfigureAwait(false);
        return new OperationSettingsResponse(
            Bool(s, SettingKeys.BillOnUnloadedWeight), Bool(s, SettingKeys.PayDriverOnLoadedWeight),
            Dec(s, SettingKeys.ShortageTolerancePct), Bool(s, SettingKeys.LockTariffs), Bool(s, SettingKeys.InvoiceApproval),
            Bool(s, SettingKeys.AccrueDriverWage), migration);
    }

    public async Task<OperationSettingsResponse> UpdateOperationsAsync(OperationSettingsRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        new Validator().Check(r.ShortageTolerancePct is >= 0 and <= 100, "shortageTolerancePct", ErrorCodes.ValidationRange).ThrowIfInvalid();
        var current = await GetOperationsAsync(ct).ConfigureAwait(false);
        var weightBasisChanged = current.BillOnUnloadedWeight != r.BillOnUnloadedWeight || current.PayDriverOnLoadedWeight != r.PayDriverOnLoadedWeight;
        if (weightBasisChanged && !r.ConfirmWeightBasisChange)
        {
            // FR-170: applies to new trips only; saved trips keep their amounts.
            throw new AppException(ErrorCodes.ConfirmationRequired, 422, "confirmWeightBasisChange",
                ctx.Language == "en" ? "the new weight basis applies to new trips only; saved trips keep their amounts."
                                     : "أساس الوزن الجديد يُطبّق على الرحلات الجديدة فقط، وتحتفظ الرحلات المحفوظة بمبالغها.");
        }
        await SetSettingAsync(SettingKeys.BillOnUnloadedWeight, B(r.BillOnUnloadedWeight), ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.PayDriverOnLoadedWeight, B(r.PayDriverOnLoadedWeight), ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.ShortageTolerancePct, r.ShortageTolerancePct.ToString(CultureInfo.InvariantCulture), ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.LockTariffs, B(r.LockTariffs), ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.InvoiceApproval, B(r.InvoiceApproval), ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.AccrueDriverWage, B(r.AccrueDriverWage), ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetOperationsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The value of one setting of the current company; null when it was never saved.</summary>
    public Task<string?> SettingAsync(string key, CancellationToken ct) => CompanySettingValues.ValueAsync(db, key, ct);

    private Task<Dictionary<string, string>> AllSettingsAsync(CancellationToken ct) => CompanySettingValues.AllAsync(db, ct);

    private Task SetSettingAsync(string key, string value, CancellationToken ct) => CompanySettingValues.SetAsync(db, changes, key, value, ct);

    private static bool Bool(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) ? v == "true" : SettingKeys.Defaults[key] == "true";

    private static decimal Dec(Dictionary<string, string> s, string key) =>
        decimal.Parse(s.TryGetValue(key, out var v) ? v : SettingKeys.Defaults[key], CultureInfo.InvariantCulture);

    private static string B(bool v) => v ? "true" : "false";
}
