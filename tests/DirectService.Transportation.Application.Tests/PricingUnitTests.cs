using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Contracts.Pricing;

namespace DirectService.Transportation.Application.Tests;

/// <summary>
/// B4 pricing rules without a database: tariff specificity and matching (FR-020, FR-021, BR-028) with the PRD's scores, the
/// driver-rate resolution table (FR-030, BR-029), re-resolution on edit (FR-032), rate months and the month-strip status (FR-028).
/// </summary>
public sealed class PricingUnitTests
{
    private static readonly DateOnly Day = new(2026, 10, 15);
    private static readonly TariffKeys Trip = new(PartyId: 1, MaterialId: 2, LoadingSiteId: 3, UnloadingSiteId: 4);

    private static TariffLine Line(long id, TariffKeys keys, decimal price, DateOnly? from = null, DateOnly? to = null) => new(id, keys, price, from, to);

    [Theory]
    [InlineData(null, null, null, null, 0)]   // all keys All
    [InlineData(1L, null, null, null, 8)]     // party
    [InlineData(1L, 2L, null, null, 12)]      // party + material
    [InlineData(null, null, 3L, 4L, 3)]       // route only
    [InlineData(1L, 2L, 3L, 4L, 15)]          // all four
    public void Specificity_follows_FR021_scores(long? party, long? material, long? loading, long? unloading, int expected) =>
        Assert.Equal(expected, PricingRules.Specificity(new TariffKeys(party, material, loading, unloading)));

    [Fact]
    public void A_line_matches_when_each_key_equals_or_is_All_and_the_date_is_within_validity_FR020()
    {
        Assert.True(PricingRules.Matches(Line(1, new(null, null, null, null), 5m), Trip, Day));
        Assert.True(PricingRules.Matches(Line(1, new(1, null, 3, null), 5m), Trip, Day));
        Assert.False(PricingRules.Matches(Line(1, new(9, null, null, null), 5m), Trip, Day));
        Assert.False(PricingRules.Matches(Line(1, new(1, 2, 3, 9), 5m), Trip, Day));
        // Validity dates are inclusive; open ends match.
        Assert.True(PricingRules.Matches(Line(1, Trip, 5m, Day, Day), Trip, Day));
        Assert.True(PricingRules.Matches(Line(1, Trip, 5m, null, Day), Trip, Day));
        Assert.False(PricingRules.Matches(Line(1, Trip, 5m, Day.AddDays(1)), Trip, Day));
        Assert.False(PricingRules.Matches(Line(1, Trip, 5m, null, Day.AddDays(-1)), Trip, Day));
    }

    [Fact]
    public void Most_specific_line_wins_and_ties_go_to_the_latest_valid_from_BR028()
    {
        var all = Line(1, new(null, null, null, null), 4.000m);
        var party = Line(2, new(1, null, null, null), 5.000m);
        var partyMaterial = Line(3, new(1, 2, null, null), 5.500m);
        var route = Line(4, new(null, null, 3, 4), 5.250m);
        Assert.Equal(3, PricingRules.Pick([all, party, partyMaterial, route], Trip, Day)!.Id);   // 12 beats 8 and 3
        Assert.Equal(2, PricingRules.Pick([all, party, route], Trip, Day)!.Id);                 // 8 beats 3
        Assert.Equal(1, PricingRules.Pick([all], Trip, Day)!.Id);

        // Same score: the latest start date wins; an open start counts as the earliest.
        var open = Line(5, new(1, 2, null, null), 6.000m);
        var july = Line(6, new(1, 2, null, null), 6.500m, new DateOnly(2026, 7, 1));
        var september = Line(7, new(1, 2, null, null), 7.000m, new DateOnly(2026, 9, 1));
        Assert.Equal(7, PricingRules.Pick([open, september, july], Trip, Day)!.Id);
        Assert.Equal(6, PricingRules.Pick([open, september, july], Trip, new DateOnly(2026, 8, 1))!.Id);
        Assert.Equal(5, PricingRules.Pick([open, september, july], Trip, new DateOnly(2026, 6, 1))!.Id);

        // A more specific line outside its validity does not win.
        var expired = Line(8, Trip, 9.000m, null, new DateOnly(2026, 1, 31));
        Assert.Equal(7, PricingRules.Pick([expired, september], Trip, Day)!.Id);
        Assert.Null(PricingRules.Pick([Line(9, new(5, null, null, null), 1m)], Trip, Day));
    }

