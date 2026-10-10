using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>
/// Monthly loading-site driver rates (FR-027 … FR-037, PRD 7.2.2, flow 9.2): per month (YYYY-MM) and loading site an own-fleet and
/// an external-fleet rate per ton (at least one; each &gt; 0). The month strip shows no rates / partial / complete; a month lists every
/// active loading site for entry; a new month never inherits the previous month's rates (FR-029). Saving a month that has trips warns
/// that only new trips use the new rates (FR-034); clearing both rates of a site that has trips in the month is refused (FR-035);
/// every change is written to trn.TRN_RateHistory (FR-036). Rates are linked to the site by id, so renaming a site keeps them.
/// </summary>
public sealed class MonthlyRateService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, IRecordLockGuard locks, RateHistoryLog history,
    TariffService tariffs)
{
    public const string EntityType = "TRN.MonthlySiteRate";

    /// <summary>At most this many months in one month-strip request.</summary>
    public const int MaxStripMonths = 36;

    private const string TripVoid = "Void";

    private bool En => ctx.Language == "en";

    // ------------------------------------------------------------ month strip (FR-028)

    /// <summary>Months <paramref name="from"/> … <paramref name="to"/> (default: the 11 months before the current month up to the next month).</summary>
    public async Task<IReadOnlyList<MonthStatusResponse>> MonthsAsync(string? from, string? to, CancellationToken ct)
    {
        var today = await support.TodayAsync(ct).ConfigureAwait(false);
        var first = from is null ? today.AddMonths(-11) : PricingRules.Range(MonthOrFail(from, "from")).First;
        var last = to is null ? today.AddMonths(1) : PricingRules.Range(MonthOrFail(to, "to")).First;
        first = new DateOnly(first.Year, first.Month, 1);
        last = new DateOnly(last.Year, last.Month, 1);
        var count = (last.Year - first.Year) * 12 + last.Month - first.Month + 1;
        if (count < 1 || count > MaxStripMonths) throw Fail.Field("to", ErrorCodes.ValidationRange, "to");
        var months = Enumerable.Range(0, count).Select(i => PricingRules.Month(first.AddMonths(i))).ToList();

        var activeSites = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active).Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);
        var rated = await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => months.Contains(r.RateMonth) && activeSites.Contains(r.LoadingSiteId))
            .GroupBy(r => r.RateMonth).Select(g => new { Month = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Month, x => x.Count, ct).ConfigureAwait(false);
        var tripMonths = await db.Set<Trip>().AsNoTracking().Where(t => months.Contains(t.RateMonth) && t.Status != TripVoid && t.PartnerId == null)
            .Select(t => t.RateMonth).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return months.Select(m =>
        {
            var n = rated.GetValueOrDefault(m);
            return new MonthStatusResponse(m, PricingRules.MonthStatus(activeSites.Count, n), activeSites.Count, n, tripMonths.Contains(m));
        }).ToList();
    }

    // ------------------------------------------------------------ one month (FR-027, FR-028, FR-029)

    public async Task<MonthRatesResponse> GetAsync(string month, CancellationToken ct) => await LoadAsync(MonthOrFail(month, "month"), ct).ConfigureAwait(false);

    private async Task<MonthRatesResponse> LoadAsync(string month, CancellationToken ct)
    {
        var rates = await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => r.RateMonth == month).ToListAsync(ct).ConfigureAwait(false);
        var ratedIds = rates.Select(r => r.LoadingSiteId).ToList();
        // Every active site, plus archived sites that still have rates in the month.
        var sites = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active || ratedIds.Contains(s.Id))
            .OrderBy(s => s.Name).ToListAsync(ct).ConfigureAwait(false);
        var trips = await TripCountsAsync(month, ct).ConfigureAwait(false);
        var byId = rates.ToDictionary(r => r.LoadingSiteId);
        var list = sites.Select(s =>
        {
            byId.TryGetValue(s.Id, out var r);
            return new SiteRateResponse(new Ref(support.Token<LoadingSite>(s.PublicId), s.Name), s.Area, s.Status, r?.OwnFleetRate, r?.ExternalFleetRate,
                trips.GetValueOrDefault(s.Id), r is null ? null : support.Token<MonthlySiteRate>(r.PublicId), r is null ? null : Convert.ToBase64String(r.RowVersion));
        }).ToList();
        var active = sites.Count(s => s.Status == MasterCodes.Active);
        var ratedActive = sites.Count(s => s.Status == MasterCodes.Active && byId.ContainsKey(s.Id));
        return new MonthRatesResponse(month, PricingRules.MonthStatus(active, ratedActive), trips.Count > 0, active, ratedActive, list);
    }

    /// <summary>Non-void trips per loading site whose rate month is <paramref name="month"/> (FR-034, FR-035).</summary>
    private async Task<Dictionary<long, int>> TripCountsAsync(string month, CancellationToken ct) =>
        await db.Set<Trip>().AsNoTracking().Where(t => t.RateMonth == month && t.Status != TripVoid && t.PartnerId == null)
            .GroupBy(t => t.LoadingSiteId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct).ConfigureAwait(false);

    /// <summary>
    /// Saves the sites sent for a month in one transaction. A site with both rates empty is cleared (soft delete with reason),
    /// refused while it has trips in the month (FR-035). Unchanged sites are skipped. Each change writes a history row (FR-036).
    /// </summary>
    public async Task<MonthRatesSaveResult> SaveAsync(string month, MonthRatesRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var m = MonthOrFail(month, "month");
        var items = r.Sites ?? [];
        if (items.Count == 0) throw Fail.Field("sites", ErrorCodes.ValidationRequired, "sites");
        if (items.Count > 2000) throw Fail.Field("sites", ErrorCodes.ValidationRange, "sites");
        var decimals = await tariffs.DecimalsAsync(ct).ConfigureAwait(false);

        // Validate every row first so the client gets all field problems at once (sites[i].ownFleetRate …).
        var v = new Validator();
        for (var i = 0; i < items.Count; i++)
        {
            var s = items[i];
            v.Check(!string.IsNullOrWhiteSpace(s.LoadingSiteId), $"sites[{i}].loadingSiteId", ErrorCodes.ValidationRequired)
             .Check(s.OwnFleetRate is null || s.OwnFleetRate > 0 && Money.IsValidAmount(s.OwnFleetRate.Value, decimals), $"sites[{i}].ownFleetRate", ErrorCodes.ValidationRange)
             .Check(s.ExternalFleetRate is null || s.ExternalFleetRate > 0 && Money.IsValidAmount(s.ExternalFleetRate.Value, decimals), $"sites[{i}].externalFleetRate", ErrorCodes.ValidationRange);
        }
        v.ThrowIfInvalid();

        var publicIds = items.Select(s => support.Decode<LoadingSite>(s.LoadingSiteId)).ToList();
        var wanted = publicIds.Where(g => g is not null).Select(g => g!.Value).Distinct().ToList();
        var sites = await db.Set<LoadingSite>().AsNoTracking().Where(x => wanted.Contains(x.PublicId)).ToDictionaryAsync(x => x.PublicId, ct).ConfigureAwait(false);
        var trips = await TripCountsAsync(m, ct).ConfigureAwait(false);
        var existing = await db.Set<MonthlySiteRate>().Where(x => x.RateMonth == m).ToListAsync(ct).ConfigureAwait(false);
        var seen = new HashSet<long>();
        var work = new List<(MonthlySiteRate Rate, LoadingSite Site, string Action, Dictionary<string, object?>? Before, Dictionary<string, object?> After)>();
        for (var i = 0; i < items.Count; i++)
        {
            var s = items[i];
            var field = $"sites[{i}].loadingSiteId";
            if (publicIds[i] is not Guid publicId || !sites.TryGetValue(publicId, out var site)) throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
            if (!seen.Add(site.Id)) throw Fail.Field(field, ErrorCodes.ImportDuplicate, site.Name);
            var rate = existing.FirstOrDefault(x => x.LoadingSiteId == site.Id);
            var clearing = s.OwnFleetRate is null && s.ExternalFleetRate is null;
            if (rate is null)
            {
                if (clearing) continue;
                // An archived site keeps the rates it has but gets no new ones (FR-162: archived records are not chosen on new entries).
                if (site.Status != MasterCodes.Active) throw Fail.RuleOn(field, ErrorCodes.RecordArchived, site.Name);
                rate = new MonthlySiteRate { RateMonth = m, LoadingSiteId = site.Id, OwnFleetRate = s.OwnFleetRate, ExternalFleetRate = s.ExternalFleetRate };
                db.Set<MonthlySiteRate>().Add(rate);
                work.Add((rate, site, "Create", null, Snapshot(m, site.Name, rate.OwnFleetRate, rate.ExternalFleetRate)));
                continue;
            }
            if (rate.OwnFleetRate == s.OwnFleetRate && rate.ExternalFleetRate == s.ExternalFleetRate) continue;
            await locks.EnsureNotLockedByOthersAsync(EntityType, rate.Id, ct).ConfigureAwait(false);
            // Several managers may edit the same month grid: changing stored rates needs the version last seen (review M1).
            if (string.IsNullOrEmpty(s.RowVersion)) throw Fail.Field($"sites[{i}].rowVersion", ErrorCodes.ValidationRequired, "rowVersion");
            db.ExpectRowVersion(rate, s.RowVersion);
            var before = Snapshot(m, site.Name, rate.OwnFleetRate, rate.ExternalFleetRate);
            if (clearing)
            {
                if (trips.GetValueOrDefault(site.Id) is var n and > 0) throw Fail.RuleOn(field, ErrorCodes.RatesInUse, site.Name, m, n);
                rate.DeleteReason = En ? "Rates cleared" : "تم مسح الأسعار";
                db.Set<MonthlySiteRate>().Remove(rate);
                work.Add((rate, site, "Clear", before, Snapshot(m, site.Name, null, null)));
            }
            else
            {
                rate.OwnFleetRate = s.OwnFleetRate;
                rate.ExternalFleetRate = s.ExternalFleetRate;
                work.Add((rate, site, "Update", before, Snapshot(m, site.Name, rate.OwnFleetRate, rate.ExternalFleetRate)));
            }
        }

        if (work.Count > 0)
        {
            await db.InTransactionAsync(async c =>
            {
                await db.SaveChangesAsync(c).ConfigureAwait(false);
                // FR-035 again after the cleared rows are written (their row locks are held until commit): a trip saved meanwhile
                // is seen here and the clearing is rolled back. Trip save (B5) reads the rate row with an update lock (review M2).
                var cleared = work.Where(w => w.Action == "Clear").Select(w => w.Site.Id).ToList();
                if (cleared.Count > 0)
                {
                    var used = await db.Set<Trip>().Where(t => t.RateMonth == m && t.Status != TripVoid && t.PartnerId == null && cleared.Contains(t.LoadingSiteId))
                        .GroupBy(t => t.LoadingSiteId).Select(g => new { g.Key, Count = g.Count() }).FirstOrDefaultAsync(c).ConfigureAwait(false);
                    if (used is not null) throw Fail.Rule(ErrorCodes.RatesInUse, work.First(w => w.Site.Id == used.Key).Site.Name, m, used.Count);
                }
                foreach (var w in work) history.Add(RateHistoryLog.MonthlySiteRate, w.Rate.Id, w.Action, w.Before, w.After, w.Site.Name);
                await db.SaveChangesAsync(c).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        var warnings = new List<string>();
        if (work.Count > 0 && trips.Count > 0) warnings.Add(ErrorCatalog.Format(ErrorCodes.RatesMonthHasTrips, ctx.Language, m));
        return new MonthRatesSaveResult(await LoadAsync(m, ct).ConfigureAwait(false), work.Count, warnings);
    }

    private static Dictionary<string, object?> Snapshot(string month, string site, decimal? own, decimal? external) => new(StringComparer.Ordinal)
    {
        ["RateMonth"] = month,
        ["LoadingSite"] = site,
        ["OwnFleetRate"] = RateHistoryLog.Value(own),
        ["ExternalFleetRate"] = RateHistoryLog.Value(external),
    };

    // ------------------------------------------------------------ history (FR-036), missing rates (FR-037)

    /// <summary>History of a month's rates, or of one site's rates in the month when <paramref name="loadingSite"/> is given.</summary>
    public async Task<IReadOnlyList<RateHistoryEntry>> HistoryAsync(string month, string? loadingSite, CancellationToken ct)
    {
        var m = MonthOrFail(month, "month");
        // Soft-deleted (cleared) rows keep their history: the soft-delete filter is lifted, the company filter applied explicitly.
        var companyId = ctx.CompanyId;
        var query = db.Set<MonthlySiteRate>().IgnoreQueryFilters().AsNoTracking().Where(r => r.CompanyId == companyId && r.RateMonth == m);
        if (!string.IsNullOrWhiteSpace(loadingSite))
        {
            var site = await support.FindAsync<LoadingSite>(loadingSite, ct).ConfigureAwait(false);
            query = query.Where(r => r.LoadingSiteId == site.Id);
        }
        var ids = await query.Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        return await history.ReadAsync(RateHistoryLog.MonthlySiteRate, ids, ct).ConfigureAwait(false);
    }

    /// <summary>Active loading sites without any rate in <paramref name="month"/> (default: the current month in the company time zone).</summary>
    public async Task<MissingRatesResponse> MissingAsync(string? month, CancellationToken ct)
    {
        var m = month is null ? PricingRules.Month(await support.TodayAsync(ct).ConfigureAwait(false)) : MonthOrFail(month, "month");
        var rated = db.Set<MonthlySiteRate>().Where(r => r.RateMonth == m).Select(r => r.LoadingSiteId);
        var active = await db.Set<LoadingSite>().AsNoTracking().CountAsync(s => s.Status == MasterCodes.Active, ct).ConfigureAwait(false);
        var missing = await db.Set<LoadingSite>().AsNoTracking().Where(s => s.Status == MasterCodes.Active && !rated.Contains(s.Id))
            .OrderBy(s => s.Name).Select(s => new { s.PublicId, s.Name }).ToListAsync(ct).ConfigureAwait(false);
        return new MissingRatesResponse(m, active, missing.Select(s => new Ref(support.Token<LoadingSite>(s.PublicId), s.Name)).ToList());
    }

    /// <summary>The rates of a month for export/print (FR-028): every listed site with its two rates.</summary>
    internal Task<MonthRatesResponse> ForExportAsync(string month, CancellationToken ct) => LoadAsync(MonthOrFail(month, "month"), ct);

    private static string MonthOrFail(string? month, string field) => PricingRules.ParseMonth(month) ?? throw Fail.Field(field, ErrorCodes.ValidationFormat, field);
}
