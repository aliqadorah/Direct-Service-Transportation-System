namespace DirectService.SharedKernel;

/// <summary>
/// What the shared engines show of a company (Platform row): the print header (names, tax and registration numbers, address, phone, logo),
/// the currency with its decimals and the time zone (Stage 2 session 8, DT-321).
/// </summary>
public sealed record CompanyHeader(string Name, string? NameEn, string? TaxNumber, string? CommercialRegNo, string? Address, string? Phone, byte[]? LogoFile,
    string CurrencyCode, byte CurrencyDecimals, string TimeZone);

/// <summary>The current company's data for the shared engines; companies are Platform rows, so Shared reads them only through this interface.</summary>
public interface ICompanyInfo
{
    /// <summary>The current company (throws when the request has none or the row does not exist, as the former direct reads did).</summary>
    Task<CompanyHeader> CurrentAsync(CancellationToken ct);

    /// <summary>The current company's time zone id, or null when there is no such company.</summary>
    Task<string?> TimeZoneAsync(CancellationToken ct);
}

/// <summary>A user as shown by the shared engines: the URL token of the user and the full name.</summary>
public sealed record NamedUser(string Token, string FullName);

/// <summary>The company's calendar for the services of every module (Stage 3 session 6c).</summary>
public static class CompanyCalendar
{
    /// <summary>Today in the current company's time zone (the server's UTC date when the company has none or it is unknown).</summary>
    public static async Task<DateOnly> TodayAsync(this ICompanyInfo company, TimeProvider clock, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(company);
        ArgumentNullException.ThrowIfNull(clock);
        var zone = await company.TimeZoneAsync(ct).ConfigureAwait(false);
        var now = clock.GetUtcNow();
        if (zone is not null && TimeZoneInfo.TryFindSystemTimeZoneById(zone, out var tz)) now = TimeZoneInfo.ConvertTime(now, tz);
        return DateOnly.FromDateTime(now.DateTime);
    }
}
