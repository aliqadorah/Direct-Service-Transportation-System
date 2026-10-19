using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Trips;

/// <summary>Company settings that drive trip calculations and rules (7.9.1), read once per operation.</summary>
public sealed record TripSettings(bool BillOnUnloadedWeight, bool PayDriverOnLoadedWeight, decimal ShortageTolerancePct, bool LockTariffs,
    bool AccrueDriverWage, int Decimals, DateOnly Today);

/// <summary>Where a save gets its prices: the database (form) or preloaded tables (import).</summary>
public interface ITripPriceSource
{
    Task<TariffLine?> TariffAsync(string partyType, TariffKeys keys, DateOnly date, CancellationToken ct);

    Task<decimal?> MonthlyRateAsync(long loadingSiteId, string rateMonth, string fleetClass, CancellationToken ct);
}

/// <summary>Bulk (import) options: receipt numbers already checked for the file; dates already known to be in open periods.</summary>
public sealed class TripBulkOptions
{
    public HashSet<DateOnly> OpenDates { get; } = [];
}

/// <summary>A trip request with its selections resolved to active records (form: by token; import: by name).</summary>
public sealed record TripDraft(
    string? ReceiptNo, DateOnly? TripDate, DateOnly? LoadingDate, DateOnly? UnloadingDate,
    Driver Driver, string FleetClass, Customer Customer, LoadingSite LoadingSite, UnloadingSite UnloadingSite, Material Material, Partner? Partner,
    decimal? LoadedWeight, decimal? UnloadedWeight, decimal? CustomerPrice, decimal? DriverRate, decimal? PartnerRate,
    string? Notes, IReadOnlyDictionary<string, JsonElement>? CustomValues);

