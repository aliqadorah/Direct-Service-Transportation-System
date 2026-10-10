using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Pricing;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Trips;

/// <summary>
/// Trip import from .xlsx, CSV/TSV or pasted rows (FR-013, FR-033, US-02). Columns are matched by header (Arabic, English, field key,
/// the company's label) or by an explicit mapping. Every row goes through the same rules as the form (<see cref="TripService.ApplyAsync"/>):
/// prices from the tariffs, driver rate from the monthly site rate when there is no rate column, a row without a rate rejected, a
/// supplied rate different from the monthly one saved as Manual (masters.manage). Duplicate receipt numbers in the file and in the
/// company are rejected. Strict mode: any rejected row blocks the import. Tolerant mode (DT-72): invalid optional values take their
/// defaults and the row is flagged Needs review; rejected rows are skipped and reported. Each import is logged in cfg.ImportBatches.
/// </summary>
public sealed class TripImportService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    TripService trips, StandardFieldService fields, CustomFieldService customFields, IMonthlyRateLock rateLock, IChangeContext changes)
{
    private sealed record Column(string Key, params string[] Aliases);

    private static readonly Column[] Columns =
    [
        new("receiptNo", "رقم السند", "رقم الوصل", "رقم الإيصال", "السند", "Receipt", "Receipt number", "Receipt no"),
        new("tripDate", "التاريخ", "تاريخ الرحلة", "Date", "Trip date"),
        new("loadingDate", "تاريخ التحميل", "Loading date"),
        new("unloadingDate", "تاريخ التنزيل", "تاريخ التفريغ", "Unloading date"),
        new("driverId", "السائق", "اسم السائق", "الرقم الوطني", "اللوحة", "Driver", "Plate", "National ID"),
        new("customerId", "العميل", "Customer"),
        new("loadingSiteId", "موقع التحميل", "من", "Loading site", "From"),
        new("unloadingSiteId", "موقع التنزيل", "موقع التفريغ", "إلى", "Unloading site", "To"),
        new("materialId", "المادة", "Material"),
        new("loadedWeight", "الوزن المحمّل", "وزن التحميل", "محمّل", "Loaded weight", "Loaded"),
        new("unloadedWeight", "الوزن المنزّل", "وزن التفريغ", "منزّل", "Unloaded weight", "Unloaded"),
        new("customerPrice", "سعر العميل", "السعر", "Customer price", "Price"),
        new("partnerId", "الشريك", "Partner"),
        new("driverRate", "أجرة السائق", "الأجرة", "Driver rate", "Rate"),
        new("partnerRate", "سعر الشريك", "Partner rate"),
        new("notes", "ملاحظات", "Notes"),
    ];

    private sealed record Prepared(int Row, Trip Trip, bool Review, string? Reason);

    private bool En => ctx.Language == "en";

    public async Task<TripImportPreview> PreviewAsync(Stream content, string fileName, long length, string? mode, string? mapping, CancellationToken ct) =>
        (await AnalyseAsync(content, fileName, length, mode, mapping, locking: false, ct).ConfigureAwait(false)).Preview;

