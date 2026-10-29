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