/// <summary>
/// Trips (FR-001 … FR-017, PRD 7.1, 8.2, 8.4): create from the form or quick-entry row, validate (7.1.1), price from tariffs and
/// monthly site rates (FR-022, FR-030), calculate (8.2), freeze (BR-030), post the wage or partner-cost entry in the same
/// transaction (FR-016, 8.6); edit while unbilled, unsettled and in an open period with reverse + re-post (FR-010, BR-035);
/// void with a reason and reversal (FR-011); clear Needs review (FR-014); list with filters and totals (FR-009); last ten trips
/// (FR-008); quick-entry defaults (FR-007); history (FR-015).
/// </summary>
public sealed class TripService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IChangeContext changes, IRecordLockGuard locks,
    IPeriodGuard periods, PostingEngine posting, PricingResolver pricing, StandardFieldService fields, CustomFieldService customFields,
    CompanySettingsService settings, IMonthlyRateLock rateLock, INumberingService numbering, Platform.PlanLimits? limits = null)
{
    /// <summary>Trips saved this month must fit the plan (P-07); viewing and exporting are never limited (BR-013).</summary>
    internal Task EnsureTripLimitAsync(int adding, CancellationToken ct) =>
        limits is null || ctx.TenantId is not long tenant ? Task.CompletedTask : limits.EnsureTripsAsync(tenant, adding, ct);

    public const string EntityType = "TRN.Trip";
    public const string SourceType = "Trip";
    public const string Active = "Active";
    public const string Void = "Void";
    private const decimal MaxWeight = 999_999.999m;

    private bool En => ctx.Language == "en";

    // ============================================================ settings and price sources

    public async Task<TripSettings> SettingsAsync(CancellationToken ct)
    {
        async Task<bool> Flag(string key) => string.Equals(await settings.SettingAsync(key, ct).ConfigureAwait(false) ?? SettingKeys.Defaults[key], "true", StringComparison.OrdinalIgnoreCase);
        var tolText = await settings.SettingAsync(SettingKeys.ShortageTolerancePct, ct).ConfigureAwait(false) ?? SettingKeys.Defaults[SettingKeys.ShortageTolerancePct];
        var tolerance = decimal.TryParse(tolText, NumberStyles.Number, CultureInfo.InvariantCulture, out var t) ? t : 0.5m;
        return new TripSettings(await Flag(SettingKeys.BillOnUnloadedWeight).ConfigureAwait(false), await Flag(SettingKeys.PayDriverOnLoadedWeight).ConfigureAwait(false),
            tolerance, await Flag(SettingKeys.LockTariffs).ConfigureAwait(false), await Flag(SettingKeys.AccrueDriverWage).ConfigureAwait(false),
            await posting.CurrencyDecimalsAsync(ct).ConfigureAwait(false), await support.TodayAsync(ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Live calculation for the trip form (UX-17): the same <see cref="TripCalculator"/> and company settings as a save; missing
    /// inputs count as 0 so the panel can show partial figures while the user types.
    /// </summary>
    public async Task<TripCalculationResponse> CalculateAsync(TripCalculationRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var s = await SettingsAsync(ct).ConfigureAwait(false);
        static decimal Safe(decimal? v) => v is decimal d && d >= 0 && d <= 1_000_000m ? d : 0m;
        var unloaded = r.UnloadedWeight is decimal u && u >= 0 && u <= 1_000_000m ? u : (decimal?)null;
        var a = TripCalculator.Calculate(new TripFigures(Safe(r.LoadedWeight), unloaded, Safe(r.CustomerPrice), r.TariffPrice is decimal tp && tp > 0 ? tp : null,
            Safe(r.DriverRate), r.PartnerRate is null ? null : Safe(r.PartnerRate), r.IsPartnerTrip, s.BillOnUnloadedWeight, s.PayDriverOnLoadedWeight,
            s.ShortageTolerancePct, s.Decimals));
        return new TripCalculationResponse(a.BillingQty, a.WageQty, a.Revenue, a.DriverWage, a.PartnerCost, a.Margin, a.ShortageWeight, a.ShortagePct,
            a.IsShortageFlagged, a.IsTariffBreach, a.BreachAmount, s.BillOnUnloadedWeight, s.PayDriverOnLoadedWeight, s.ShortageTolerancePct, s.Decimals);
    }

    /// <summary>Database prices for a save: the monthly rate row is read WITH (UPDLOCK) so clearing it waits for the trip (B4 review M2).</summary>
    private sealed class DbPriceSource(IMonthlyRateLock rateLock, PricingResolver pricing) : ITripPriceSource
    {
        public Task<TariffLine?> TariffAsync(string partyType, TariffKeys keys, DateOnly date, CancellationToken ct) => pricing.TariffAsync(partyType, keys, date, ct);

        public async Task<decimal?> MonthlyRateAsync(long loadingSiteId, string rateMonth, string fleetClass, CancellationToken ct)
        {
            var row = (await rateLock.ReadAsync([rateMonth], loadingSiteId, ct).ConfigureAwait(false)).SingleOrDefault();
            return row is null ? null : PricingRules.RateFor(fleetClass, row.OwnFleetRate, row.ExternalFleetRate);
        }
    }

    // ============================================================ create, edit, void, clear review

    public async Task<(TripSaveResult Value, bool Created)> CreateAsync(TripRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var clientId = MasterIds.ClientId(r.ClientId);
        if (await support.ExistingAsync<Trip>(clientId, ct).ConfigureAwait(false) is { } again)
            return (new TripSaveResult(await ResponseAsync(again.Id, ct).ConfigureAwait(false), []), false);
        await EnsureTripLimitAsync(1, ct).ConfigureAwait(false);
        var s = await SettingsAsync(ct).ConfigureAwait(false);
        var draft = await DraftAsync(r, null, ct).ConfigureAwait(false);
        var t = new Trip();
        if (clientId is Guid id) t.PublicId = id;
        IReadOnlyList<TripWarning> warnings = [];
        var raced = await db.InTransactionAsync(async c =>
        {
            warnings = await ApplyAsync(t, draft, isNew: true, s, new DbPriceSource(rateLock, pricing), c).ConfigureAwait(false);
            db.Set<Trip>().Add(t);
            if (await support.SaveCreateAsync<Trip>(clientId, c).ConfigureAwait(false) is { } other) return other;
            await PostAsync(t, s, c).ConfigureAwait(false);
            return null;
        }, ct).ConfigureAwait(false);
        if (raced is not null) return (new TripSaveResult(await ResponseAsync(raced.Id, ct).ConfigureAwait(false), []), false);
        return (new TripSaveResult(await ResponseAsync(t.Id, ct).ConfigureAwait(false), warnings), true);
    }

    public async Task<TripSaveResult> UpdateAsync(string token, TripRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var t = await support.FindAsync<Trip>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        await EnsureEditableAsync(t, ct).ConfigureAwait(false);
        RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(t, r.RowVersion);
        var s = await SettingsAsync(ct).ConfigureAwait(false);
        var draft = await DraftAsync(r, t, ct).ConfigureAwait(false);
        IReadOnlyList<TripWarning> warnings = [];
        await db.InTransactionAsync(async c =>
        {
            // The old date's period must be open too: the old entry is reversed on its own date (DT-22, FR-010).
            await periods.EnsureOpenAsync(t.TripDate, c).ConfigureAwait(false);
            var before = EntryKey(t);
            var oldPartner = t.PartnerId;
            var oldCost = t.PartnerCost;
            warnings = await ApplyAsync(t, draft, isNew: false, s, new DbPriceSource(rateLock, pricing), c).ConfigureAwait(false);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (oldPartner is not null && (oldPartner != t.PartnerId || t.PartnerCost < oldCost))
                await EnsurePartnerNotOverpaidAsync(oldPartner.Value, c).ConfigureAwait(false);
            // The entry is reversed and re-posted only when what it posts changes (amount, party, date, receipt) (BR-035).
            if (t.JournalEntryId is null || EntryKey(t) != before)
            {
                await ReverseEntryAsync(t, En ? $"Edit of trip {t.ReceiptNo}" : $"تعديل الرحلة {t.ReceiptNo}", c).ConfigureAwait(false);
                await PostAsync(t, s, c).ConfigureAwait(false);
            }
        }, ct).ConfigureAwait(false);
        return new TripSaveResult(await ResponseAsync(t.Id, ct).ConfigureAwait(false), warnings);
    }

    public async Task<TripResponse> VoidAsync(string token, TripVoidRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var why = MasterIds.Reason(r.Reason);
        var t = await support.FindAsync<Trip>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        await EnsureEditableAsync(t, ct).ConfigureAwait(false);
        RequireRowVersion(r.RowVersion);
        db.ExpectRowVersion(t, r.RowVersion);
        await db.InTransactionAsync(async c =>
        {
            await periods.EnsureOpenAsync(t.TripDate, c).ConfigureAwait(false);
            t.Status = Void;
            t.VoidReason = why;
            t.VoidedBy = ctx.UserId ?? throw new InvalidOperationException("A void needs an acting user.");
            t.VoidedAt = DateTime.UtcNow;
            changes.SetAction(t, "Void", why);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            if (t.PartnerId is long partnerId) await EnsurePartnerNotOverpaidAsync(partnerId, c).ConfigureAwait(false);
            await ReverseEntryAsync(t, En ? $"Void of trip {t.ReceiptNo}" : $"إلغاء الرحلة {t.ReceiptNo}", c).ConfigureAwait(false);
            await ResolvePendingAsync(t.Id, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A partner trip voided or made cheaper must not leave the partner paid more than its trips cost (FR-085, B8 review M2): checked
    /// under the partner-payment number lock, which payments also take.
    /// </summary>
    private async Task EnsurePartnerNotOverpaidAsync(long partnerId, CancellationToken ct)
    {
        await numbering.LockAsync(Configuration.DocumentTypes.PartnerPayment, ct).ConfigureAwait(false);
        var cost = await db.Set<Trip>().Where(x => x.PartnerId == partnerId && x.Status == Active).SumAsync(x => (decimal?)x.PartnerCost, ct).ConfigureAwait(false) ?? 0;
        var paid = await db.Set<PartnerPayment>().Where(p => p.PartnerId == partnerId && p.Status == "Active").SumAsync(p => (decimal?)p.Amount, ct).ConfigureAwait(false) ?? 0;
        if (paid > cost) throw Fail.Rule(ErrorCodes.TripPartnerOverpaid, Money.Format(paid), Money.Format(cost));
    }

    /// <summary>A user with trips.edit clears Needs review after checking; recorded in history (FR-014).</summary>
    public async Task<TripResponse> ClearReviewAsync(string token, string? rowVersion, CancellationToken ct)
    {
        var t = await support.FindAsync<Trip>(token, ct).ConfigureAwait(false);
        await locks.EnsureNotLockedByOthersAsync(EntityType, t.Id, ct).ConfigureAwait(false);
        if (t.Status == Void) throw Fail.Rule(ErrorCodes.TripIsVoid, t.ReceiptNo);
        if (!t.NeedsReview) return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
        RequireRowVersion(rowVersion);
        db.ExpectRowVersion(t, rowVersion);
        t.NeedsReview = false;
        t.ReviewNote = null;
        changes.SetAction(t, "Reviewed");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes to a saved trip need the version last seen (review M2, DT-73): invoicing or settling it meanwhile changes the row,
    /// so a stale edit or void is refused with 409 instead of acting on an invoiced or settled trip.
    /// </summary>
    private static void RequireRowVersion(string? rowVersion)
    {
        if (string.IsNullOrEmpty(rowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, "rowVersion");
    }

    /// <summary>Edit and void only while active, unbilled and unsettled (7.1.2, FR-010, FR-011).</summary>
    private async Task EnsureEditableAsync(Trip t, CancellationToken ct)
    {
        if (t.Status == Void) throw Fail.Rule(ErrorCodes.TripIsVoid, t.ReceiptNo);
        if (t.InvoiceId is long inv)
        {
            var number = await db.Set<Invoice>().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(i => i.Id == inv).Select(i => i.Number).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            throw Fail.Rule(ErrorCodes.TripInvoiced, t.ReceiptNo, number ?? (En ? "a draft invoice" : "مسودة فاتورة"));
        }
        if (t.SettlementId is long stl)
        {
            var number = await db.Set<Settlement>().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).Where(x => x.Id == stl).Select(x => x.Number).SingleOrDefaultAsync(ct).ConfigureAwait(false);
            throw Fail.Rule(ErrorCodes.TripSettled, t.ReceiptNo, number);
        }
    }

    // ============================================================ draft (form) and apply (form + import)

    /// <summary>Resolves the selections of a form request; archived records are refused unless already stored on the trip.</summary>
    private async Task<TripDraft> DraftAsync(TripRequest r, Trip? current, CancellationToken ct)
    {
        var errors = new List<FieldError>();
        var rules = await fields.RulesAsync(FieldCatalog.Trip, ct).ConfigureAwait(false);
        rules.Require(errors, "driverId", string.IsNullOrWhiteSpace(r.DriverId));
        rules.Require(errors, "customerId", string.IsNullOrWhiteSpace(r.CustomerId));
        rules.Require(errors, "loadingSiteId", string.IsNullOrWhiteSpace(r.LoadingSiteId));
        rules.Require(errors, "unloadingSiteId", string.IsNullOrWhiteSpace(r.UnloadingSiteId));
        rules.Require(errors, "materialId", string.IsNullOrWhiteSpace(r.MaterialId));
        if (errors.Count > 0) throw Fail.Fields(errors);
        var driverRef = await support.ChooseAsync<Driver>("driverId", r.DriverId, current?.DriverId, x => x.FullName, ct).ConfigureAwait(false);
        var driver = await db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).SingleAsync(d => d.Id == driverRef.Id, ct).ConfigureAwait(false);
        var customer = await support.ChooseAsync<Customer>("customerId", r.CustomerId, current?.CustomerId, x => x.Name, ct).ConfigureAwait(false);
        var loading = await support.ChooseAsync<LoadingSite>("loadingSiteId", r.LoadingSiteId, current?.LoadingSiteId, x => x.Name, ct).ConfigureAwait(false);
        var unloading = await support.ChooseAsync<UnloadingSite>("unloadingSiteId", r.UnloadingSiteId, current?.UnloadingSiteId, x => x.Name, ct).ConfigureAwait(false);
        var material = await support.ChooseAsync<Material>("materialId", r.MaterialId, current?.MaterialId, x => x.Name, ct).ConfigureAwait(false);
        Partner? partner = null;
        if (!string.IsNullOrWhiteSpace(r.PartnerId))
            partner = await support.ChooseAsync<Partner>("partnerId", r.PartnerId, current?.PartnerId, x => x.Name, ct).ConfigureAwait(false);
        var partnerValue = current is null ? partner : rules.Keep("partnerId", false, partner, current.PartnerId is long pid ? await db.Set<Partner>().AsNoTracking().SingleAsync(p => p.Id == pid, ct).ConfigureAwait(false) : null);
        return new TripDraft(r.ReceiptNo, r.TripDate, r.LoadingDate, r.UnloadingDate, driver, driver.OwnershipType.FleetClass, customer, loading, unloading, material,
            partnerValue, r.LoadedWeight, r.UnloadedWeight, r.CustomerPrice, r.DriverRate, r.PartnerRate, r.Notes, r.CustomValues);
    }

    /// <summary>
    /// Validates a draft against 7.1.1 and the company's field rules, resolves prices (FR-022, FR-030, FR-032), calculates (8.2),
    /// enforces the tariff lock (FR-025) and writes the trip. Returns the non-blocking warnings (FR-002). Shared by form and import.
    /// </summary>
    public async Task<IReadOnlyList<TripWarning>> ApplyAsync(Trip t, TripDraft d, bool isNew, TripSettings s, ITripPriceSource prices, CancellationToken ct,
        string? reviewNote = null, TripBulkOptions? bulk = null)
    {
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(d);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(prices);
        var rules = await fields.RulesAsync(FieldCatalog.Trip, ct).ConfigureAwait(false);
        var errors = new List<FieldError>();
        var receipt = TextNormalizer.CleanText(TextNormalizer.ToAsciiDigits(d.ReceiptNo));
        var receiptKey = TextNormalizer.Key(receipt);
        var loadingDate = d.LoadingDate ?? (isNew ? d.TripDate : t.LoadingDate);
        var unloadingDate = rules.Keep("unloadingDate", isNew, d.UnloadingDate, t.UnloadingDate);
        var unloaded = rules.Keep("unloadedWeight", isNew, d.UnloadedWeight, t.UnloadedWeight);
        var notes = rules.Keep("notes", isNew, TextNormalizer.CleanText(d.Notes), t.Notes);

        rules.Require(errors, "receiptNo", receipt is null);
        if (receipt is { Length: > 30 }) rules.Invalid(errors, "receiptNo", ErrorCodes.ValidationRange);
        rules.Require(errors, "tripDate", d.TripDate is null);
        if (d.TripDate > s.Today) errors.Add(new FieldError("tripDate", ErrorCodes.DateFuture, [rules.Label("tripDate")]));
        if (loadingDate > s.Today) errors.Add(new FieldError("loadingDate", ErrorCodes.DateFuture, [rules.Label("loadingDate")]));
        rules.Require(errors, "unloadingDate", unloadingDate is null);
        if (unloadingDate is DateOnly ud && loadingDate is DateOnly ld && ud < ld) rules.Invalid(errors, "unloadingDate", ErrorCodes.ValidationRange);
        rules.Require(errors, "loadedWeight", d.LoadedWeight is null);
        if (d.LoadedWeight is decimal lw && (lw <= 0 || lw > MaxWeight || decimal.Round(lw, 3) != lw)) rules.Invalid(errors, "loadedWeight", ErrorCodes.ValidationRange);
        rules.Require(errors, "unloadedWeight", unloaded is null);
        if (unloaded is decimal uw && (uw < 0 || uw > MaxWeight || decimal.Round(uw, 3) != uw)) rules.Invalid(errors, "unloadedWeight", ErrorCodes.ValidationRange);
        if (d.CustomerPrice is decimal cp && !Money.IsValidAmount(cp, 4)) rules.Invalid(errors, "customerPrice", ErrorCodes.ValidationRange);
        if (d.DriverRate is decimal dr && !Money.IsValidAmount(dr, 4)) rules.Invalid(errors, "driverRate", ErrorCodes.ValidationRange);
        if (d.PartnerRate is decimal pr && !Money.IsValidAmount(pr, 4)) rules.Invalid(errors, "partnerRate", ErrorCodes.ValidationRange);
        rules.Require(errors, "notes", notes is null);
        if (notes is { Length: > 1000 }) rules.Invalid(errors, "notes", ErrorCodes.ValidationRange);
        var custom = await customFields.ValidateValuesAsync(FieldCatalog.Trip, d.CustomValues, t.CustomValues, isNew, errors, ct).ConfigureAwait(false);
        if (errors.Count > 0) throw Fail.Fields(errors);

        // A partner's truck is never part of the company's own fleet (DT-48, BR-029).
        if (d.Partner is not null && d.FleetClass == MasterCodes.Own) throw Fail.RuleOn("partnerId", ErrorCodes.DriverPartnerOwnFleet, d.Partner.Name);
        // The import checks receipt numbers against the company once for the whole file (review M6).
        if (bulk is null && await db.Set<Trip>().AnyAsync(x => x.ReceiptNoKey == receiptKey && x.Id != t.Id, ct).ConfigureAwait(false))
            throw Fail.Field("receiptNo", ErrorCodes.TripReceiptDuplicate, receipt);
        // Trip and loading dates not in a closed period (8.9), each distinct date checked once in a bulk import.
        foreach (var date in new[] { d.TripDate!.Value, loadingDate!.Value }.Distinct())
        {
            if (bulk?.OpenDates.Contains(date) == true) continue;
            await periods.EnsureOpenAsync(date, ct).ConfigureAwait(false);
            bulk?.OpenDates.Add(date);
        }

        // ---- prices (FR-022, FR-030 … FR-032). Tariff validity on the trip date (FR-020); rate month from the loading date (BR-029).
        var keysChanged = isNew || t.CustomerId != d.Customer.Id || t.MaterialId != d.Material.Id || t.LoadingSiteId != d.LoadingSite.Id
            || t.UnloadingSiteId != d.UnloadingSite.Id || t.TripDate != d.TripDate;
        var partnerKeysChanged = keysChanged || t.PartnerId != d.Partner?.Id;
        var tripKeys = new TariffKeys(d.Customer.Id, d.Material.Id, d.LoadingSite.Id, d.UnloadingSite.Id);
        // Tariff changes never alter saved trips (FR-026): an edit keeps the stored tariff price unless the keys or date changed.
        var tariffPrice = keysChanged ? (await prices.TariffAsync(PartyTypes.Customer, tripKeys, d.TripDate.Value, ct).ConfigureAwait(false))?.Price : t.TariffPrice;
        // On an edit that re-resolves the tariff, a price resent unchanged by the form counts as not typed (review M5, DT-74).
        var typedPrice = !isNew && keysChanged && d.CustomerPrice == t.CustomerPrice ? null : d.CustomerPrice;
        var customerPrice = typedPrice ?? tariffPrice ?? d.CustomerPrice;
        if (customerPrice is null) throw Fail.Field("customerPrice", ErrorCodes.ValidationRequired, rules.Label("customerPrice"));
        var lockReason = (string?)null;
        if (tariffPrice is decimal tp && customerPrice != tp)
        {
            // Tariff lock: deviating prices only for masters.manage; every deviation audited (FR-025, BR-004). An edit that keeps
            // the stored price and tariff is not a new deviation (review M4).
            var newDeviation = isNew || customerPrice != t.CustomerPrice || tp != t.TariffPrice;
            if (newDeviation && s.LockTariffs && !ctx.HasPermission(PermissionKeys.MastersManage))
                throw Fail.RuleOn("customerPrice", ErrorCodes.TariffLocked, Money.Format(tp));
            if (newDeviation) lockReason = $"{(En ? "Tariff deviation" : "انحراف عن التعرفة")}: {Money.Format(customerPrice.Value)} / {Money.Format(tp)}";
        }

        var isPartner = d.Partner is not null;
        decimal? partnerRate = null;
        if (isPartner)
        {
            partnerRate = d.PartnerRate ?? (partnerKeysChanged || t.PartnerRate is null
                ? (await prices.TariffAsync(PartyTypes.Partner, tripKeys with { PartyId = d.Partner!.Id }, d.TripDate.Value, ct).ConfigureAwait(false))?.Price
                : t.PartnerRate);
            if (partnerRate is null) throw Fail.Field("partnerRate", ErrorCodes.ValidationRequired, rules.Label("partnerRate"));
        }

        var rateMonth = PricingRules.Month(loadingDate!.Value);
        var canManual = ctx.HasPermission(PermissionKeys.MastersManage);
        DriverRateDecision decision;
        if (!isNew && !isPartner && t.PartnerId is null && t.FleetClass == d.FleetClass
            && !PricingRules.MustResolveAgain(t.LoadingSiteId, t.RateMonth, t.DriverId, d.LoadingSite.Id, rateMonth, d.Driver.Id))
        {
            // FR-032: the stored rate is kept; a different typed rate follows the manual-rate rules.
            decision = d.DriverRate is null || d.DriverRate == t.DriverRate
                ? new DriverRateDecision(t.IsManualRate ? DriverRateOutcome.Manual : DriverRateOutcome.Monthly, t.DriverRate)
                : canManual ? new DriverRateDecision(DriverRateOutcome.Manual, d.DriverRate.Value) : new DriverRateDecision(DriverRateOutcome.ManualNotAllowed, t.DriverRate);
        }
        else
        {
            var monthly = isPartner ? null : await prices.MonthlyRateAsync(d.LoadingSite.Id, rateMonth, d.FleetClass, ct).ConfigureAwait(false);
            // A rate resent unchanged by the form while the rate is resolved again counts as not typed (review M5, DT-74).
            var typedRate = !isNew && d.DriverRate == t.DriverRate ? null : d.DriverRate;
            decision = PricingRules.ResolveDriverRate(isPartner, monthly, typedRate, canManual);
        }
        if (decision.Outcome == DriverRateOutcome.ManualNotAllowed) throw Fail.RuleOn("driverRate", ErrorCodes.RateManualNotAllowed);
        if (decision.Outcome == DriverRateOutcome.Missing)
            throw Fail.RuleOn("driverRate", ErrorCodes.RateMissing, d.LoadingSite.Name, rateMonth, pricing.FleetLabel(d.FleetClass));

        // ---- calculations (8.2)
        var amounts = TripCalculator.Calculate(new TripFigures(d.LoadedWeight!.Value, unloaded, customerPrice.Value, tariffPrice, decision.Rate, partnerRate, isPartner,
            s.BillOnUnloadedWeight, s.PayDriverOnLoadedWeight, s.ShortageTolerancePct, s.Decimals));

        t.ReceiptNo = receipt!;
        t.ReceiptNoKey = receiptKey;
        t.TripDate = d.TripDate.Value;
        t.LoadingDate = loadingDate.Value;
        t.UnloadingDate = unloadingDate;
        t.DriverId = d.Driver.Id;
        t.CustomerId = d.Customer.Id;
        t.LoadingSiteId = d.LoadingSite.Id;
        t.UnloadingSiteId = d.UnloadingSite.Id;
        t.MaterialId = d.Material.Id;
        t.PartnerId = d.Partner?.Id;
        t.TruckPlateAtSave = d.Driver.PlateNumber;
        t.FleetClass = d.FleetClass;
        t.LoadedWeight = d.LoadedWeight.Value;
        t.UnloadedWeight = unloaded;
        t.BillingQty = amounts.BillingQty;
        t.WageQty = amounts.WageQty;
        t.CustomerPrice = customerPrice.Value;
        t.TariffPrice = tariffPrice;
        t.DriverRate = decision.Rate;
        t.RateSource = decision.Source;
        t.RateMonth = rateMonth;
        t.IsManualRate = decision.IsManual;
        t.PartnerRate = partnerRate;
        t.Revenue = amounts.Revenue;
        t.DriverWage = amounts.DriverWage;
        t.PartnerCost = amounts.PartnerCost;
        t.ShortageWeight = amounts.ShortageWeight;
        t.ShortagePct = amounts.ShortagePct;
        t.IsShortageFlagged = amounts.IsShortageFlagged;
        t.IsTariffBreach = amounts.IsTariffBreach;
        t.Notes = notes;
        t.CustomValues = custom;
        if (reviewNote is not null)
        {
            t.NeedsReview = true;
            t.ReviewNote = reviewNote.Length <= 500 ? reviewNote : reviewNote[..500];
        }
        var reasons = new[] { lockReason, decision.IsManual && (isNew || (d.DriverRate is not null && d.DriverRate != t.DriverRate)) ? $"{(En ? "Manual driver rate" : "أجرة سائق يدوية")} {Money.Format(decision.Rate)}" : null }
            .Where(x => x is not null).ToList();
        if (reasons.Count > 0) changes.SetAction(t, isNew ? "Create" : "Update", string.Join(" · ", reasons));
        return Warnings(d, unloaded, s.Today);
    }

    private List<TripWarning> Warnings(TripDraft d, decimal? unloaded, DateOnly today)
    {
        var list = new List<TripWarning>();
        string W(string code, params object?[] args) => ErrorCatalog.Format(code, ctx.Language, args);
        if (TripCalculator.OverCapacity(d.LoadedWeight!.Value, d.Driver.CapacityTons))
            list.Add(new TripWarning(ErrorCodes.TripWarnCapacity, W(ErrorCodes.TripWarnCapacity, Money.Format(d.LoadedWeight.Value), Money.Format(d.Driver.CapacityTons))));
        if (TripCalculator.UnloadedTooHigh(d.LoadedWeight.Value, unloaded))
            list.Add(new TripWarning(ErrorCodes.TripWarnUnloaded, W(ErrorCodes.TripWarnUnloaded)));
        foreach (var a in DriverService.Alerts(d.Driver, today).Where(a => a.Expired))
            list.Add(new TripWarning(ErrorCodes.TripWarnDocument, W(ErrorCodes.TripWarnDocument, DocumentName(a.Document), a.ExpiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));
        return list;
    }

    private string DocumentName(string doc) => (En, doc) switch
    {
        (true, "licence") => "driving licence",
        (true, "registration") => "truck registration",
        (true, "insurance") => "truck insurance",
        (true, _) => "truck inspection",
        (false, "licence") => "رخصة القيادة",
        (false, "registration") => "ترخيص الشاحنة",
        (false, "insurance") => "تأمين الشاحنة",
        (false, _) => "فحص الشاحنة",
    };

    // ============================================================ posting (FR-016, 8.6)

    private static (DateOnly, string, long, long?, decimal, decimal) EntryKey(Trip t) => (t.TripDate, t.ReceiptNo, t.DriverId, t.PartnerId, t.DriverWage, t.PartnerCost);

    /// <summary>
    /// The entry a trip posts: own/external fleet with wage accrual on → driver wage expense / driver payable (party driver);
    /// partner trip → partner cost expense / partner payable (party partner), always. Null when nothing is due (accrual off, or 0).
    /// </summary>
    public PostingRequest? BuildEntry(Trip t, bool accrueDriverWage)
    {
        ArgumentNullException.ThrowIfNull(t);
        if (t.Status == Void) return null;
        var description = $"{(En ? "Trip" : "رحلة")} {t.ReceiptNo}";
        if (t.PartnerId is long partnerId)
        {
            if (t.PartnerCost <= 0) return null;
            var party = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyPartner, partnerId);
            return new PostingRequest(t.TripDate, description, AccountingCodes.ModuleTrn, SourceType, t.Id, false,
                [PostingLine.DebitKey(PostingKeys.PartnerCostExpense, t.PartnerCost), PostingLine.CreditKey(PostingKeys.PartnerPayable, t.PartnerCost, party)]);
        }
        if (!accrueDriverWage || t.DriverWage <= 0) return null;
        var driver = new PostingParty(AccountingCodes.ModuleTrn, AccountingCodes.PartyDriver, t.DriverId);
        return new PostingRequest(t.TripDate, description, AccountingCodes.ModuleTrn, SourceType, t.Id, false,
            [PostingLine.DebitKey(PostingKeys.DriverWageExpense, t.DriverWage), PostingLine.CreditKey(PostingKeys.DriverPayable, t.DriverWage, driver)]);
    }

    internal async Task PostAsync(Trip t, TripSettings s, CancellationToken ct)
    {
        var request = BuildEntry(t, s.AccrueDriverWage);
        if (request is null)
        {
            t.JournalEntryId = null;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await ResolvePendingAsync(t.Id, ct).ConfigureAwait(false);
            return;
        }
        var entry = await posting.PostOrPendAsync(request, ct).ConfigureAwait(false);
        t.JournalEntryId = entry?.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (entry is not null) await ResolvePendingAsync(t.Id, ct).ConfigureAwait(false);
    }

    /// <summary>Reverses the trip's posted entry (edit or void, BR-035); a waiting pending posting needs no reversal.</summary>
    private async Task ReverseEntryAsync(Trip t, string description, CancellationToken ct)
    {
        if (t.JournalEntryId is not long entryId) return;
        var entry = await db.Set<JournalEntry>().SingleAsync(e => e.Id == entryId, ct).ConfigureAwait(false);
        if (entry.Status == AccountingCodes.Posted) await posting.ReverseAsync(entry, description, null, ct).ConfigureAwait(false);
        t.JournalEntryId = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task ResolvePendingAsync(long tripId, CancellationToken ct)
    {
        var open = await db.Set<PendingPosting>().Where(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == tripId && p.ResolvedAt == null)
            .ToListAsync(ct).ConfigureAwait(false);
        if (open.Count == 0) return;
        foreach (var p in open) p.ResolvedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ============================================================ reads

    internal sealed record Row(Trip T, Guid DriverPid, string DriverName, Guid CustomerPid, string CustomerName, Guid LoadingPid, string LoadingName,
        Guid UnloadingPid, string UnloadingName, Guid MaterialPid, string MaterialName, Guid? PartnerPid, string? PartnerName,
        Guid? InvoicePid, string? InvoiceNumber, Guid? SettlementPid, string? SettlementNumber, Guid? EntryPid, string? EntryNumber, bool Pending);

    internal IQueryable<Row> Rows(IQueryable<Trip> q) => q.Select(t => new Row(t,
        t.Driver.PublicId, t.Driver.FullName, t.Customer.PublicId, t.Customer.Name, t.LoadingSite.PublicId, t.LoadingSite.Name,
        t.UnloadingSite.PublicId, t.UnloadingSite.Name, t.Material.PublicId, t.Material.Name,
        t.Partner != null ? t.Partner.PublicId : null, t.Partner != null ? t.Partner.Name : null,
        t.Invoice != null ? t.Invoice.PublicId : null, t.Invoice != null ? t.Invoice.Number : null,
        t.Settlement != null ? t.Settlement.PublicId : null, t.Settlement != null ? t.Settlement.Number : null,
        t.JournalEntry != null ? t.JournalEntry.PublicId : null, t.JournalEntry != null ? t.JournalEntry.Number : null,
        db.Set<PendingPosting>().Any(p => p.SourceModule == AccountingCodes.ModuleTrn && p.SourceType == SourceType && p.SourceId == t.Id && p.ResolvedAt == null)));

    private static readonly IReadOnlyDictionary<string, Expression<Func<Trip, object?>>> SortFields = new Dictionary<string, Expression<Func<Trip, object?>>>(StringComparer.Ordinal)
    {
        ["tripDate"] = t => t.TripDate,
        ["receiptNo"] = t => t.ReceiptNoKey,
        ["driver"] = t => t.Driver.FullName,
        ["customer"] = t => t.Customer.Name,
        ["loadedWeight"] = t => t.LoadedWeight,
        ["revenue"] = t => t.Revenue,
        ["shortagePct"] = t => t.ShortagePct,
        ["createdAt"] = t => t.CreatedAt,
    };

    public async Task<TripPage> ListAsync(TripListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var filtered = await SearchAsync(Filter(q), q.Q, ct).ConfigureAwait(false);
        var totals = await filtered.GroupBy(_ => 1).Select(g => new TripTotals(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.UnloadedWeight ?? 0m),
            g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), g.Sum(t => t.Revenue - t.DriverWage - t.PartnerCost))).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new TripTotals(0, 0, 0, 0, 0, 0, 0);
        var paging = new PageRequest(q.Page, q.PageSize);
        var sorted = MasterDataSupport.Sort(filtered, q.Sort, SortFields, "-tripDate").ThenByDescending(t => t.Id);
        var rows = await Rows(sorted.Skip(paging.Skip).Take(paging.SafePageSize)).ToListAsync(ct).ConfigureAwait(false);
        return new TripPage(new PagedResult<TripResponse>(rows.Select(ToResponse).ToList(), paging.SafePage, paging.SafePageSize, totals.Trips), totals);
    }

    /// <summary>Every trip of the filtered list in list order (export; at most 50,000 rows).</summary>
    internal async Task<IReadOnlyList<Row>> AllAsync(TripListQuery q, CancellationToken ct) =>
        await Rows(MasterDataSupport.Sort(await SearchAsync(Filter(q), q.Q, ct).ConfigureAwait(false), q.Sort, SortFields, "-tripDate").ThenByDescending(t => t.Id).Take(50_000))
            .ToListAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Search by any part of the receipt number, case and spaces ignored (DT-75). Drivers (with their plates), customers and sites
    /// have their own filters; the Ctrl+K global search covers plates (FR-187). With 500,000 trips (NFR-01) the match runs once
    /// over the narrow receipt index and the list, totals and page then use the matching ids; a very broad search (over 5,000
    /// matches) keeps the LIKE predicate.
    /// </summary>
    private async Task<IQueryable<Trip>> SearchAsync(IQueryable<Trip> query, string? q, CancellationToken ct)
    {
        if (MasterDataSupport.SearchText(q) is not { } text) return query;
        var key = TextNormalizer.Key(text).Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal);
        var pattern = $"%{key}%";
        // DT-206: the binary-collation copy of the key (indexed per company); the key is already upper-case with ASCII digits.
        var matching = query.Where(t => EF.Functions.Like(t.ReceiptNoSearch!, pattern));
        var ids = await matching.Select(t => t.Id).Take(5001).ToListAsync(ct).ConfigureAwait(false);
        return ids.Count <= 5000 ? db.Set<Trip>().AsNoTracking().Where(t => ids.Contains(t.Id)) : matching;
    }

    private IQueryable<Trip> Filter(TripListQuery q)
    {
        var query = db.Set<Trip>().AsNoTracking();
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "active" => query.Where(t => t.Status == Active),
            "void" => query.Where(t => t.Status == Void),
            "all" => query,
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, "status"),
        };
        query = (q.Billing?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "unbilled" => query.Where(t => t.InvoiceId == null),
            "invoiced" => query.Where(t => t.InvoiceId != null),
            _ => throw Fail.Field("billing", ErrorCodes.ValidationFormat, "billing"),
        };
        query = YesNo(q.Settled, "settled") switch { true => query.Where(t => t.SettlementId != null), false => query.Where(t => t.SettlementId == null), _ => query };
        query = (q.Fleet?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "own" => query.Where(t => t.FleetClass == MasterCodes.Own),
            "external" => query.Where(t => t.FleetClass == MasterCodes.External),
            _ => throw Fail.Field("fleet", ErrorCodes.ValidationFormat, "fleet"),
        };
        query = YesNo(q.PartnerTrips, "partnerTrips") switch { true => query.Where(t => t.PartnerId != null), false => query.Where(t => t.PartnerId == null), _ => query };
        if (q.Shortage is bool sh) query = query.Where(t => t.IsShortageFlagged == sh);
        if (q.Breach is bool br) query = query.Where(t => t.IsTariffBreach == br);
        if (q.Manual is bool mr) query = query.Where(t => t.IsManualRate == mr);
        if (q.Review is bool rv) query = query.Where(t => t.NeedsReview == rv);
        if (q.From is DateOnly from) query = query.Where(t => t.TripDate >= from);
        if (q.To is DateOnly to) query = query.Where(t => t.TripDate <= to);
        if (Pid<Driver>(q.Driver, "driver") is Guid d) query = query.Where(t => t.Driver.PublicId == d);
        if (Pid<Customer>(q.Customer, "customer") is Guid c) query = query.Where(t => t.Customer.PublicId == c);
        if (Pid<Partner>(q.Partner, "partner") is Guid p) query = query.Where(t => t.Partner != null && t.Partner.PublicId == p);
        if (Pid<LoadingSite>(q.LoadingSite, "loadingSite") is Guid l) query = query.Where(t => t.LoadingSite.PublicId == l);
        if (Pid<UnloadingSite>(q.UnloadingSite, "unloadingSite") is Guid u) query = query.Where(t => t.UnloadingSite.PublicId == u);
        if (Pid<Material>(q.Material, "material") is Guid m) query = query.Where(t => t.Material.PublicId == m);
        return query;
    }

    private Guid? Pid<T>(string? token, string field) =>
        string.IsNullOrWhiteSpace(token) ? null : support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);

    private static bool? YesNo(string? text, string field) => (text?.Trim().ToLowerInvariant()) switch
    {
        null or "" => null,
        "yes" or "true" => true,
        "no" or "false" => false,
        _ => throw Fail.Field(field, ErrorCodes.ValidationFormat, field),
    };

    /// <summary>The last ten trips saved by the current user (FR-008).</summary>
    public async Task<IReadOnlyList<TripResponse>> RecentAsync(CancellationToken ct)
    {
        var uid = ctx.UserId;
        var rows = await Rows(db.Set<Trip>().AsNoTracking().Where(t => t.CreatedBy == uid).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(10))
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(ToResponse).ToList();
    }

    /// <summary>Quick-entry defaults from the user's last saved trip, still active records only (FR-007, DT-70).</summary>
    public async Task<QuickEntryDefaults> QuickEntryAsync(CancellationToken ct)
    {
        var uid = ctx.UserId;
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var last = await db.Set<Trip>().AsNoTracking().Where(t => t.CreatedBy == uid).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Select(t => new
            {
                D = t.Driver.Status == MasterCodes.Active ? new { t.Driver.PublicId, Name = t.Driver.FullName } : null,
                C = t.Customer.Status == MasterCodes.Active ? new { t.Customer.PublicId, t.Customer.Name } : null,
                M = t.Material.Status == MasterCodes.Active ? new { t.Material.PublicId, t.Material.Name } : null,
                L = t.LoadingSite.Status == MasterCodes.Active ? new { t.LoadingSite.PublicId, t.LoadingSite.Name } : null,
                U = t.UnloadingSite.Status == MasterCodes.Active ? new { t.UnloadingSite.PublicId, t.UnloadingSite.Name } : null,
            }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new QuickEntryDefaults(today,
            last?.D is { } d ? new Ref(support.Token<Driver>(d.PublicId), d.Name) : null,
            last?.C is { } c ? new Ref(support.Token<Customer>(c.PublicId), c.Name) : null,
            last?.M is { } m ? new Ref(support.Token<Material>(m.PublicId), m.Name) : null,
            last?.L is { } l ? new Ref(support.Token<LoadingSite>(l.PublicId), l.Name) : null,
            last?.U is { } u ? new Ref(support.Token<UnloadingSite>(u.PublicId), u.Name) : null);
    }

    public async Task<TripDetails> GetAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<Trip>(token, ct).ConfigureAwait(false);
        var response = await ResponseAsync(t.Id, ct).ConfigureAwait(false);
        var driver = await db.Set<Driver>().AsNoTracking().IgnoreQueryFilters([Accounting.QueryFilterNames.SoftDelete]).SingleAsync(x => x.Id == t.DriverId, ct).ConfigureAwait(false);
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var warnings = t.Status == Void ? [] : Warnings(new TripDraft(t.ReceiptNo, t.TripDate, t.LoadingDate, t.UnloadingDate, driver, t.FleetClass, null!, null!, null!, null!, null,
            t.LoadedWeight, t.UnloadedWeight, t.CustomerPrice, t.DriverRate, t.PartnerRate, t.Notes, null), t.UnloadedWeight, today);
        return new TripDetails(response, warnings, await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false));
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string token, CancellationToken ct)
    {
        var t = await support.FindAsync<Trip>(token, ct).ConfigureAwait(false);
        return await support.HistoryAsync(EntityType, t.Id, ct).ConfigureAwait(false);
    }

    internal async Task<TripResponse> ResponseAsync(long id, CancellationToken ct) =>
        ToResponse(await Rows(db.Set<Trip>().AsNoTracking().Where(t => t.Id == id)).SingleAsync(ct).ConfigureAwait(false));

    internal TripResponse ToResponse(Row r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var t = r.T;
        return new TripResponse(support.Token<Trip>(t.PublicId), t.ReceiptNo, t.TripDate, t.LoadingDate, t.UnloadingDate,
            new Ref(support.Token<Driver>(r.DriverPid), r.DriverName), t.TruckPlateAtSave, new Ref(support.Token<Customer>(r.CustomerPid), r.CustomerName),
            new Ref(support.Token<LoadingSite>(r.LoadingPid), r.LoadingName), new Ref(support.Token<UnloadingSite>(r.UnloadingPid), r.UnloadingName),
            new Ref(support.Token<Material>(r.MaterialPid), r.MaterialName), r.PartnerPid is Guid p ? new Ref(support.Token<Partner>(p), r.PartnerName!) : null, t.FleetClass,
            t.LoadedWeight, t.UnloadedWeight, t.BillingQty, t.WageQty, t.CustomerPrice, t.TariffPrice,
            t.TariffPrice is decimal tp ? Money.Round((t.CustomerPrice - tp) * t.BillingQty) : null,
            t.DriverRate, t.RateSource, t.RateMonth, t.PartnerRate, t.Revenue, t.DriverWage, t.PartnerCost, t.Revenue - t.DriverWage - t.PartnerCost,
            t.ShortageWeight, t.ShortagePct, t.IsShortageFlagged, t.IsTariffBreach, t.IsManualRate, t.NeedsReview, t.ReviewNote,
            t.Status, t.InvoiceId is null ? "Unbilled" : "Invoiced", t.SettlementId is not null, t.Status == Active && t.InvoiceId is null && t.SettlementId is null,
            r.InvoicePid is Guid i ? new DocRef(support.Token<Invoice>(i), r.InvoiceNumber) : null,
            r.SettlementPid is Guid s ? new DocRef(support.Token<Settlement>(s), r.SettlementNumber) : null,
            r.EntryPid is Guid e ? new DocRef(support.Token<JournalEntry>(e), r.EntryNumber) : null, r.Pending,
            t.VoidReason, t.Notes, CustomFieldService.Read(t.CustomValues), DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(t.RowVersion));
    }
}
