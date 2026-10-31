using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>
/// Price resolution against the company's tables (PRD 8.4): the winning tariff line for a customer or partner (BR-028, FR-020 …
/// FR-022) and the monthly driver rate of a loading site, month and fleet class (BR-029, FR-030). Used by the trip form's quote
/// (FR-006) and by trip save and import (B5), which freeze the result into the trip (BR-030).
/// </summary>
public sealed class PricingResolver(IAppDbContext db, IRequestContext ctx, MasterDataSupport support)
{
    /// <summary>The winning active tariff line for a trip's keys on <paramref name="date"/>, or null (the user enters the price).</summary>
    public async Task<TariffLine?> TariffAsync(string partyType, TariffKeys trip, DateOnly date, CancellationToken ct)
    {
        // Candidate lines through IX_TRN_Tariffs_Lookup; the final pick follows the same rules as PricingRules.Pick.
        var party = trip.PartyId;
        var query = db.Set<Tariff>().AsNoTracking()
            .Where(t => t.PartyType == partyType && t.Status == MasterCodes.Active)
            .Where(t => (t.ValidFrom == null || t.ValidFrom <= date) && (t.ValidTo == null || t.ValidTo >= date))
            .Where(t => t.MaterialId == null || t.MaterialId == trip.MaterialId)
            .Where(t => t.LoadingSiteId == null || t.LoadingSiteId == trip.LoadingSiteId)
            .Where(t => t.UnloadingSiteId == null || t.UnloadingSiteId == trip.UnloadingSiteId);
        query = partyType == PartyTypes.Customer
            ? query.Where(t => t.CustomerId == null || t.CustomerId == party)
            : query.Where(t => t.PartnerId == null || t.PartnerId == party);
        var best = await query.OrderByDescending(t => t.Specificity).ThenByDescending(t => t.ValidFrom).ThenByDescending(t => t.Id)
            .Select(t => new { t.Id, t.PublicId, t.CustomerId, t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId, t.Price, t.ValidFrom, t.ValidTo })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return best is null ? null
            : new TariffLine(best.Id, new TariffKeys(best.CustomerId ?? best.PartnerId, best.MaterialId, best.LoadingSiteId, best.UnloadingSiteId),
                best.Price, best.ValidFrom, best.ValidTo, best.PublicId);
    }

