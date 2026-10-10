using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.MasterData;

namespace DirectService.Transportation.Contracts.Pricing;

/// <summary>Whose price a tariff line sets (PRD 7.2.1).</summary>
public static class PartyTypes
{
    public const string Customer = "Customer";
    public const string Partner = "Partner";
}

/// <summary>Driver rate sources stored on trips (FR-031).</summary>
public static class RateSources
{
    public const string Monthly = "Monthly";
    public const string Manual = "Manual";
}

/// <summary>Status of a month in the month strip (FR-028).</summary>
public static class MonthRateStatus
{
    public const string None = "None";
    public const string Partial = "Partial";
    public const string Complete = "Complete";
}

// ---------------------------------------------------------------- tariffs (FR-019 … FR-026)

/// <summary>
/// A tariff line. Every key token may be empty for "All" (party, material, loading site, unloading site); the party is a customer
/// when <see cref="PartyType"/> is Customer and a partner when it is Partner. Price per ton &gt; 0 (D-03); validity dates optional.
/// </summary>
public sealed record TariffRequest(
    string? PartyType, string? PartyId, string? MaterialId, string? LoadingSiteId, string? UnloadingSiteId, decimal? Price,
    DateOnly? ValidFrom, DateOnly? ValidTo, string? Notes, string? ClientId, string? RowVersion);

/// <summary>A tariff line; a null reference means "All". <see cref="Specificity"/> = party 8 + material 4 + loading 2 + unloading 1 (FR-021).</summary>
public sealed record TariffResponse(
    string Id, string PartyType, Ref? Party, Ref? Material, Ref? LoadingSite, Ref? UnloadingSite, decimal Price,
    DateOnly? ValidFrom, DateOnly? ValidTo, int Specificity, string? Notes, string Status, string RowVersion);

/// <summary>
/// Tariff list filters: party type; party, material, loading site, unloading site by token or "all" (lines whose key is All);
/// lines valid on a date; search in party, material, site names and notes; status; sort; paging.
/// </summary>
public sealed record TariffListQuery(
    string? PartyType, string? Party, string? Material, string? LoadingSite, string? UnloadingSite, DateOnly? ValidOn, string? Q,
    string? Status, string? Sort, int Page = 1, int PageSize = 50);

/// <summary>One change-history entry of a tariff line or a monthly site rate (FR-026, FR-036): who, when, what, old → new.</summary>
public sealed record RateHistoryEntry(DateTime At, string? User, string Action, string? Subject, IReadOnlyList<FieldChange> Changes);

/// <summary>A tariff line with its history.</summary>
public sealed record TariffDetails(TariffResponse Record, IReadOnlyList<RateHistoryEntry> History);

// ---------------------------------------------------------------- monthly loading-site driver rates (FR-027 … FR-037)

/// <summary>A month of the month strip: no rates / partial / complete over the active loading sites (FR-028).</summary>
public sealed record MonthStatusResponse(string Month, string Status, int ActiveSites, int RatedSites, bool HasTrips);

/// <summary>
/// The rates of one loading site in a month. <see cref="Id"/>/<see cref="RowVersion"/> are null while the site has no rates;
/// <see cref="TripCount"/> counts the month's trips loaded at the site (rates cannot be cleared while it is above 0, FR-035).
/// </summary>
public sealed record SiteRateResponse(
    Ref LoadingSite, string? Area, string SiteStatus, decimal? OwnFleetRate, decimal? ExternalFleetRate, int TripCount, string? Id, string? RowVersion);

/// <summary>
/// A month for entry: every active loading site (and archived sites that have rates in the month). <see cref="HasTrips"/> = the
/// month already has trips, so saving warns that only new trips use the new rates (FR-034).
/// </summary>
public sealed record MonthRatesResponse(string Month, string Status, bool HasTrips, int ActiveSites, int RatedSites, IReadOnlyList<SiteRateResponse> Sites);

/// <summary>Rates of one site; both empty clears the site's rates for the month (refused while the site has trips in it, FR-035).</summary>
public sealed record SiteRateRequest(string? LoadingSiteId, decimal? OwnFleetRate, decimal? ExternalFleetRate, string? RowVersion);

/// <summary>The sites of a month to save (only the sites sent are changed).</summary>
public sealed record MonthRatesRequest(IReadOnlyList<SiteRateRequest>? Sites);

/// <summary>Saved month with the number of sites changed and warnings (FR-034).</summary>
public sealed record MonthRatesSaveResult(MonthRatesResponse Month, int Changed, IReadOnlyList<string> Warnings);

/// <summary>Active loading sites without rates in a month (dashboard warning, FR-037).</summary>
public sealed record MissingRatesResponse(string Month, int ActiveSites, IReadOnlyList<Ref> Sites);

// ---------------------------------------------------------------- price quote for the trip form (FR-006, FR-022, FR-030)

/// <summary>
/// What the trip form knows: customer, partner (optional), driver, material, loading and unloading site, trip date (tariff
/// validity, FR-020) and loading date (rate month, BR-029; defaults to the trip date).
/// </summary>
public sealed record PriceQuoteQuery(
    string? Customer, string? Partner, string? Driver, string? Material, string? LoadingSite, string? UnloadingSite, DateOnly? TripDate, DateOnly? LoadingDate);

/// <summary>A price filled from the winning tariff line (FR-022) with its source.</summary>
public sealed record TariffPrice(decimal Price, string TariffId, int Specificity, DateOnly? ValidFrom, DateOnly? ValidTo);

/// <summary>
/// Driver rate pre-fill (FR-030): the monthly rate of the loading site for the rate month and fleet class; 0 for partner trips;
/// null with <see cref="Missing"/> (site, month, fleet type) when there is no rate — then only a user who may enter manual rates
/// (<see cref="CanEnterManualRate"/>, BR-004) can save the trip.
/// </summary>
public sealed record DriverRateQuote(string FleetClass, string RateMonth, bool IsPartnerTrip, decimal? Rate, string? Source, string? Missing, bool CanEnterManualRate);

/// <summary>
/// Prices for the trip form: customer price and partner rate from the tariffs (null = no matching line, the user enters it),
/// driver rate from the monthly site rates, and the tariff-lock state (FR-025: deviations need masters.manage when on).
/// </summary>
public sealed record PriceQuoteResponse(TariffPrice? CustomerPrice, TariffPrice? PartnerRate, DriverRateQuote? DriverRate, bool TariffLock, bool CanDeviateFromTariff);
