using System.Globalization;
using System.Text.RegularExpressions;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Configuration;

/// <summary>
/// Company settings (core.settings.manage; FR-168 … FR-171, 7.9.1): profile and logo, market, transport
/// operation settings, numbering, data-migration status. Every change is audited old → new (FR-168)
/// by the interceptor. Accounting groups (mapping, periods) and e-invoicing live in their modules.
/// </summary>
public sealed partial class CompanySettingsService(IAppDbContext db, IRequestContext ctx, IChangeContext changes, IUrlTokenService tokens, TimeProvider clock)
{
    public const int MaxLogoBytes = 512 * 1024;

    // ------------------------------------------------------------ profile

    public async Task<CompanyProfileResponse> GetProfileAsync(CancellationToken ct)
    {
        var c = await Company(ct).ConfigureAwait(false);
        return new CompanyProfileResponse(c.Name, c.NameEn, c.TaxNumber, c.CommercialRegNo, c.Address, c.Phone, c.Email,
            c.LogoFile is { Length: > 0 }, Convert.ToBase64String(c.RowVersion));
    }

    public async Task<CompanyProfileResponse> UpdateProfileAsync(CompanyProfileRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var c = await Company(ct).ConfigureAwait(false);
        var name = TextNormalizer.CleanText(r.Name);
        var phone = TextNormalizer.CleanCode(r.Phone);
        var mail = r.Email?.Trim();
        var phonePattern = await SettingAsync(SettingKeys.PhonePattern, ct).ConfigureAwait(false);
        new Validator()
            .Require("name", name).MaxLength("name", name, 200)
            .MaxLength("nameEn", r.NameEn, 200)
            .MaxLength("taxNumber", r.TaxNumber, 30).MaxLength("commercialRegNo", r.CommercialRegNo, 30)
            .MaxLength("address", r.Address, 300)
            .Check(phone is null || Regex.IsMatch(phone, phonePattern ?? SettingKeys.JordanPhonePattern, RegexOptions.None, TimeSpan.FromSeconds(1)), "phone")
            .Check(string.IsNullOrEmpty(mail) || EmailFormat.IsValid(mail), "email").MaxLength("email", mail, 150)
            .ThrowIfInvalid();
        if (await db.Set<Company>().AnyAsync(x => x.Id != c.Id && x.Name == name, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name!);
        db.ExpectRowVersion(c, r.RowVersion);
        c.Name = name!;
        c.NameEn = TextNormalizer.CleanText(r.NameEn);
        c.TaxNumber = TextNormalizer.CleanCode(r.TaxNumber);
        c.CommercialRegNo = TextNormalizer.CleanCode(r.CommercialRegNo);
        c.Address = TextNormalizer.CleanText(r.Address);
        c.Phone = phone;
        c.Email = string.IsNullOrEmpty(mail) ? null : mail;
        changes.SetAction(c, "SettingChange");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetProfileAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Company logo (printed on documents). PNG or JPEG, at most 512 KB, checked by content (SEC-13).</summary>
    public async Task SetLogoAsync(byte[] content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var isPng = content.Length > 8 && content.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var isJpeg = content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF;
        new Validator()
            .Check(isPng || isJpeg, "logo")
            .Check(content.Length <= MaxLogoBytes, "logo", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        var c = await Company(ct).ConfigureAwait(false);
        c.LogoFile = content;
        changes.SetAction(c, "SettingChange");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<byte[]?> GetLogoAsync(CancellationToken ct) =>
        await db.Set<Company>().AsNoTracking().Where(c => c.Id == ctx.CompanyId).Select(c => c.LogoFile).SingleOrDefaultAsync(ct).ConfigureAwait(false);

    // ------------------------------------------------------------ market

    public async Task<MarketSettingsResponse> GetMarketAsync(CancellationToken ct)
    {
        var c = await Company(ct).ConfigureAwait(false);
        return new MarketSettingsResponse(c.CountryCode, c.CurrencyCode, c.CurrencyDecimals, c.TaxRate, c.TaxInclusive, c.TimeZone,
            await SettingAsync(SettingKeys.PhonePattern, ct).ConfigureAwait(false) ?? SettingKeys.JordanPhonePattern,
            await SettingAsync(SettingKeys.IbanPattern, ct).ConfigureAwait(false) ?? SettingKeys.JordanIbanPattern,
            Convert.ToBase64String(c.RowVersion));
    }

    public async Task<MarketSettingsResponse> UpdateMarketAsync(MarketSettingsRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        new Validator()
            .Check(r.CountryCode is not null && TwoUpper().IsMatch(r.CountryCode), "countryCode")
            .Check(r.CurrencyCode is not null && ThreeUpper().IsMatch(r.CurrencyCode), "currencyCode")
            .Check(r.CurrencyDecimals is >= 0 and <= 3, "currencyDecimals", ErrorCodes.ValidationRange)
            .Check(r.TaxRate is >= 0 and <= 100 && decimal.Round(r.TaxRate, 2) == r.TaxRate, "taxRate", ErrorCodes.ValidationRange)
            .Check(IsTimeZone(r.TimeZone), "timeZone")
            .Check(IsRegex(r.PhonePattern), "phonePattern")
            .Check(IsRegex(r.IbanPattern), "ibanPattern")
            .ThrowIfInvalid();
        var c = await Company(ct).ConfigureAwait(false);
        db.ExpectRowVersion(c, r.RowVersion);
        c.CountryCode = r.CountryCode!;
        c.CurrencyCode = r.CurrencyCode!;
        c.CurrencyDecimals = (byte)r.CurrencyDecimals;
        c.TaxRate = r.TaxRate;
        c.TaxInclusive = r.TaxInclusive;
        c.TimeZone = r.TimeZone;
        changes.SetAction(c, "SettingChange");
        await SetSettingAsync(SettingKeys.PhonePattern, r.PhonePattern, ct).ConfigureAwait(false);
        await SetSettingAsync(SettingKeys.IbanPattern, r.IbanPattern, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetMarketAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ operations

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

    /// <summary>FR-171: marks initial setup / data migration complete; disables the opening-balance difference; irreversible.</summary>
    public async Task MarkMigrationCompleteAsync(bool confirm, CancellationToken ct)
    {
        if (!confirm)
            throw new AppException(ErrorCodes.ConfirmationRequired, 422, "confirm",
                ctx.Language == "en" ? "this cannot be undone." : "لا يمكن التراجع عن هذا الإجراء.");
        var c = await Company(ct).ConfigureAwait(false);
        if (c.MigrationCompletedAt is not null) throw Fail.Rule(ErrorCodes.MigrationAlreadyComplete);
        c.MigrationCompletedAt = clock.GetUtcNow().UtcDateTime;
        changes.SetAction(c, "MigrationComplete");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------ numbering (FR-169, BR-037, BR-038a)

    public async Task<IReadOnlyList<NumberSequenceResponse>> SequencesAsync(CancellationToken ct)
    {
        var rows = await db.Set<NumberSequence>().AsNoTracking().OrderBy(s => s.DocumentType).ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToResponse).ToList();
    }

    public async Task<NumberSequenceResponse> UpdateSequenceAsync(string sequenceToken, NumberSequenceRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var publicId = tokens.Decode<NumberSequence>(sequenceToken) ?? throw Fail.NotFound();
        var s = await db.Set<NumberSequence>().SingleOrDefaultAsync(x => x.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        var prefix = TextNormalizer.CleanCode(r.Prefix) ?? string.Empty;
        new Validator()
            .Check(prefix.Length <= 10, "prefix", ErrorCodes.ValidationRange)
            .Check(r.Digits is >= 3 and <= 10, "digits", ErrorCodes.ValidationRange)
            .ThrowIfInvalid();
        // BR-038a: the next number can never be set at or below an issued number (the last issued is NextNumber − 1).
        if (r.NextNumber < s.NextNumber)
            throw Fail.RuleOn("nextNumber", ErrorCodes.SequenceBelowIssued, s.NextNumber - 1);
        db.ExpectRowVersion(s, r.RowVersion);
        s.Prefix = prefix;
        s.Digits = (byte)r.Digits;
        s.NextNumber = r.NextNumber;
        changes.SetAction(s, "SettingChange");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToResponse(s);
    }

    private NumberSequenceResponse ToResponse(NumberSequence s) =>
        new(tokens.Encode<NumberSequence>(s.PublicId), s.DocumentType, s.Prefix, s.Digits, s.NextNumber,
            DocumentTypes.Format(s.Prefix, s.Digits, s.NextNumber), Convert.ToBase64String(s.RowVersion));

    // ------------------------------------------------------------ helpers

    private async Task<Company> Company(CancellationToken ct) =>
        await db.Set<Company>().SingleOrDefaultAsync(c => c.Id == ctx.CompanyId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();

    private async Task<Dictionary<string, string>> AllSettingsAsync(CancellationToken ct) =>
        await db.Set<CompanySetting>().AsNoTracking().ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue, ct).ConfigureAwait(false);

    public async Task<string?> SettingAsync(string key, CancellationToken ct) =>
        await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == key).Select(s => s.SettingValue).SingleOrDefaultAsync(ct).ConfigureAwait(false);

    private async Task SetSettingAsync(string key, string value, CancellationToken ct)
    {
        var s = await db.Set<CompanySetting>().SingleOrDefaultAsync(x => x.SettingKey == key, ct).ConfigureAwait(false);
        if (s is null)
        {
            db.Set<CompanySetting>().Add(new CompanySetting { SettingKey = key, SettingValue = value });
        }
        else if (s.SettingValue != value)
        {
            s.SettingValue = value;
            changes.SetAction(s, "SettingChange");
        }
    }

    private static bool Bool(Dictionary<string, string> s, string key) =>
        s.TryGetValue(key, out var v) ? v == "true" : SettingKeys.Defaults[key] == "true";

    private static decimal Dec(Dictionary<string, string> s, string key) =>
        decimal.Parse(s.TryGetValue(key, out var v) ? v : SettingKeys.Defaults[key], CultureInfo.InvariantCulture);

    private static string B(bool v) => v ? "true" : "false";

    private static bool IsTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 50) return false;
        return TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
    }

    private static bool IsRegex(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 300) return false;
        try { _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1)); return true; }
        catch (ArgumentException) { return false; }
    }

    [GeneratedRegex("^[A-Z]{2}$")]
    private static partial Regex TwoUpper();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex ThreeUpper();
}