    public async Task<TripImportResult> ImportAsync(Stream content, string fileName, long length, string? mode, string? mapping, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return await db.InTransactionAsync(async c =>
        {
            buffer.Position = 0;
            // Analysed again inside the transaction with the monthly rates read WITH (UPDLOCK): the rates cannot be cleared meanwhile.
            var (prepared, preview) = await AnalyseAsync(buffer, fileName, length, mode, mapping, locking: true, c).ConfigureAwait(false);
            if (preview.Mode == "Strict" && preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
            if (prepared.Count == 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
            await trips.EnsureTripLimitAsync(prepared.Count, c).ConfigureAwait(false);
            var batch = ImportParse.Batch(TripService.EntityType, fileName, preview.TotalRows, prepared.Count, ctx.UserId!.Value, preview.Mode);
            db.Set<ImportBatch>().Add(batch);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            var s = await trips.SettingsAsync(c).ConfigureAwait(false);
            foreach (var p in prepared)
            {
                p.Trip.ImportBatchId = batch.Id;
                db.Set<Trip>().Add(p.Trip);
                // The batch save reset the change context: the manual-rate / tariff-deviation reasons are set again (review H1).
                if (p.Reason is not null) changes.SetAction(p.Trip, "Create", p.Reason);
            }
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            foreach (var p in prepared) await trips.PostAsync(p.Trip, s, c).ConfigureAwait(false);
            return new TripImportResult(tokens.Encode<ImportBatch>(batch.PublicId), preview.TotalRows, prepared.Count, preview.TotalRows - prepared.Count, prepared.Count(p => p.Review));
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Preloaded prices for the whole file: active tariff lines and the monthly rates of the file's months.</summary>
    private sealed class CachedPrices(IReadOnlyList<(string PartyType, TariffLine Line)> tariffs, IReadOnlyDictionary<(long, string), (decimal? Own, decimal? Ext)> rates) : ITripPriceSource
    {
        public Task<TariffLine?> TariffAsync(string partyType, TariffKeys keys, DateOnly date, CancellationToken ct) =>
            Task.FromResult(PricingRules.Pick(tariffs.Where(t => t.PartyType == partyType).Select(t => t.Line), keys, date));

        public Task<decimal?> MonthlyRateAsync(long loadingSiteId, string rateMonth, string fleetClass, CancellationToken ct) =>
            Task.FromResult(rates.TryGetValue((loadingSiteId, rateMonth), out var r) ? PricingRules.RateFor(fleetClass, r.Own, r.Ext) : null);
    }

    private async Task<CachedPrices> PricesAsync(IReadOnlyCollection<string> months, bool locking, CancellationToken ct)
    {
        var tariffs = (await db.Set<Tariff>().AsNoTracking().Where(t => t.Status == MasterCodes.Active)
                .Select(t => new { t.Id, t.PublicId, t.PartyType, t.CustomerId, t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId, t.Price, t.ValidFrom, t.ValidTo })
                .ToListAsync(ct).ConfigureAwait(false))
            .Select(t => (t.PartyType, new TariffLine(t.Id, new TariffKeys(t.CustomerId ?? t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId), t.Price, t.ValidFrom, t.ValidTo, t.PublicId)))
            .ToList();
        var list = months.ToList();
        var rates = locking
            ? await rateLock.ReadAsync(list, null, ct).ConfigureAwait(false)
            : (await db.Set<MonthlySiteRate>().AsNoTracking().Where(r => list.Contains(r.RateMonth))
                .Select(r => new { r.LoadingSiteId, r.RateMonth, r.OwnFleetRate, r.ExternalFleetRate }).ToListAsync(ct).ConfigureAwait(false))
                .Select(r => new LockedMonthlyRate(r.LoadingSiteId, r.RateMonth, r.OwnFleetRate, r.ExternalFleetRate)).ToList();
        return new CachedPrices(tariffs, rates.ToDictionary(r => (r.LoadingSiteId, r.RateMonth), r => (r.OwnFleetRate, r.ExternalFleetRate)));
    }

    private async Task<(List<Prepared> Rows, TripImportPreview Preview)> AnalyseAsync(Stream content, string fileName, long length, string? modeText, string? mappingJson,
        bool locking, CancellationToken ct)
    {
        var mode = (modeText?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "strict" => "Strict",
            "tolerant" => "Tolerant",
            _ => throw Fail.Field("mode", ErrorCodes.ValidationFormat, "mode"),
        };
        var tolerant = mode == "Tolerant";
        var mapping = ImportMapping.Parse(mappingJson);
        var sheet = sheets.Read(content, fileName, length);
        var rules = await fields.RulesAsync(FieldCatalog.Trip, ct).ConfigureAwait(false);
        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in Columns)
        {
            var i = mapping.TryGetValue(c.Key, out var header) ? sheet.Column(header) : sheet.Column([c.Key, rules.Label(c.Key), .. c.Aliases]);
            if (i >= 0) matched[c.Key] = sheet.Headers[i];
            index[c.Key] = i;
        }
        var defs = (await customFields.DefinitionsAsync(FieldCatalog.Trip, ct).ConfigureAwait(false)).Where(d => !d.IsHidden).ToList();
        var customIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var d in defs)
        {
            var i = mapping.TryGetValue("custom." + d.FieldKey, out var h) ? sheet.Column(h) : sheet.Column(new[] { d.FieldKey, d.LabelAr, d.LabelEn }.Where(a => a is not null).Select(a => a!).ToArray());
            if (i >= 0) matched["custom." + d.FieldKey] = sheet.Headers[i];
            customIndex[d.FieldKey] = i;
        }
        ImportParse.RequireColumns(sheet, (index["receiptNo"], rules.Label("receiptNo")), (index["tripDate"], rules.Label("tripDate")),
            (index["driverId"], rules.Label("driverId")), (index["customerId"], rules.Label("customerId")), (index["loadingSiteId"], rules.Label("loadingSiteId")),
            (index["unloadingSiteId"], rules.Label("unloadingSiteId")), (index["materialId"], rules.Label("materialId")), (index["loadedWeight"], rules.Label("loadedWeight")));

        // Lookups among active records (the trip form selects only active master data, FR-167).
        var drivers = await db.Set<Driver>().AsNoTracking().Include(d => d.OwnershipType).Where(d => d.Status == MasterCodes.Active).ToListAsync(ct).ConfigureAwait(false);
        var customers = await Active<Customer>(c => c.Name, ct).ConfigureAwait(false);
        var loadingSites = await Active<LoadingSite>(s => s.Name, ct).ConfigureAwait(false);
        var unloadingSites = await Active<UnloadingSite>(s => s.Name, ct).ConfigureAwait(false);
        var materials = await Active<Material>(m => m.Name, ct).ConfigureAwait(false);
        var partners = await Active<Partner>(p => p.Name, ct).ConfigureAwait(false);
        var partnerById = partners.Values.SelectMany(x => x).ToDictionary(p => p.Id);

        string? Cell(SheetRow row, string key) => index.TryGetValue(key, out var i) && i >= 0 ? TextNormalizer.CleanText(row[i]) : null;
        // Months of both dates (an invalid loading date falls back to the trip date in tolerant mode, review L1).
        var months = sheet.Rows.SelectMany(r => new[] { Cell(r, "loadingDate"), Cell(r, "tripDate") }).Select(t => ImportParse.TryDate(t, out var d) ? PricingRules.Month(d) : null)
            .Where(m => m is not null).Select(m => m!).Distinct().ToList();
        var bulk = new TripBulkOptions();
        var prices = await PricesAsync(months, locking, ct).ConfigureAwait(false);
        var s = await trips.SettingsAsync(ct).ConfigureAwait(false);

        var problems = new ImportProblems(ctx.Language);
        var prepared = new List<Prepared>();
        var keys = new List<(int, string?)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        var fileKeys = sheet.Rows.Select(r => TextNormalizer.Key(Cell(r, "receiptNo"))).Where(k => k.Length > 0).Distinct().ToList();
        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in fileKeys.Chunk(1000))
        {
            var part = chunk.ToList();
            existing.UnionWith(await db.Set<Trip>().AsNoTracking().Where(t => part.Contains(t.ReceiptNoKey)).Select(t => t.ReceiptNoKey).ToListAsync(ct).ConfigureAwait(false));
        }

        foreach (var row in sheet.Rows)
        {
            var receipt = Cell(row, "receiptNo");
            keys.Add((row.RowNumber, receipt));
            var review = new List<string>();
            var ok = true;
            void Problem(string code, params object?[] args) { problems.Add(row.RowNumber, receipt, code, args); ok = false; }

            var key = TextNormalizer.Key(receipt);
            if (key.Length > 0 && (existing.Contains(key) || !seen.Add(key)))
            {
                duplicates++;
                Problem(ErrorCodes.ImportDuplicate, receipt);
                continue;
            }
            DateOnly? Date(string field, bool optional)
            {
                var text = Cell(row, field);
                if (text is null) return null;
                if (ImportParse.TryDate(text, out var d)) return d;
                if (optional && tolerant) { review.Add(rules.Label(field)); return null; }
                Problem(ErrorCodes.ImportInvalid, rules.Label(field), text);
                return null;
            }
            decimal? Number(string field, bool optional)
            {
                var text = Cell(row, field);
                if (text is null) return null;
                if (ImportParse.TryAmount(text, 4, out var v)) return v;
                if (optional && tolerant) { review.Add(rules.Label(field)); return null; }
                Problem(ErrorCodes.ImportInvalid, rules.Label(field), text);
                return null;
            }
            T? Find<T>(Dictionary<string, List<T>> map, string field) where T : class
            {
                var text = Cell(row, field);
                if (text is null) return null;
                if (map.TryGetValue(TextNormalizer.Key(MasterDataSupport.Name(text)), out var found) && found.Count == 1) return found[0];
                Problem(ErrorCodes.ImportUnknown, rules.Label(field), text);
                return null;
            }

            var tripDate = Date("tripDate", false);
            var loadingDate = Date("loadingDate", true);
            var unloadingDate = Date("unloadingDate", true);
            var loaded = Number("loadedWeight", false);
            var unloaded = Number("unloadedWeight", true);
            var customerPrice = Number("customerPrice", false);
            var driverRate = Number("driverRate", false);
            var partnerRate = Number("partnerRate", false);
            var driverText = Cell(row, "driverId");
            var driver = driverText is null ? null : FindDriver(drivers, driverText);
            if (driverText is not null && driver is null) Problem(ErrorCodes.ImportUnknown, rules.Label("driverId"), driverText);
            var customer = Find(customers, "customerId");
            var loading = Find(loadingSites, "loadingSiteId");
            var unloading = Find(unloadingSites, "unloadingSiteId");
            var material = Find(materials, "materialId");
            // Partner: the cell when given; otherwise the driver's partner, as the form pre-fills it (7.1.1, DT-71).
            Partner? partner = null;
            if (Cell(row, "partnerId") is { } partnerText)
            {
                if (partners.TryGetValue(TextNormalizer.Key(MasterDataSupport.Name(partnerText)), out var p) && p.Count == 1) partner = p[0];
                else if (tolerant)
                {
                    // Default = the driver's partner, as the form pre-fills it (review M3).
                    review.Add(rules.Label("partnerId"));
                    if (driver?.PartnerId is long fallback && partnerById.TryGetValue(fallback, out var fp)) partner = fp;
                }
                else Problem(ErrorCodes.ImportUnknown, rules.Label("partnerId"), partnerText);
            }
            else if (driver?.PartnerId is long dp && partnerById.TryGetValue(dp, out var dpp))
            {
                partner = dpp;
            }
            var custom = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var d in defs)
            {
                var text = customIndex[d.FieldKey] >= 0 ? row[customIndex[d.FieldKey]] : null;
                if (text is null) continue;
                if (CustomFieldService.TryParseCell(d, text, out var v)) { if (v is JsonElement value) custom[d.FieldKey] = value; }
                else if (tolerant) review.Add(customFields.Label(d));
                else Problem(ErrorCodes.ImportInvalid, customFields.Label(d), text);
            }
            // Required values missing (driver, customer, sites, material, dates, weight) are reported by the form rules below.
            if (!ok) continue;
            if (driver is null || customer is null || loading is null || unloading is null || material is null)
            {
                foreach (var (field, missing) in new[] { ("driverId", driver is null), ("customerId", customer is null), ("loadingSiteId", loading is null), ("unloadingSiteId", unloading is null), ("materialId", material is null) })
                    if (missing) Problem(ErrorCodes.ValidationRequired, rules.Label(field));
                continue;
            }
            try
            {
                var draft = new TripDraft(receipt, tripDate, loadingDate, unloadingDate, driver, driver.OwnershipType.FleetClass, customer, loading, unloading, material, partner,
                    loaded, unloaded, customerPrice, driverRate, partnerRate, Cell(row, "notes"), custom);
                var t = new Trip();
                var note = review.Count > 0 ? (En ? "Defaults applied on import: " : "قيم افتراضية عند الاستيراد: ") + string.Join(", ", review) : null;
                await trips.ApplyAsync(t, draft, isNew: true, s, prices, ct, note, bulk).ConfigureAwait(false);
                prepared.Add(new Prepared(row.RowNumber, t, note is not null, changes.GetReason(t)));
            }
            catch (AppException ex)
            {
                if (ex.Errors.Count > 0) foreach (var e in ex.Errors) problems.Add(row.RowNumber, receipt, e.Code, e.Args.Length > 0 && e.Args[0] is string f ? [rules.Label(f), .. e.Args.Skip(1)] : e.Args);
                else problems.Add(row.RowNumber, receipt, ex.Code, ex.Args);
            }
        }
        var p0 = problems.Preview(keys);
        var unused = sheet.Headers.Where(h => !string.IsNullOrWhiteSpace(h) && !matched.Values.Contains(h, StringComparer.Ordinal)).ToList();
        return (prepared, new TripImportPreview(p0.TotalRows, prepared.Count, p0.RejectedRows, duplicates, prepared.Count(p => p.Review), mode, p0.Rows, new ImportColumns(matched, unused)));
    }

    private async Task<Dictionary<string, List<T>>> Active<T>(Func<T, string> name, CancellationToken ct) where T : AuditableEntity, IArchivable =>
        (await db.Set<T>().AsNoTracking().Where(x => x.Status == MasterCodes.Active).ToListAsync(ct).ConfigureAwait(false))
        .GroupBy(x => TextNormalizer.Key(name(x))).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    /// <summary>A driver by national ID, plate or name (unique among active drivers).</summary>
    private static Driver? FindDriver(List<Driver> drivers, string text)
    {
        var digits = TextNormalizer.ToAsciiDigits(text).Replace(" ", string.Empty, StringComparison.Ordinal);
        var plate = MasterDataSupport.Plate(text);
        var name = TextNormalizer.Key(MasterDataSupport.Name(text));
        var byId = drivers.Where(d => d.NationalId == digits).ToList();
        if (byId.Count == 1) return byId[0];
        var byPlate = drivers.Where(d => d.PlateNumber == plate).ToList();
        if (byPlate.Count == 1) return byPlate[0];
        var byName = drivers.Where(d => TextNormalizer.Key(d.FullName) == name).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }
}
