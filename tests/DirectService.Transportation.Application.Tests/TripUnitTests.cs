using DirectService.Transportation.Application.Trips;

namespace DirectService.Transportation.Application.Tests;

/// <summary>B5 trip calculations without a database (PRD 8.2, FR-005, FR-012, FR-023) with the PRD's numeric example.</summary>
public sealed class TripUnitTests
{
    private static TripFigures Fig(decimal loaded = 32.000m, decimal? unloaded = 31.850m, decimal price = 6.000m, decimal? tariff = null, decimal rate = 3.500m,
        decimal? partnerRate = null, bool partner = false, bool billUnloaded = true, bool payLoaded = false, decimal tolerance = 0.5m) =>
        new(loaded, unloaded, price, tariff, rate, partnerRate, partner, billUnloaded, payLoaded, tolerance);

    [Fact]
    public void PRD_example_revenue_wage_margin_and_shortage()
    {
        // 8.2: loaded 32.000 t, unloaded 31.850 t, price 6.000, driver rate 3.500 → revenue 191.100, wage 111.475, margin 79.625,
        // shortage 0.150 t = 0.47 % (within tolerance).
        var a = TripCalculator.Calculate(Fig());
        Assert.Equal(31.850m, a.BillingQty);
        Assert.Equal(31.850m, a.WageQty);
        Assert.Equal(191.100m, a.Revenue);
        Assert.Equal(111.475m, a.DriverWage);
        Assert.Equal(0m, a.PartnerCost);
        Assert.Equal(79.625m, a.Margin);
        Assert.Equal(0.150m, a.ShortageWeight);
        Assert.Equal(0.4688m, a.ShortagePct);
        Assert.False(a.IsShortageFlagged);
        Assert.False(a.IsTariffBreach);
        Assert.Null(a.BreachAmount);
    }

    [Fact]
    public void Weight_basis_settings_choose_the_quantities_FR005()
    {
        // Bill on loaded weight (setting off) → billing on loaded; pay driver on loaded (setting on) → wage on loaded.
        var a = TripCalculator.Calculate(Fig(billUnloaded: false, payLoaded: true));
        Assert.Equal(32.000m, a.BillingQty);
        Assert.Equal(32.000m, a.WageQty);
        Assert.Equal(192.000m, a.Revenue);
        Assert.Equal(112.000m, a.DriverWage);
        // No unloaded weight: billing and wage fall back to loaded; no shortage.
        var b = TripCalculator.Calculate(Fig(unloaded: null));
        Assert.Equal(32.000m, b.BillingQty);
        Assert.Equal(32.000m, b.WageQty);
        Assert.Null(b.ShortageWeight);
        Assert.False(b.IsShortageFlagged);
    }

    [Fact]
    public void Partner_trip_pays_the_partner_not_the_driver()
    {
        var a = TripCalculator.Calculate(Fig(loaded: 31.500m, unloaded: 31.020m, rate: 0m, partnerRate: 2.750m, partner: true));
        Assert.Equal(0m, a.DriverWage);
        Assert.Equal(85.305m, a.PartnerCost);  // 31.020 × 2.750
        Assert.Equal(186.120m, a.Revenue);
        Assert.Equal(100.815m, a.Margin);
        Assert.True(a.IsShortageFlagged);      // 0.480 / 31.500 = 1.5238 % > 0.5 %
        Assert.Equal(1.5238m, a.ShortagePct);
    }

    [Fact]
    public void Tariff_breach_is_loss_when_negative_and_rounding_is_half_up_FR023()
    {
        var loss = TripCalculator.Calculate(Fig(loaded: 32.800m, unloaded: 32.790m, price: 5.000m, tariff: 5.250m, rate: 3.250m));
        Assert.True(loss.IsTariffBreach);
        Assert.Equal(-8.198m, loss.BreachAmount);  // (5.000 − 5.250) × 32.790 = −8.1975 → −8.198 (half-up away from zero)
        var equal = TripCalculator.Calculate(Fig(tariff: 6.000m));
        Assert.False(equal.IsTariffBreach);
        Assert.Equal(0m, equal.BreachAmount);
        // Half-up at each amount (BR-021): 33.150 × 5.250 = 174.0375 → 174.038.
        Assert.Equal(174.038m, TripCalculator.Calculate(Fig(loaded: 33.200m, unloaded: 33.150m, price: 5.250m)).Revenue);
    }

    [Fact]
    public void Shortage_flag_uses_the_company_tolerance_and_warnings_FR012_FR002()
    {
        Assert.True(TripCalculator.Calculate(Fig(tolerance: 0.4m)).IsShortageFlagged);   // 0.4688 % > 0.4 %
        Assert.False(TripCalculator.Calculate(Fig(loaded: 30m, unloaded: 30.5m)).IsShortageFlagged);  // over-delivery is not a shortage
        Assert.True(TripCalculator.OverCapacity(33.000m, 32.000m));
        Assert.False(TripCalculator.OverCapacity(32.000m, 32.000m));
        Assert.True(TripCalculator.UnloadedTooHigh(32.000m, 32.641m));   // above 32.640
        Assert.False(TripCalculator.UnloadedTooHigh(32.000m, 32.640m));
    }
}
