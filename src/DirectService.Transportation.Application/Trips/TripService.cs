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
    CompanySettingsService settings, IMonthlyRateLock rateLock, INumberingService numbering, AuditTrail audit, Platform.PlanLimits? limits = null)
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
            var (oldPartner, oldCost) = (t.PartnerId, t.PartnerCost);
            warnings = await ApplyAsync(t, draft, isNew: false, s, new DbPriceSource(rateLock, pricing), c).ConfigureAwait(false);
            // DT-230: moved to another partner or made cheaper after the partner was paid — the user's choice and reason are required.
            if (await PartnerPaidAsync(t, oldPartner, oldCost, r.PartnerPaidHandling, TextNormalizer.CleanText(r.PartnerPaidReason) is { } pr ? pr[..Math.Min(pr.Length, 400)] : null, "partnerPaidReason", c).ConfigureAwait(false)
                is { } paid)
                audit.Record(EntityType, t.Id, "PartnerPaidChange", paid);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
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
            var (oldPartner, oldCost) = (t.PartnerId, t.PartnerCost);
            t.Status = Void;
            t.VoidReason = why;
            t.VoidedBy = ctx.UserId ?? throw new InvalidOperationException("A void needs an acting user.");
            t.VoidedAt = DateTime.UtcNow;
            // DT-230: a trip already paid to its partner — the choice for the amount goes with the void reason into the history.
            var paid = await PartnerPaidAsync(t, oldPartner, oldCost, r.PartnerPaidHandling, why, "reason", c).ConfigureAwait(false);
            changes.SetAction(t, "Void", paid ?? why);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            await ReverseEntryAsync(t, En ? $"Void of trip {t.ReceiptNo}" : $"إلغاء الرحلة {t.ReceiptNo}", c).ConfigureAwait(false);
            await ResolvePendingAsync(t.Id, c).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
        return await ResponseAsync(t.Id, ct).ConfigureAwait(false);
    }

    private static readonly string[] PartnerPaidChoices = ["Return", "Deduct"];

    /// <summary>
    /// Owner decision 21 Oct 2026 on DT-228 (DT-230): a partner trip voided, moved to another partner or made cheaper after the partner was paid
    /// leaves the partner owing the company. Then the change needs the partner-payment permission (drv.settle), a reason and the user's choice
    /// for the amount already paid — "Return" (owes the company until a partner repayment) or "Deduct" (set off against the next payment(s));
    /// the third answer, "the payment was not actually made", is to void that payment first (CashVoidRequest.ForTrip). Without a choice the
    /// change is refused with <c>trip.partnerPaid</c> carrying the amount and the partner's last payment, so the screen can ask. Checked under
    /// the partner-payment number lock, which payments also take. Returns the history text (reason · choice), or null when nothing was paid.
    /// </summary>
    private async Task<string?> PartnerPaidAsync(Trip t, long? oldPartner, decimal oldCost, string? handling, string? reason, string reasonField, CancellationToken ct)
    {
        if (oldPartner is not long partnerId) return null;
        await numbering.LockAsync(Configuration.DocumentTypes.PartnerPayment, ct).ConfigureAwait(false);
        var others = await db.Set<Trip>().Where(x => x.PartnerId == partnerId && x.Status == Active && x.Id != t.Id).SumAsync(x => (decimal?)x.PartnerCost, ct).ConfigureAwait(false) ?? 0;
        var paid = await db.Set<PartnerPayment>().Where(p => p.PartnerId == partnerId && p.Status == "Active")
            .SumAsync(p => (decimal?)(p.Kind == "Repayment" ? -p.Amount : p.Amount), ct).ConfigureAwait(false) ?? 0;
        var after = others + (t.Status == Active && t.PartnerId == partnerId ? t.PartnerCost : 0);
        var overpaid = Math.Max(0, paid - after) - Math.Max(0, paid - (others + oldCost));
        if (overpaid <= 0) return null;
        if (!ctx.HasPermission(PermissionKeys.DrvSettle)) throw new AppException(ErrorCodes.TripPartnerPaidPermission, 403);
        var mode = PartnerPaidChoices.FirstOrDefault(m => string.Equals(m, handling?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            var last = await db.Set<PartnerPayment>().AsNoTracking().Where(p => p.PartnerId == partnerId && p.Status == "Active" && p.Kind == "Payment")
                .OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Id).Select(p => new { p.PublicId, p.Number, p.Amount }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
            throw new AppException(ErrorCodes.TripPartnerPaid, 409, "partnerPaidHandling", Money.Format(overpaid))
            {
                Extra = new Dictionary<string, object?>
                {
                    ["overpaid"] = overpaid, ["tripCost"] = oldCost,
                    ["paymentId"] = last is null ? null : support.Token<PartnerPayment>(last.PublicId), ["paymentNumber"] = last?.Number, ["paymentAmount"] = last?.Amount,
                },
            };
        }
        if (string.IsNullOrWhiteSpace(reason)) throw Fail.Field(reasonField, ErrorCodes.ReasonRequired);
        db.Set<PartnerOverpayment>().Add(new PartnerOverpayment { PartnerId = partnerId, TripId = t.Id, Amount = overpaid, Handling = mode, Reason = reason });
        return reason + " · " + (mode == "Return"
            ? (En ? $"paid to the partner {Money.Format(overpaid)}: the partner will return it" : $"مدفوع للشريك {Money.Format(overpaid)}: سيعيده الشريك")
            : (En ? $"paid to the partner {Money.Format(overpaid)}: deducted from the next payment(s)" : $"مدفوع للشريك {Money.Format(overpaid)}: يُخصم من الدفعة التالية"));
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
        if (string.IsNullOrEmpty(rowVersion)) throw Fail.Field("rowVersion", ErrorCodes.ValidationRequired, new FieldRef("rowVersion"));
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
        ["partner"] = t => t.Partner!.Name,
        ["invoice"] = t => t.Invoice!.Number,
    };

    /// <summary>
    /// DT-271 (owner findings 27 Oct 2026): "settled" means paid, for every trip. A driver trip (no partner) is paid by the driver settlement
    /// that holds it; a partner trip when the partner's net payments cover its cost — payments allocated to the partner's active trips oldest
    /// first (trip date, receipt number), the DT-230 rule. A partly covered trip is unpaid, a voided one never paid. The trip list uses the
    /// <see cref="PaymentCuts"/> (filter, totals, paging, export); the report builder this expression over the view trn.TRN_TripPartnerCoverage.
    /// </summary>
    public static readonly Expression<Func<Trip, bool>> IsPaid = t =>
        (t.PartnerId == null && t.SettlementId != null) || (t.PartnerId != null && t.PartnerCoverage != null && t.PartnerCoverage.OpenAmount == 0);

    public async Task<TripPage> ListAsync(TripListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var (query, cuts) = await FilterAsync(q, ct).ConfigureAwait(false);
        var filtered = await SearchAsync(query, q.Q, ct).ConfigureAwait(false);
        var totals = await filtered.GroupBy(_ => 1).Select(g => new TripTotals(g.Count(), g.Sum(t => t.LoadedWeight), g.Sum(t => t.UnloadedWeight ?? 0m),
            g.Sum(t => t.Revenue), g.Sum(t => t.DriverWage), g.Sum(t => t.PartnerCost), g.Sum(t => t.Revenue - t.DriverWage - t.PartnerCost))).SingleOrDefaultAsync(ct).ConfigureAwait(false)
            ?? new TripTotals(0, 0, 0, 0, 0, 0, 0);
        var paging = new PageRequest(q.Page, q.PageSize);
        var sorted = MasterDataSupport.Sort(filtered, q.Sort, SortFields, "-tripDate").ThenByDescending(t => t.Id);
        var rows = await Rows(sorted.Skip(paging.Skip).Take(paging.SafePageSize)).ToListAsync(ct).ConfigureAwait(false);
        var open = await PartnerOpenAsync(rows, ct, cuts).ConfigureAwait(false);
        return new TripPage(new PagedResult<TripResponse>(rows.Select(r => ToResponse(r, open)).ToList(), paging.SafePage, paging.SafePageSize, totals.Trips), totals);
    }

    /// <summary>Every trip of the filtered list in list order (export; at most 50,000 rows) with the payment cuts used to filter them, if any.</summary>
    internal async Task<(IReadOnlyList<Row> Rows, PaymentCuts? Cuts)> AllAsync(TripListQuery q, CancellationToken ct)
    {
        var (query, cuts) = await FilterAsync(q, ct).ConfigureAwait(false);
        var rows = await Rows(MasterDataSupport.Sort(await SearchAsync(query, q.Q, ct).ConfigureAwait(false), q.Sort, SortFields, "-tripDate").ThenByDescending(t => t.Id).Take(50_000))
            .ToListAsync(ct).ConfigureAwait(false);
        return (rows, cuts);
    }

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

    /// <summary>The list filters; "settled" reads the partners' payment cuts first (DT-271), which the rows then reuse.</summary>
    private async Task<(IQueryable<Trip> Query, PaymentCuts? Cuts)> FilterAsync(TripListQuery q, CancellationToken ct)
    {
        var query = Filter(q);
        long? partner = null;
        if (Pid<Partner>(q.Partner, "partner") is Guid p)
        {
            // By the partner's id rather than a join on its public id, so the partner trips' index (IX_TRN_Trips_PartnerOrder) serves it.
            partner = await db.Set<Partner>().AsNoTracking().Where(x => x.PublicId == p).Select(x => (long?)x.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? -1;
            query = query.Where(t => t.PartnerId == partner);
        }
        var settled = YesNo(q.Settled, "settled");
        if (settled is null) return (query, null);
        var cuts = await CutsAsync(partner is long one ? [one] : null, ct).ConfigureAwait(false);
        var unpaid = cuts.Unpaid();   // true only for active partner trips not fully paid
        Expression<Func<Trip, bool>> driverOpen = t => t.PartnerId == null && t.SettlementId == null;
        Expression<Func<Trip, bool>> partnerVoid = t => t.PartnerId != null && t.Status != Active;
        Expression<Func<Trip, bool>> paidCandidates = t => (t.PartnerId == null && t.SettlementId != null) || (t.PartnerId != null && t.Status == Active);
        query = settled.Value
            ? query.Where(paidCandidates).Where(Expression.Lambda<Func<Trip, bool>>(Expression.Not(unpaid.Body), unpaid.Parameters))
            : query.Where(Or(Or(driverOpen, partnerVoid), unpaid));
        return (query, cuts);
    }

    /// <summary>a || b over one trip parameter.</summary>
    private static Expression<Func<Trip, bool>> Or(Expression<Func<Trip, bool>> a, Expression<Func<Trip, bool>> b) =>
        Expression.Lambda<Func<Trip, bool>>(Expression.OrElse(a.Body, new Rebind(b.Parameters[0], a.Parameters[0]).Visit(b.Body)), a.Parameters);

    private sealed class Rebind(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    private IQueryable<Trip> Filter(TripListQuery q)
    {
        var query = db.Set<Trip>().AsNoTracking();
        query = (q.Status?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "active" => query.Where(t => t.Status == Active),
            "void" => query.Where(t => t.Status == Void),
            "all" => query,
            _ => throw Fail.Field("status", ErrorCodes.ValidationFormat, new FieldRef("status")),
        };
        query = (q.Billing?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "unbilled" => query.Where(t => t.InvoiceId == null),
            "invoiced" => query.Where(t => t.InvoiceId != null),
            _ => throw Fail.Field("billing", ErrorCodes.ValidationFormat, new FieldRef("billing")),
        };
        query = (q.Fleet?.Trim().ToLowerInvariant()) switch
        {
            null or "" => query,
            "own" => query.Where(t => t.FleetClass == MasterCodes.Own),
            "external" => query.Where(t => t.FleetClass == MasterCodes.External),
            _ => throw Fail.Field("fleet", ErrorCodes.ValidationFormat, new FieldRef("fleet")),
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
        if (Pid<LoadingSite>(q.LoadingSite, "loadingSite") is Guid l) query = query.Where(t => t.LoadingSite.PublicId == l);
        if (Pid<UnloadingSite>(q.UnloadingSite, "unloadingSite") is Guid u) query = query.Where(t => t.UnloadingSite.PublicId == u);
        if (Pid<Material>(q.Material, "material") is Guid m) query = query.Where(t => t.Material.PublicId == m);
        return query;
    }

    private Guid? Pid<T>(string? token, string field) =>
        string.IsNullOrWhiteSpace(token) ? null : support.Decode<T>(token) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field));

    private static bool? YesNo(string? text, string field) => (text?.Trim().ToLowerInvariant()) switch
    {
        null or "" => null,
        "yes" or "true" => true,
        "no" or "false" => false,
        _ => throw Fail.Field(field, ErrorCodes.ValidationFormat, new FieldRef(field)),
    };

    /// <summary>The last ten trips saved by the current user (FR-008).</summary>
    public async Task<IReadOnlyList<TripResponse>> RecentAsync(CancellationToken ct)
    {
        var uid = ctx.UserId;
        var rows = await Rows(db.Set<Trip>().AsNoTracking().Where(t => t.CreatedBy == uid).OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).Take(10))
            .ToListAsync(ct).ConfigureAwait(false);
        var open = await PartnerOpenAsync(rows, ct).ConfigureAwait(false);
        return rows.Select(r => ToResponse(r, open)).ToList();
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

    internal async Task<TripResponse> ResponseAsync(long id, CancellationToken ct)
    {
        var row = await Rows(db.Set<Trip>().AsNoTracking().Where(t => t.Id == id)).SingleAsync(ct).ConfigureAwait(false);
        return ToResponse(row, await PartnerOpenAsync([row], ct).ConfigureAwait(false));
    }

    /// <summary>
    /// DT-271: where each partner's payments run out — the boundary day (trn.TRN_PartnerPaymentBoundary) and, on that day, the open part of
    /// each trip in receipt-number order. Trips before the day are paid, trips after it open; a partner without a boundary is paid in full.
    /// Two small queries (one row per partner, then the boundary days' trips) instead of a running total over every trip, so the list
    /// filter is a plain condition on the trip date (NFR-01: 500,000 trips).
    /// </summary>
    internal sealed class PaymentCuts(IReadOnlyDictionary<long, (DateOnly Day, IReadOnlyDictionary<long, decimal> DayOpen)> cuts)
    {
        public static readonly PaymentCuts None = new(new Dictionary<long, (DateOnly, IReadOnlyDictionary<long, decimal>)>());

        public IReadOnlyDictionary<long, (DateOnly Day, IReadOnlyDictionary<long, decimal> DayOpen)> Partners { get; } = cuts;

        /// <summary>The part still to pay of an active partner trip.</summary>
        public decimal Open(Trip t) =>
            t.PartnerId is not long p || !Partners.TryGetValue(p, out var cut) || t.TripDate < cut.Day ? 0m
            : t.TripDate > cut.Day ? t.PartnerCost
            : cut.DayOpen.TryGetValue(t.Id, out var open) ? open : 0m;

        /// <summary>The active partner trips not yet fully paid: after their partner's boundary day, or open on it.</summary>
        public Expression<Func<Trip, bool>> Unpaid()
        {
            var t = Expression.Parameter(typeof(Trip), "t");
            Expression body = Expression.Constant(false);
            foreach (var (partner, cut) in Partners)
                body = Expression.OrElse(body, Expression.AndAlso(
                    Expression.Equal(Expression.Property(t, nameof(Trip.PartnerId)), Expression.Constant(partner, typeof(long?))),
                    Expression.GreaterThan(Expression.Property(t, nameof(Trip.TripDate)), Expression.Constant(cut.Day))));
            var open = Partners.Values.SelectMany(c => c.DayOpen.Where(o => o.Value > 0).Select(o => o.Key)).ToList();
            if (open.Count > 0)
                body = Expression.OrElse(body, Expression.Call(typeof(Enumerable), nameof(Enumerable.Contains), [typeof(long)], Expression.Constant(open), Expression.Property(t, nameof(Trip.Id))));
            return Expression.Lambda<Func<Trip, bool>>(body, t);
        }
    }

    /// <summary>The payment cuts of the given partners (null = every partner of the company).</summary>
    internal async Task<PaymentCuts> CutsAsync(IReadOnlyCollection<long>? partners, CancellationToken ct)
    {
        var query = db.Set<PartnerPaymentBoundary>().AsNoTracking().Where(b => b.BoundaryDate != null);
        // The partners as constants (IN (…)): the filter then reaches the per-day totals of only these partners in the view.
        if (partners is not null) query = query.Where(b => EF.Constant(partners.ToList()).Contains(b.PartnerId));
        var bounds = await query.Select(b => new { b.PartnerId, Day = b.BoundaryDate!.Value, b.Paid, b.CostBefore }).ToListAsync(ct).ConfigureAwait(false);
        if (bounds.Count == 0) return PaymentCuts.None;
        var ids = bounds.Select(b => b.PartnerId).ToList();
        var days = bounds.Select(b => b.Day).Distinct().ToList();
        // The boundary days' trips in the view's order (receipt number in the database's collation, then id).
        var dayTrips = await db.Set<Trip>().AsNoTracking()
            .Where(t => t.Status == Active && t.PartnerId != null && EF.Constant(ids).Contains(t.PartnerId.Value) && days.Contains(t.TripDate))
            .OrderBy(t => t.ReceiptNoKey).ThenBy(t => t.Id).Select(t => new { t.Id, Partner = t.PartnerId!.Value, t.TripDate, t.PartnerCost })
            .ToListAsync(ct).ConfigureAwait(false);
        var cuts = new Dictionary<long, (DateOnly, IReadOnlyDictionary<long, decimal>)>();
        foreach (var b in bounds)
        {
            var open = new Dictionary<long, decimal>();
            var toDate = b.CostBefore;
            foreach (var t in dayTrips.Where(x => x.Partner == b.PartnerId && x.TripDate == b.Day))
            {
                toDate += t.PartnerCost;
                open[t.Id] = toDate <= b.Paid ? 0m : toDate - t.PartnerCost >= b.Paid ? t.PartnerCost : toDate - b.Paid;
            }
            cuts[b.PartnerId] = (b.Day, open);
        }
        return new PaymentCuts(cuts);
    }

    /// <summary>DT-271: the part still to pay of the rows' active partner trips (trip id → open amount; absent = not a partner trip).</summary>
    internal async Task<IReadOnlyDictionary<long, decimal>> PartnerOpenAsync(IReadOnlyCollection<Row> rows, CancellationToken ct, PaymentCuts? known = null)
    {
        var trips = rows.Where(r => r.T.PartnerId is not null && r.T.Status == Active).Select(r => r.T).ToList();
        if (trips.Count == 0) return new Dictionary<long, decimal>();
        var cuts = known ?? await CutsAsync(trips.Select(t => t.PartnerId!.Value).Distinct().ToList(), ct).ConfigureAwait(false);
        return trips.ToDictionary(t => t.Id, cuts.Open);
    }

    /// <summary>Payment state of a trip (DT-271): driver trips by their settlement, partner trips by the open part of their cost.</summary>
    private static (string? State, decimal? Open) Payment(Trip t, IReadOnlyDictionary<long, decimal> open)
    {
        if (t.Status != Active) return (null, null);
        if (t.PartnerId is null) return (t.SettlementId is null ? "Unsettled" : "Settled", null);
        var left = open.TryGetValue(t.Id, out var v) ? v : 0m;
        return (left <= 0 ? "PartnerPaid" : left < t.PartnerCost ? "PartnerPartlyPaid" : "PartnerUnpaid", left);
    }

    internal TripResponse ToResponse(Row r, IReadOnlyDictionary<long, decimal> open)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(open);
        var t = r.T;
        var (payment, partnerOpen) = Payment(t, open);
        return new TripResponse(support.Token<Trip>(t.PublicId), t.ReceiptNo, t.TripDate, t.LoadingDate, t.UnloadingDate,
            new Ref(support.Token<Driver>(r.DriverPid), r.DriverName), t.TruckPlateAtSave, new Ref(support.Token<Customer>(r.CustomerPid), r.CustomerName),
            new Ref(support.Token<LoadingSite>(r.LoadingPid), r.LoadingName), new Ref(support.Token<UnloadingSite>(r.UnloadingPid), r.UnloadingName),
            new Ref(support.Token<Material>(r.MaterialPid), r.MaterialName), r.PartnerPid is Guid p ? new Ref(support.Token<Partner>(p), r.PartnerName!) : null, t.FleetClass,
            t.LoadedWeight, t.UnloadedWeight, t.BillingQty, t.WageQty, t.CustomerPrice, t.TariffPrice,
            t.TariffPrice is decimal tp ? Money.Round((t.CustomerPrice - tp) * t.BillingQty) : null,
            t.DriverRate, t.RateSource, t.RateMonth, t.PartnerRate, t.Revenue, t.DriverWage, t.PartnerCost, t.Revenue - t.DriverWage - t.PartnerCost,
            t.ShortageWeight, t.ShortagePct, t.IsShortageFlagged, t.IsTariffBreach, t.IsManualRate, t.NeedsReview, t.ReviewNote,
            t.Status, t.InvoiceId is null ? "Unbilled" : "Invoiced", payment is "Settled" or "PartnerPaid", t.Status == Active && t.InvoiceId is null && t.SettlementId is null,
            r.InvoicePid is Guid i ? new DocRef(support.Token<Invoice>(i), r.InvoiceNumber) : null,
            r.SettlementPid is Guid s ? new DocRef(support.Token<Settlement>(s), r.SettlementNumber) : null,
            r.EntryPid is Guid e ? new DocRef(support.Token<JournalEntry>(e), r.EntryNumber) : null, r.Pending,
            t.VoidReason, t.Notes, CustomFieldService.Read(t.CustomValues), DateTime.SpecifyKind(t.CreatedAt, DateTimeKind.Utc), Convert.ToBase64String(t.RowVersion),
            payment, partnerOpen);
    }
}