    [Theory]
    // partner trip → 0 whatever the table says (7.1.1, FR-005)
    [InlineData(true, 3.5, null, false, DriverRateOutcome.Monthly, 0)]
    [InlineData(true, null, null, false, DriverRateOutcome.Monthly, 0)]
    // monthly rate and no different typed rate → Monthly
    [InlineData(false, 3.5, null, false, DriverRateOutcome.Monthly, 3.5)]
    [InlineData(false, 3.5, 3.5, false, DriverRateOutcome.Monthly, 3.5)]
    // monthly rate and a different typed rate → Manual with masters.manage, refused without (BR-004)
    [InlineData(false, 3.5, 4.0, true, DriverRateOutcome.Manual, 4.0)]
    [InlineData(false, 3.5, 4.0, false, DriverRateOutcome.ManualNotAllowed, 3.5)]
    // no rate: typed by masters.manage → Manual; otherwise blocked (message names site, month, fleet type)
    [InlineData(false, null, 4.0, true, DriverRateOutcome.Manual, 4.0)]
    [InlineData(false, null, 4.0, false, DriverRateOutcome.Missing, 0)]
    [InlineData(false, null, null, true, DriverRateOutcome.Missing, 0)]
    public void Driver_rate_resolution_follows_FR030(bool partner, double? monthly, double? typed, bool canManual, DriverRateOutcome outcome, double rate)
    {
        var d = PricingRules.ResolveDriverRate(partner, (decimal?)monthly, (decimal?)typed, canManual);
        Assert.Equal(outcome, d.Outcome);
        Assert.Equal((decimal)rate, d.Rate);
        Assert.Equal(outcome is DriverRateOutcome.Monthly or DriverRateOutcome.Manual, d.CanSave);
        Assert.Equal(outcome == DriverRateOutcome.Manual ? RateSources.Manual : RateSources.Monthly, d.Source);
    }

    [Fact]
    public void Fleet_class_picks_the_rate_and_edits_resolve_again_only_on_site_month_or_driver_change_FR032()
    {
        Assert.Equal(3.5m, PricingRules.RateFor("Own", 3.5m, 4.25m));
        Assert.Equal(4.25m, PricingRules.RateFor("External", 3.5m, 4.25m));
        Assert.Null(PricingRules.RateFor("External", 3.5m, null));

        Assert.False(PricingRules.MustResolveAgain(1, "2026-10", 7, 1, "2026-10", 7));
        Assert.True(PricingRules.MustResolveAgain(1, "2026-10", 7, 2, "2026-10", 7));
        Assert.True(PricingRules.MustResolveAgain(1, "2026-10", 7, 1, "2026-11", 7));
        Assert.True(PricingRules.MustResolveAgain(1, "2026-10", 7, 1, "2026-10", 8));
    }

    [Fact]
    public void Rate_months_and_month_strip_status_FR027_FR028()
    {
        Assert.Equal("2026-10", PricingRules.Month(new DateOnly(2026, 10, 31)));
        Assert.Equal("2026-01", PricingRules.ParseMonth(" 2026-01 "));
        foreach (var bad in new[] { null, "", "2026-13", "2026-00", "26-10", "2026/10", "2026-1", "٢٠٢٦-١٠", "0999-01" })
            Assert.Null(PricingRules.ParseMonth(bad));
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), PricingRules.Range("2028-02"));

        Assert.Equal(MonthRateStatus.None, PricingRules.MonthStatus(5, 0));
        Assert.Equal(MonthRateStatus.Partial, PricingRules.MonthStatus(5, 3));
        Assert.Equal(MonthRateStatus.Complete, PricingRules.MonthStatus(5, 5));
        Assert.Equal(MonthRateStatus.None, PricingRules.MonthStatus(0, 0));
    }
}
