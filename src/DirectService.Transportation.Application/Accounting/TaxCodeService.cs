using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Accounting.Domain.Acc;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Accounting;

/// <summary>
/// Tax codes (Settings → Tax codes, core.settings.manage; DT-257): code, rate, Arabic and English description, status and order. Only active
/// codes are offered on journal lines. A code used on a posted journal line cannot be deleted or change its rate — deactivate it and create a
/// new one; its descriptions and order may still change. Soft delete, audited like every company record.
/// </summary>
public sealed class TaxCodeService(IAppDbContext db, IUrlTokenService tokens, IChangeContext changes, MasterData.MasterDataSupport support)
{
    public const string EntityType = "ACC.TaxCode";
    public const int NameMax = 100;

    public async Task<IReadOnlyList<TaxCodeResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<TaxCode>().AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Code).ToListAsync(ct).ConfigureAwait(false);
        var ids = rows.Select(r => r.Id).ToList();
        var used = (await db.Set<EntryLine>().AsNoTracking().Where(l => l.TaxCodeId != null && ids.Contains(l.TaxCodeId.Value)).Select(l => l.TaxCodeId!.Value)
            .Distinct().ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return rows.Select(t => ToResponse(t, used.Contains(t.Id))).ToList();
    }

    /// <summary>Active codes for the journal lines' pick list, in their order.</summary>
    public async Task<IReadOnlyList<TaxCodeRef>> OptionsAsync(CancellationToken ct) =>
        (await db.Set<TaxCode>().AsNoTracking().Where(t => t.Status == AccountingCodes.Active).OrderBy(t => t.SortOrder).ThenBy(t => t.Code)
            .ToListAsync(ct).ConfigureAwait(false)).Select(Ref).ToList();

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<TaxCode>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false);
    }

    public async Task<TaxCodeResponse> CreateAsync(TaxCodeRequest r, CancellationToken ct)
    {
        var v = Validate(r);
        if (await db.Set<TaxCode>().AnyAsync(t => t.Code == v.Code, ct).ConfigureAwait(false)) throw Fail.Conflict(ErrorCodes.DuplicateKey, v.Code);
        var t = new TaxCode { Code = v.Code, Rate = v.Rate, NameAr = v.NameAr, NameEn = v.NameEn, SortOrder = v.SortOrder ?? await NextOrderAsync(ct).ConfigureAwait(false) };
        db.Set<TaxCode>().Add(t);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(t, false);
    }

    /// <summary>Saves a code; the code itself and the rate cannot change once it is used on a posted line (DT-257).</summary>
    public async Task<TaxCodeResponse> UpdateAsync(string token, TaxCodeRequest r, CancellationToken ct)
    {
        var v = Validate(r);
        var t = await support.FindAsync<TaxCode>(token, ct).ConfigureAwait(false);
        var inUse = await InUseAsync(t.Id, ct).ConfigureAwait(false);
        if (inUse && (v.Rate != t.Rate || v.Code != t.Code)) throw Fail.RuleOn(v.Rate != t.Rate ? "rate" : "code", ErrorCodes.TaxCodeInUse, t.Code);
        if (v.Code != t.Code && await db.Set<TaxCode>().AnyAsync(x => x.Code == v.Code && x.Id != t.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, v.Code);
        db.ExpectRowVersion(t, r.RowVersion);
        t.Code = v.Code;
        t.Rate = v.Rate;
        t.NameAr = v.NameAr;
        t.NameEn = v.NameEn;
        if (v.SortOrder is int order) t.SortOrder = order;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(t, inUse);
    }

    /// <summary>Deactivate (no longer offered; posted lines keep it) or activate again.</summary>
    public async Task<TaxCodeResponse> SetStatusAsync(string token, bool active, string? rowVersion, CancellationToken ct)
    {
        var t = await support.FindAsync<TaxCode>(token, ct).ConfigureAwait(false);
        db.ExpectRowVersion(t, rowVersion);
        t.Status = active ? AccountingCodes.Active : AccountingCodes.Archived;
        changes.SetAction(t, active ? "Activate" : "Deactivate");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(t, await InUseAsync(t.Id, ct).ConfigureAwait(false));
    }

    /// <summary>Soft delete with a reason; refused while a posted journal line uses the code.</summary>
    public async Task DeleteAsync(string token, string? reason, CancellationToken ct)
    {
        var why = TextNormalizer.CleanText(reason) ?? throw Fail.Field("reason", ErrorCodes.ReasonRequired);
        var t = await support.FindAsync<TaxCode>(token, ct).ConfigureAwait(false);
        if (await InUseAsync(t.Id, ct).ConfigureAwait(false)) throw Fail.Rule(ErrorCodes.TaxCodeInUse, t.Code);
        t.DeleteReason = why.Length <= 500 ? why : why[..500];
        db.Set<TaxCode>().Remove(t);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Used = on a posted journal line (a taxed line or its automatic tax line); reversed and void entries count too.</summary>
    private Task<bool> InUseAsync(long id, CancellationToken ct) => db.Set<EntryLine>().AsNoTracking().AnyAsync(l => l.TaxCodeId == id, ct);

    private async Task<int> NextOrderAsync(CancellationToken ct) =>
        (await db.Set<TaxCode>().AsNoTracking().MaxAsync(t => (int?)t.SortOrder, ct).ConfigureAwait(false) ?? 0) + 10;

    private static (string Code, decimal Rate, string NameAr, string? NameEn, int? SortOrder) Validate(TaxCodeRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var code = TextNormalizer.CleanCode(r.Code);
        var nameAr = TextNormalizer.CleanText(r.NameAr);
        var nameEn = TextNormalizer.CleanText(r.NameEn);
        new Validator()
            .Require("code", code).Check(code is null || AccountingCodeRule.IsCode(code), "code")
            .Check(r.Rate is not null, "rate", ErrorCodes.ValidationRequired)
            .Check(r.Rate is null || (r.Rate is >= 0 and <= 100 && decimal.Round(r.Rate.Value, 2) == r.Rate), "rate", ErrorCodes.ValidationRange)
            .Require("nameAr", nameAr).MaxLength("nameAr", nameAr, NameMax)
            .MaxLength("nameEn", nameEn, NameMax)
            .Check(r.SortOrder is null or (>= 0 and <= 9999), "sortOrder", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        return (code!, r.Rate!.Value, nameAr!, nameEn, r.SortOrder);
    }

    private TaxCodeRef Ref(TaxCode t) => new(tokens.Encode<TaxCode>(t.PublicId), t.Code, t.Rate, t.NameAr, t.NameEn);

    private TaxCodeResponse ToResponse(TaxCode t, bool inUse) =>
        new(tokens.Encode<TaxCode>(t.PublicId), t.Code, t.Rate, t.NameAr, t.NameEn, t.Status, t.SortOrder, inUse, Convert.ToBase64String(t.RowVersion));
}
