namespace DirectService.Platform.Contracts.Tenants;

public sealed record TenantResponse(
    string Name, string Status, string PlanCode, string PlanName, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd,
    int PasswordMinLength, bool PasswordRequireLetter, bool PasswordRequireDigit, int SessionIdleTimeoutMinutes, string RowVersion,
    bool RequireTwoFactor = false);

/// <summary>Tenant sign-in security (Owner only); <paramref name="RequireTwoFactor"/>: every user must use two-step verification (revision 6).</summary>
public sealed record TenantSecurityRequest(int PasswordMinLength, bool PasswordRequireLetter, bool PasswordRequireDigit, int SessionIdleTimeoutMinutes, string? RowVersion,
    bool RequireTwoFactor = false);

public sealed record CompanyResponse(string Id, string Name, string? NameEn, string Status, bool IsDemo, DateTime CreatedAt, string RowVersion);
public sealed record CreateCompanyRequest(string Name, string? NameEn);

// ---- company settings (settings.manage, FR-168)

public sealed record CompanyProfileResponse(
    string Name, string? NameEn, string? TaxNumber, string? CommercialRegNo, string? Address, string? Phone, string? Email,
    bool HasLogo, string RowVersion);

public sealed record CompanyProfileRequest(
    string Name, string? NameEn, string? TaxNumber, string? CommercialRegNo, string? Address, string? Phone, string? Email, string? RowVersion);

public sealed record MarketSettingsResponse(
    string CountryCode, string CurrencyCode, int CurrencyDecimals, decimal TaxRate, bool TaxInclusive, string TimeZone,
    string PhonePattern, string IbanPattern, string RowVersion);

public sealed record MarketSettingsRequest(
    string CountryCode, string CurrencyCode, int CurrencyDecimals, decimal TaxRate, bool TaxInclusive, string TimeZone,
    string PhonePattern, string IbanPattern, string? RowVersion);

public sealed record NumberSequenceResponse(string Id, string DocumentType, string Prefix, int Digits, long NextNumber, string NextFormatted, string RowVersion);
public sealed record NumberSequenceRequest(string Prefix, int Digits, long NextNumber, string? RowVersion);
