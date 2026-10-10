using System.Globalization;
using DirectService.Transportation.Contracts.Pricing;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>The keys of a tariff line or of a trip; null on a line means "All".</summary>
public readonly record struct TariffKeys(long? PartyId, long? MaterialId, long? LoadingSiteId, long? UnloadingSiteId);

/// <summary>A tariff line as the resolver sees it (<see cref="PublicId"/> for links to the line).</summary>
public sealed record TariffLine(long Id, TariffKeys Keys, decimal Price, DateOnly? ValidFrom, DateOnly? ValidTo, Guid PublicId = default);

/// <summary>Outcome of the driver-rate resolution (FR-030).</summary>
public enum DriverRateOutcome
{
    /// <summary>Rate taken from the monthly site rate (or 0 for a partner trip).</summary>
    Monthly,

    /// <summary>A typed rate saved as Manual (flagged, audited; needs masters.manage).</summary>
    Manual,

    /// <summary>No rate: the trip cannot be saved (message names site, month and fleet type).</summary>
    Missing,

    /// <summary>A typed rate differs from the monthly rate but the user may not enter manual rates (BR-004).</summary>
    ManualNotAllowed,
}

/// <summary>The rate a trip stores and its source.</summary>
public sealed record DriverRateDecision(DriverRateOutcome Outcome, decimal Rate)
{
    public bool CanSave => Outcome is DriverRateOutcome.Monthly or DriverRateOutcome.Manual;
    public bool IsManual => Outcome == DriverRateOutcome.Manual;
    public string Source => IsManual ? RateSources.Manual : RateSources.Monthly;
}

/// <summary>
/// Pricing rules of PRD 7.2 and 8.4 as pure functions: tariff matching and specificity (FR-020, FR-021, BR-028), driver-rate
/// resolution (FR-030, BR-029), re-resolution on edit (FR-032) and rate months (FR-027).
/// </summary>
public static class PricingRules
{
    /// <summary>Specificity score: party 8 + material 4 + loading site 2 + unloading site 1 (FR-021). All keys All = 0; all four = 15.</summary>
    public static int Specificity(TariffKeys k) =>
        (k.PartyId is null ? 0 : 8) + (k.MaterialId is null ? 0 : 4) + (k.LoadingSiteId is null ? 0 : 2) + (k.UnloadingSiteId is null ? 0 : 1);

    /// <summary>A line matches when each key equals the trip's value or is All, and the date is within the validity dates (FR-020).</summary>
    public static bool Matches(TariffLine line, TariffKeys trip, DateOnly date)
    {
        ArgumentNullException.ThrowIfNull(line);
        var k = line.Keys;
        return (k.PartyId is null || k.PartyId == trip.PartyId)
            && (k.MaterialId is null || k.MaterialId == trip.MaterialId)
            && (k.LoadingSiteId is null || k.LoadingSiteId == trip.LoadingSiteId)
            && (k.UnloadingSiteId is null || k.UnloadingSiteId == trip.UnloadingSiteId)
            && (line.ValidFrom is null || line.ValidFrom <= date)
            && (line.ValidTo is null || line.ValidTo >= date);
    }

    /// <summary>
    /// The winning line (BR-028): highest specificity; tie → latest Valid from (an open start counts as the earliest); then the
    /// newest line. Null when nothing matches (the user enters the price, FR-022).
    /// </summary>
    public static TariffLine? Pick(IEnumerable<TariffLine> lines, TariffKeys trip, DateOnly date) =>
        lines.Where(l => Matches(l, trip, date))
            .OrderByDescending(l => Specificity(l.Keys))
            .ThenByDescending(l => l.ValidFrom ?? DateOnly.MinValue)
            .ThenByDescending(l => l.Id)
            .FirstOrDefault();

    /// <summary>
    /// Driver rate of a new trip (FR-030): partner trip → 0; monthly rate and no different typed rate → Monthly; monthly rate and a
    /// different typed rate → Manual (masters.manage, else refused); no rate and a typed rate by a user with masters.manage → Manual;
    /// otherwise Missing.
    /// </summary>
    public static DriverRateDecision ResolveDriverRate(bool partnerTrip, decimal? monthlyRate, decimal? typedRate, bool canEnterManualRate)
    {
        if (partnerTrip) return new(DriverRateOutcome.Monthly, 0m);
        if (monthlyRate is decimal monthly)
        {
            if (typedRate is null || typedRate == monthly) return new(DriverRateOutcome.Monthly, monthly);
            return canEnterManualRate ? new(DriverRateOutcome.Manual, typedRate.Value) : new(DriverRateOutcome.ManualNotAllowed, monthly);
        }
        return typedRate is decimal typed && canEnterManualRate ? new(DriverRateOutcome.Manual, typed) : new(DriverRateOutcome.Missing, 0m);
    }

    /// <summary>
    /// On edit the stored rate is kept unless the loading site, loading month or driver changes; then it is resolved again by
    /// FR-030 (FR-032).
    /// </summary>
    public static bool MustResolveAgain(long oldSiteId, string oldRateMonth, long oldDriverId, long newSiteId, string newRateMonth, long newDriverId) =>
        oldSiteId != newSiteId || !string.Equals(oldRateMonth, newRateMonth, StringComparison.Ordinal) || oldDriverId != newDriverId;

    /// <summary>Monthly rate of a fleet class (Own → own-fleet rate; External → external-fleet rate, BR-029).</summary>
    public static decimal? RateFor(string fleetClass, decimal? ownFleetRate, decimal? externalFleetRate) =>
        fleetClass == "Own" ? ownFleetRate : externalFleetRate;

    /// <summary>Rate month (YYYY-MM) of a loading date (BR-029).</summary>
    public static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Parses a YYYY-MM month (years 1000–2999 as the design CHECK allows, months 01–12); null when invalid.</summary>
    public static string? ParseMonth(string? text)
    {
        var t = text?.Trim();
        if (t is null || t.Length != 7 || t[4] != '-') return null;
        if (!int.TryParse(t.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y is < 1000 or > 2999) return null;
        if (!int.TryParse(t.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m is < 1 or > 12) return null;
        return t;
    }

    /// <summary>First and last day of a YYYY-MM month.</summary>
    public static (DateOnly First, DateOnly Last) Range(string month)
    {
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return (first, first.AddMonths(1).AddDays(-1));
    }

    /// <summary>
    /// Month-strip status (FR-028, DT-53): no active site rated → None; every active site rated → Complete; otherwise Partial.
    /// A site counts as rated when it has at least one of the two rates. A company without active loading sites shows None.
    /// </summary>
    public static string MonthStatus(int activeSites, int ratedSites) =>
        ratedSites <= 0 || activeSites <= 0 ? MonthRateStatus.None : ratedSites >= activeSites ? MonthRateStatus.Complete : MonthRateStatus.Partial;
}
