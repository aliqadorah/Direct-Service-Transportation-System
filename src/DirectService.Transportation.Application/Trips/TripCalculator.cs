using DirectService.Transportation.Application.Accounting;

namespace DirectService.Transportation.Application.Trips;

/// <summary>The inputs of the trip calculations (PRD 8.2) and the company's settings that drive them (7.9.1).</summary>
public sealed record TripFigures(
    decimal LoadedWeight, decimal? UnloadedWeight, decimal CustomerPrice, decimal? TariffPrice, decimal DriverRate, decimal? PartnerRate,
    bool IsPartnerTrip, bool BillOnUnloadedWeight, bool PayDriverOnLoadedWeight, decimal ShortageTolerancePct, int Decimals = 3);

/// <summary>The calculated amounts stored on the trip.</summary>
public sealed record TripAmounts(
    decimal BillingQty, decimal WageQty, decimal Revenue, decimal DriverWage, decimal PartnerCost, decimal Margin,
    decimal? ShortageWeight, decimal? ShortagePct, bool IsShortageFlagged, bool IsTariffBreach, decimal? BreachAmount);

/// <summary>
/// Trip calculations (PRD 8.2, FR-005, FR-012, FR-023): quantities by the weight-basis settings, amounts rounded half-up to the
/// currency's decimals at each amount (BR-020, BR-021), shortage and its flag, tariff breach.
/// </summary>
public static class TripCalculator
{
    public static TripAmounts Calculate(TripFigures f)
    {
        ArgumentNullException.ThrowIfNull(f);
        // Billing quantity: unloaded when "Bill on unloaded weight" is on and recorded, else loaded.
        var billingQty = f.BillOnUnloadedWeight && f.UnloadedWeight is decimal u ? u : f.LoadedWeight;
        // Wage quantity: loaded when "Pay driver on loaded weight" is on; otherwise unloaded, falling back to loaded.
        var wageQty = f.PayDriverOnLoadedWeight ? f.LoadedWeight : f.UnloadedWeight ?? f.LoadedWeight;
        var revenue = Money.Round(billingQty * f.CustomerPrice, f.Decimals);
        var wage = f.IsPartnerTrip ? 0m : Money.Round(wageQty * f.DriverRate, f.Decimals);
        var partnerCost = f.IsPartnerTrip ? Money.Round(wageQty * (f.PartnerRate ?? 0m), f.Decimals) : 0m;

        decimal? shortage = f.UnloadedWeight is decimal un ? f.LoadedWeight - un : null;
        // Clamped to the column's DECIMAL(9,4) range: a typo such as an unloaded weight in kilograms must not fail the save (review L2).
        decimal? pct = shortage is decimal s && f.LoadedWeight > 0 ? Math.Clamp(Math.Round(s / f.LoadedWeight * 100m, 4, MidpointRounding.AwayFromZero), -99_999.9999m, 99_999.9999m) : null;
        var flagged = pct is decimal p && p > f.ShortageTolerancePct;

        var breach = f.TariffPrice is decimal t && t != f.CustomerPrice;
        decimal? breachAmount = f.TariffPrice is decimal tp ? Money.Round((f.CustomerPrice - tp) * billingQty, f.Decimals) : null;
        return new TripAmounts(billingQty, wageQty, revenue, wage, partnerCost, revenue - wage - partnerCost, shortage, pct, flagged, breach, breachAmount);
    }

    /// <summary>Loaded weight above the truck's capacity (FR-003 warning).</summary>
    public static bool OverCapacity(decimal loaded, decimal capacity) => capacity > 0 && loaded > capacity;

    /// <summary>Unloaded weight above loaded × 1.02 (FR-002 warning).</summary>
    public static bool UnloadedTooHigh(decimal loaded, decimal? unloaded) => unloaded is decimal u && u > loaded * 1.02m;
}
