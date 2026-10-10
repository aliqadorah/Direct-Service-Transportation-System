using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Accounting;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>
/// Tariff import and export (FR-019) and the monthly-rate sheet (FR-028 "save and print": Excel/CSV now, PDF with the print engine
/// in B6, DT-45). Tariff imports follow the master-data rules (DT-44): columns matched by header (Arabic, English, field key) or an
/// explicit mapping; party, material and sites by name among active records, empty or "All"/"الكل" = All; every row validated by
/// the form's rules; duplicates (same keys and start date) within the file and against the company refused; strict and add-only;
/// one transaction with a history row per line and a cfg.ImportBatches row.
/// </summary>
public sealed class PricingExchangeService(IAppDbContext db, ISpreadsheetService sheets, IRequestContext ctx, IUrlTokenService tokens,
    TariffService tariffs, MonthlyRateService rates, RateHistoryLog history)
{
    private sealed record Column(string Key, string Ar, string En, params string[] Aliases);

    private static readonly Column[] TariffColumns =
    [
        new("partyType", "نوع الطرف", "Party type", "النوع", "Type"),
        new("party", "الطرف", "Party", "العميل / الشريك", "Customer / partner", "العميل", "الشريك", "Customer", "Partner"),
        new("material", "المادة", "Material"),
        new("loadingSite", "موقع التحميل", "Loading site"),
        new("unloadingSite", "موقع التنزيل", "Unloading site", "موقع التفريغ"),
        new("price", "السعر للطن", "Price per ton", "السعر", "Price"),
        new("validFrom", "ساري من", "Valid from", "من تاريخ"),
        new("validTo", "ساري حتى", "Valid to", "إلى تاريخ"),
        new("notes", "ملاحظات", "Notes"),
        new("status", "الحالة", "Status"),
    ];

    private bool En => ctx.Language == "en";
    private string AllText => En ? "All" : "الكل";

    // ============================================================ export

    public async Task<GeneratedFile> ExportTariffsAsync(TariffListQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var csv = Csv(format);
        var headers = TariffColumns.Select(c => En ? c.En : c.Ar).ToList();
        var rows = (await tariffs.AllAsync(q, ct).ConfigureAwait(false)).Select(r => (IReadOnlyList<object?>)
        [
            PartyTypeText(r.T.PartyType), r.PartyName ?? AllText, r.MaterialName ?? AllText, r.LoadingName ?? AllText, r.UnloadingName ?? AllText,
            r.T.Price, r.T.ValidFrom, r.T.ValidTo, r.T.Notes, r.T.Status,
        ]).ToList();
        return File("tariffs", csv, headers, rows);
    }

    public async Task<GeneratedFile> ExportMonthAsync(string month, string? format, CancellationToken ct)
    {
        var csv = Csv(format);
        var m = await rates.ForExportAsync(month, ct).ConfigureAwait(false);
        var headers = En
            ? new List<string> { "Loading site", "Area", "Own-fleet rate", "External-fleet rate", "Trips" }
            : ["موقع التحميل", "المنطقة", "سعر الأسطول الخاص", "سعر الأسطول الخارجي", "الرحلات"];
        var rows = m.Sites.Select(s => (IReadOnlyList<object?>)[s.LoadingSite.Name, s.Area, s.OwnFleetRate, s.ExternalFleetRate, s.TripCount]).ToList();
        return File("monthly-rates-" + m.Month, csv, headers, rows);
    }

    private static bool Csv(string? format) => (format?.Trim().ToLowerInvariant()) switch
    {
        null or "" or "xlsx" => false,
        "csv" => true,
        _ => throw Fail.Field("format", ErrorCodes.ValidationFormat, "format"),
    };

    private GeneratedFile File(string name, bool csv, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<object?>> rows) => csv
        ? new GeneratedFile(name + ".csv", "text/csv", sheets.WriteCsv(headers, rows))
        : new GeneratedFile(name + ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", sheets.WriteXlsx(name.Length <= 31 ? name : name[..31], headers, rows));

    private string PartyTypeText(string partyType) => (En, partyType) switch
    {
        (true, _) => partyType,
        (false, PartyTypes.Customer) => "عميل",
        (false, _) => "شريك",
    };

    // ============================================================ import

    private sealed record Prepared(int Row, Tariff Tariff, IReadOnlyDictionary<string, object?> Snapshot);

    public async Task<MasterImportPreview> PreviewTariffsAsync(Stream content, string fileName, long length, string? mapping, CancellationToken ct) =>
        (await AnalyseAsync(content, fileName, length, mapping, ct).ConfigureAwait(false)).Preview;

    public async Task<ImportResult> ImportTariffsAsync(Stream content, string fileName, long length, string? mapping, CancellationToken ct)
    {
        var (prepared, preview) = await AnalyseAsync(content, fileName, length, mapping, ct).ConfigureAwait(false);
        if (preview.RejectedRows > 0) throw Fail.Rule(ErrorCodes.ImportHasErrors, preview.RejectedRows);
        var batch = await db.InTransactionAsync(async c =>
        {
            foreach (var p in prepared) db.Set<Tariff>().Add(p.Tariff);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            foreach (var p in prepared) history.Add(RateHistoryLog.Tariff, p.Tariff.Id, "Create", null, p.Snapshot);
            var b = ImportParse.Batch(TariffService.EntityType, fileName, prepared.Count, prepared.Count, ctx.UserId!.Value);
            db.Set<ImportBatch>().Add(b);
            await db.SaveChangesAsync(c).ConfigureAwait(false);
            return b;
        }, ct).ConfigureAwait(false);
        return new ImportResult(tokens.Encode<ImportBatch>(batch.PublicId), prepared.Count, prepared.Count);
    }

    private async Task<(List<Prepared> Rows, MasterImportPreview Preview)> AnalyseAsync(Stream content, string fileName, long length, string? mappingJson, CancellationToken ct)
    {
        var mapping = ImportMapping.Parse(mappingJson);
        var sheet = sheets.Read(content, fileName, length);
        var matched = new Dictionary<string, string>(StringComparer.Ordinal);
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in TariffColumns.Where(c => c.Key != "status"))
        {
            var i = mapping.TryGetValue(c.Key, out var header) ? sheet.Column(header) : sheet.Column([c.Key, c.Ar, c.En, .. c.Aliases]);
            if (i >= 0) matched[c.Key] = sheet.Headers[i];
            index[c.Key] = i;
        }
        ImportParse.RequireColumns(sheet, (index["partyType"], En ? "Party type" : "نوع الطرف"), (index["price"], En ? "Price per ton" : "السعر للطن"));

        var problems = new ImportProblems(ctx.Language);
        var prepared = new List<Prepared>();
        var keys = new List<(int, string?)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        _decimals = await tariffs.DecimalsAsync(ct).ConfigureAwait(false);
        _existing = await tariffs.ExistingKeysAsync(ct).ConfigureAwait(false);
        foreach (var row in sheet.Rows)
        {
            string? Cell(string key) => index.TryGetValue(key, out var i) ? TextNormalizer.CleanText(row[i]) : null;
            var rowKey = string.Join(" · ", new[] { Cell("party"), Cell("material"), Cell("loadingSite"), Cell("unloadingSite") }.Where(x => x is not null));
            keys.Add((row.RowNumber, rowKey.Length == 0 ? AllText : rowKey));
            try
            {
                var item = await PrepareAsync(row.RowNumber, rowKey, Cell, problems, ct).ConfigureAwait(false);
                if (item is null) continue;
                var t = item.Tariff;
                var unique = TariffService.UniqueKey(t.PartyType, t.CustomerId, t.PartnerId, t.MaterialId, t.LoadingSiteId, t.UnloadingSiteId, t.ValidFrom);
                if (_existing.Contains(unique)) problems.Add(row.RowNumber, rowKey, ErrorCodes.TariffDuplicate);
                else if (!seen.Add(unique)) problems.Add(row.RowNumber, rowKey, ErrorCodes.TariffDuplicateInFile);
                if (!problems.Has(row.RowNumber)) prepared.Add(item);
            }
            catch (AppException ex)
            {
                if (ex.Errors.Count > 0)
                {
                    foreach (var e in ex.Errors) problems.Add(row.RowNumber, rowKey, e.Code, e.Args);
                }
                else
                {
                    problems.Add(row.RowNumber, rowKey, ex.Code, ex.Args);
                }
            }
        }
        var p = problems.Preview(keys);
        var unused = sheet.Headers.Where(h => !string.IsNullOrWhiteSpace(h) && !matched.Values.Contains(h, StringComparer.Ordinal)).ToList();
        return (prepared, new MasterImportPreview(p.TotalRows, p.ValidRows, p.RejectedRows, p.Rows, new ImportColumns(matched, unused)));
    }

    private async Task<Prepared?> PrepareAsync(int row, string? rowKey, Func<string, string?> cell, ImportProblems problems, CancellationToken ct)
    {
        var ok = true;
        var partyTypeText = cell("partyType");
        string? partyType = null;
        try
        {
            partyType = TariffService.PartyTypeOrNull(partyTypeText, "partyType");
        }
        catch (AppException)
        {
            problems.Add(row, rowKey, ErrorCodes.ImportInvalid, En ? "party type" : "نوع الطرف", partyTypeText);
            return null;
        }
        decimal? price = null;
        if (cell("price") is { } priceText)
        {
            if (ImportParse.TryAmount(priceText, 4, out var v)) price = v;
            else { problems.Add(row, rowKey, ErrorCodes.ImportInvalid, En ? "price" : "السعر", priceText); ok = false; }
        }
        DateOnly? Date(string key, string label)
        {
            var text = cell(key);
            if (text is null) return null;
            if (ImportParse.TryDate(text, out var d)) return d;
            problems.Add(row, rowKey, ErrorCodes.ImportInvalid, label, text);
            ok = false;
            return null;
        }
        var validFrom = Date("validFrom", En ? "valid from" : "ساري من");
        var validTo = Date("validTo", En ? "valid to" : "ساري حتى");
        (long Id, string Name)? party = null;
        if (partyType == PartyTypes.Customer) party = await LinkAsync<Customer>(cell("party"), x => x.Name, row, rowKey, En ? "customer" : "العميل", problems, ct).ConfigureAwait(false);
        else if (partyType == PartyTypes.Partner) party = await LinkAsync<Partner>(cell("party"), x => x.Name, row, rowKey, En ? "partner" : "الشريك", problems, ct).ConfigureAwait(false);
        var material = await LinkAsync<Material>(cell("material"), x => x.Name, row, rowKey, En ? "material" : "المادة", problems, ct).ConfigureAwait(false);
        var loading = await LinkAsync<LoadingSite>(cell("loadingSite"), x => x.Name, row, rowKey, En ? "loading site" : "موقع التحميل", problems, ct).ConfigureAwait(false);
        var unloading = await LinkAsync<UnloadingSite>(cell("unloadingSite"), x => x.Name, row, rowKey, En ? "unloading site" : "موقع التنزيل", problems, ct).ConfigureAwait(false);
        if (party is { Id: 0 } || material is { Id: 0 } || loading is { Id: 0 } || unloading is { Id: 0 } || !ok) return null;
        var request = new TariffRequest(partyType, null, null, null, null, price, validFrom, validTo, cell("notes"), null, null);
        var (type, notes) = TariffService.Validate(request, _decimals);
        var keys = new TariffService.ResolvedKeys(type == PartyTypes.Customer ? party?.Id : null, type == PartyTypes.Partner ? party?.Id : null,
            material?.Id, loading?.Id, unloading?.Id, party?.Name, material?.Name, loading?.Name, unloading?.Name);
        var t = new Tariff();
        return new Prepared(row, t, TariffService.Set(t, type, keys, request, notes));
    }

    private int _decimals = 3;
    private HashSet<string> _existing = [];

    /// <summary>
    /// Id and name of a record named in a cell, matched by name among active records (case/space-insensitive). Null for an empty
    /// cell or "All"; Id 0 when unknown, archived or ambiguous (problem added).
    /// </summary>
    private async Task<(long Id, string Name)?> LinkAsync<T>(string? text, Func<T, string> name, int row, string? rowKey, string label, ImportProblems problems, CancellationToken ct)
        where T : AuditableEntity, IArchivable
    {
        if (string.IsNullOrWhiteSpace(text) || TextNormalizer.Key(text) is "ALL" or "الكل" or "*" or "الجميع") return null;
        var key = TextNormalizer.Key(MasterDataSupport.Name(text));
        var match = (await CandidatesAsync<T>(ct).ConfigureAwait(false)).Where(x => TextNormalizer.Key(name(x)) == key).ToList();
        if (match.Count == 1) return (match[0].Id, name(match[0]));
        problems.Add(row, rowKey, ErrorCodes.ImportUnknown, label, text);
        return (0, string.Empty);
    }

    private readonly Dictionary<Type, object> _candidates = [];

    private async Task<IReadOnlyList<T>> CandidatesAsync<T>(CancellationToken ct) where T : AuditableEntity, IArchivable
    {
        if (_candidates.TryGetValue(typeof(T), out var cached)) return (IReadOnlyList<T>)cached;
        var list = await db.Set<T>().AsNoTracking().Where(x => x.Status == MasterCodes.Active).ToListAsync(ct).ConfigureAwait(false);
        _candidates[typeof(T)] = list;
        return list;
    }
}