    /// <summary>Monthly rate of a loading site for a month and fleet class, or null when the month has no rate for that class (BR-029).</summary>
    public async Task<decimal?> MonthlyRateAsync(long loadingSiteId, string rateMonth, string fleetClass, CancellationToken ct)
    {
        var row = await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => r.LoadingSiteId == loadingSiteId && r.RateMonth == rateMonth)
            .Select(r => new { r.OwnFleetRate, r.ExternalFleetRate }).SingleOrDefaultAsync(ct).ConfigureAwait(false);
        return row is null ? null : PricingRules.RateFor(fleetClass, row.OwnFleetRate, row.ExternalFleetRate);
    }

    /// <summary>"No driver rate for «site», month 2026-10, own fleet" in the user's language (FR-030, flow 9.2).</summary>
    public string MissingRateMessage(string siteName, string rateMonth, string fleetClass) =>
        ErrorCatalog.Format(ErrorCodes.RateMissing, ctx.Language, siteName, rateMonth, FleetLabel(fleetClass));

    public string FleetLabel(string fleetClass) => (ctx.Language == "en", fleetClass) switch
    {
        (true, MasterCodes.Own) => "own fleet",
        (true, _) => "external fleet",
        (false, MasterCodes.Own) => "أسطول خاص",
        (false, _) => "أسطول خارجي",
    };

    /// <summary>Whether the company's tariff lock is on (FR-025).</summary>
    public async Task<bool> TariffLockAsync(CancellationToken ct) =>
        string.Equals(await db.Set<CompanySetting>().AsNoTracking().Where(s => s.SettingKey == SettingKeys.LockTariffs).Select(s => s.SettingValue)
            .SingleOrDefaultAsync(ct).ConfigureAwait(false), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Prices for the trip form (FR-006): customer price and partner rate from the tariffs when the keys are known, and the driver
    /// rate from the monthly site rates when driver and loading site are known. The partner defaults to the driver's partner
    /// (7.1.1); a partner trip's driver rate is 0. Unknown tokens → 400 on the field.
    /// </summary>
    public async Task<PriceQuoteResponse> QuoteAsync(PriceQuoteQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var customer = await IdAsync<Customer>(q.Customer, "customer", ct).ConfigureAwait(false);
        var material = await IdAsync<Material>(q.Material, "material", ct).ConfigureAwait(false);
        var loading = await IdAsync<LoadingSite>(q.LoadingSite, "loadingSite", ct).ConfigureAwait(false);
        var unloading = await IdAsync<UnloadingSite>(q.UnloadingSite, "unloadingSite", ct).ConfigureAwait(false);
        var partner = await IdAsync<Partner>(q.Partner, "partner", ct).ConfigureAwait(false);
        Driver? driver = null;
        if (!string.IsNullOrWhiteSpace(q.Driver))
        {
            var g = support.Decode<Driver>(q.Driver) ?? throw Fail.Field("driver", ErrorCodes.ValidationFormat, new FieldRef("driver"));
            driver = await db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).SingleOrDefaultAsync(d => d.PublicId == g, ct).ConfigureAwait(false)
                ?? throw Fail.Field("driver", ErrorCodes.ValidationFormat, new FieldRef("driver"));
            // An own-fleet driver cannot carry a partner trip (DT-48, BR-029; review L9).
            if (partner is not null && driver.OwnershipType.FleetClass == MasterCodes.Own)
                throw Fail.RuleOn("partner", ErrorCodes.DriverPartnerOwnFleet,
                    await db.Set<Partner>().AsNoTracking().Where(p => p.Id == partner).Select(p => p.Name).SingleAsync(ct).ConfigureAwait(false));
            partner ??= driver.PartnerId;
        }
        var tripDate = q.TripDate ?? q.LoadingDate;
        var loadingDate = q.LoadingDate ?? q.TripDate;
        var manual = ctx.HasPermission(PermissionKeys.MastersManage);
        var tariffLock = await TariffLockAsync(ct).ConfigureAwait(false);

        TariffPrice? customerPrice = null, partnerRate = null;
        if (tripDate is DateOnly date && material is not null && loading is not null && unloading is not null)
        {
            if (customer is not null)
                customerPrice = Price(await TariffAsync(PartyTypes.Customer, new(customer, material, loading, unloading), date, ct).ConfigureAwait(false));
            if (partner is not null)
                partnerRate = Price(await TariffAsync(PartyTypes.Partner, new(partner, material, loading, unloading), date, ct).ConfigureAwait(false));
        }

        DriverRateQuote? driverRate = null;
        if (driver is not null && loading is long siteId && loadingDate is DateOnly ld)
        {
            var fleet = driver.OwnershipType.FleetClass;
            var month = PricingRules.Month(ld);
            var partnerTrip = partner is not null;
            var monthly = partnerTrip ? null : await MonthlyRateAsync(siteId, month, fleet, ct).ConfigureAwait(false);
            var decision = PricingRules.ResolveDriverRate(partnerTrip, monthly, null, manual);
            string? missing = null;
            if (decision.Outcome == DriverRateOutcome.Missing)
            {
                var siteName = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Id == siteId).Select(s => s.Name).SingleAsync(ct).ConfigureAwait(false);
                missing = MissingRateMessage(siteName, month, fleet);
            }
            driverRate = new DriverRateQuote(fleet, month, partnerTrip, decision.CanSave ? decision.Rate : null, decision.CanSave ? decision.Source : null, missing, manual);
        }
        return new PriceQuoteResponse(customerPrice, partnerRate, driverRate, tariffLock, !tariffLock || manual);
    }

    private TariffPrice? Price(TariffLine? line) => line is null ? null
        : new TariffPrice(line.Price, support.Token<Tariff>(line.PublicId), PricingRules.Specificity(line.Keys), line.ValidFrom, line.ValidTo);

    private async Task<long?> IdAsync<T>(string? token, string field, CancellationToken ct) where T : AuditableEntity
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var g = support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));
        return await db.Set<T>().AsNoTracking().Where(x => x.PublicId == g).Select(x => (long?)x.Id).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));
    }
}
