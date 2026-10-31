using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B4 pricing through the API: tariffs (FR-019 … FR-026), monthly loading-site driver rates (FR-027 … FR-037), the trip-form price
/// quote (FR-006, FR-022, FR-030), rate history, import/export and permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PricingTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Master data of one company: two customers, a partner, two materials, two loading sites, an unloading site, an own and a partner driver.</summary>
    private sealed record World(SignedInUser Owner, CustomerResponse Customer, CustomerResponse Customer2, PartnerResponse Partner, MaterialResponse Material,
        MaterialResponse Material2, SiteResponse Loading, SiteResponse Loading2, SiteResponse Unloading, DriverResponse OwnDriver, DriverResponse PartnerDriver);

    private async Task<World> WorldAsync()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        CustomerRequest Customer(string name) => new(name, null, null, "0781234567", null, null, "Aqaba", null, 30, null, null, null, null, null);
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Customer("Arab Potash")));
        var customer2 = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), Customer("Jordan Phosphate")));
        var partner = await ReadAsync<PartnerResponse>(await c.PostAsJsonAsync(U(o, "/partners"),
            new PartnerRequest("Al-Noor Transport", null, null, null, null, null, null, null, null, null, null)));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Potash", null, null, null)));
        var material2 = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Phosphate", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Ghor Al-Safi", null, null, null)));
        var loading2 = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Eshidiya", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Aqaba Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var rented = await ReadAsync<OwnershipTypeResponse>(await c.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest("Rented", "External", null, null)));
        DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId) =>
            new(name, nid, "0791234567", null, null, "L-1", "C", Today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                Today.AddYears(1), Today.AddYears(1), Today.AddYears(1), null, null, null, null);
        var own = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Ahmad", "9871234560", "10-11111", types.Single(t => t.IsBuiltIn).Id, null)));
        var ext = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Sami", "9871234561", "10-22222", rented.Id, partner.Id)));
        return new World(o, customer, customer2, partner, material, material2, loading, loading2, unloading, own, ext);
    }

    private static TariffRequest Tariff(string partyType, string? party, string? material, string? loading, string? unloading, decimal price,
        DateOnly? from = null, DateOnly? to = null, string? rowVersion = null, string? clientId = null) =>
        new(partyType, party, material, loading, unloading, price, from, to, null, clientId, rowVersion);

    private static async Task<TariffResponse> AddAsync(SignedInUser o, TariffRequest r) => await ReadAsync<TariffResponse>(await o.Client.PostAsJsonAsync(U(o, "/tariffs"), r));

    private static async Task<PriceQuoteResponse> QuoteAsync(World w, DateOnly date, string? material = null, string? loading = null, string? customer = null,
        string? driver = null, string? partner = null)
    {
        var q = $"/pricing/quote?customer={customer ?? w.Customer.Id}&material={material ?? w.Material.Id}&loadingSite={loading ?? w.Loading.Id}" +
                $"&unloadingSite={w.Unloading.Id}&tripDate={date:yyyy-MM-dd}" + (driver is null ? "" : $"&driver={driver}") + (partner is null ? "" : $"&partner={partner}");
        return await ReadAsync<PriceQuoteResponse>(await w.Owner.Client.GetAsync(U(w.Owner, q)));
    }

    // ------------------------------------------------------------------ tariffs (FR-019 … FR-023, FR-026)

    [Fact]
    public async Task Most_specific_tariff_line_fills_the_price_and_ties_go_to_the_latest_start_FR021_FR022()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var all = await AddAsync(o, Tariff("Customer", null, null, null, null, 4.000m));
        Assert.Equal(0, all.Specificity);
        Assert.Null(all.Party);
        var party = await AddAsync(o, Tariff("Customer", w.Customer.Id, null, null, null, 5.000m));
        Assert.Equal(8, party.Specificity);
        var partyMaterial = await AddAsync(o, Tariff("Customer", w.Customer.Id, w.Material.Id, null, null, 5.500m));
        Assert.Equal(12, partyMaterial.Specificity);
        var full = await AddAsync(o, Tariff("Customer", w.Customer.Id, w.Material.Id, w.Loading.Id, w.Unloading.Id, 6.000m, Today.AddDays(-30), Today.AddDays(30)));
        Assert.Equal(15, full.Specificity);
        Assert.Equal("Arab Potash", full.Party!.Name);

        Assert.Equal(6.000m, (await QuoteAsync(w, Today)).CustomerPrice!.Price);
        Assert.Equal(full.Id, (await QuoteAsync(w, Today)).CustomerPrice!.TariffId);
        // Outside the full line's validity → party + material (12).
        Assert.Equal(5.500m, (await QuoteAsync(w, Today.AddDays(31))).CustomerPrice!.Price);
        // Another material → party (8); another customer → All (0).
        Assert.Equal(5.000m, (await QuoteAsync(w, Today, material: w.Material2.Id)).CustomerPrice!.Price);
        Assert.Equal(4.000m, (await QuoteAsync(w, Today, customer: w.Customer2.Id)).CustomerPrice!.Price);

        // Tie on specificity → latest Valid from.
        await AddAsync(o, Tariff("Customer", w.Customer.Id, w.Material.Id, null, null, 5.750m, Today.AddDays(-10)));
        Assert.Equal(5.750m, (await QuoteAsync(w, Today.AddDays(31))).CustomerPrice!.Price);
        Assert.Equal(5.500m, (await QuoteAsync(w, Today.AddDays(-31))).CustomerPrice!.Price);

        // Archived lines are not used; no match → the user enters the price (null).
        var current = await ReadAsync<TariffDetails>(await o.Client.GetAsync(U(o, $"/tariffs/{all.Id}")));
        await (await o.Client.PostAsJsonAsync(U(o, $"/tariffs/{all.Id}/archive"), new StatusChangeRequest(current.Record.RowVersion))).EnsureOkAsync();
        Assert.Null((await QuoteAsync(w, Today, customer: w.Customer2.Id)).CustomerPrice);

        // Lists: filters by key or "all", validity date, status.
        var list = await ReadAsync<PagedResult<TariffResponse>>(await o.Client.GetAsync(U(o, $"/tariffs?party={w.Customer.Id}&sort=-specificity")));
        Assert.Equal([15, 12, 12, 8], list.Items.Select(t => t.Specificity));
        Assert.Single((await ReadAsync<PagedResult<TariffResponse>>(await o.Client.GetAsync(U(o, "/tariffs?status=archived")))).Items);
        Assert.Equal(2, (await ReadAsync<PagedResult<TariffResponse>>(await o.Client.GetAsync(U(o, "/tariffs?material=all&status=all")))).TotalCount);
        Assert.Equal(3, (await ReadAsync<PagedResult<TariffResponse>>(await o.Client.GetAsync(U(o, $"/tariffs?validOn={Today.AddDays(31):yyyy-MM-dd}")))).TotalCount);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.GetAsync(U(o, "/tariffs?sort=nope"))).StatusCode);
    }

    [Fact]
    public async Task Tariff_validation_duplicates_and_history_FR019_FR026()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var bad = await o.Client.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Customer", null, null, null, null, 0m, Today, Today.AddDays(-1), null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var errors = (await bad.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("price", errors);
        Assert.Contains("validTo", errors);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Supplier", null, null, null, null, 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Customer", null, null, null, null, 1.0005m))).StatusCode);
        // A partner token as the party of a customer line is refused (tokens are typed).
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Customer", w.Partner.Id, null, null, null, 1m))).StatusCode);

        var line = await AddAsync(o, Tariff("Partner", w.Partner.Id, w.Material.Id, null, null, 3.250m, clientId: "6f3b1c9e-2f1a-4d7b-9a51-0c2e8f0b1a11"));
        Assert.Equal("Partner", line.PartyType);
        // A repeated create with the same clientId returns the same line (API-05).
        var again = await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Partner", w.Partner.Id, w.Material.Id, null, null, 3.250m, clientId: "6f3b1c9e-2f1a-4d7b-9a51-0c2e8f0b1a11"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(line.Id, (await again.Content.ReadFromJsonAsync<TariffResponse>())!.Id);

        // Same keys and start date → duplicate, archived lines included.
        var dup = await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Partner", w.Partner.Id, w.Material.Id, null, null, 9m));
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("tariff.duplicate", await dup.ProblemCodeAsync());
        var archived = await ReadAsync<TariffResponse>(await o.Client.PostAsJsonAsync(U(o, $"/tariffs/{line.Id}/archive"), new StatusChangeRequest(line.RowVersion)));
        Assert.Equal("Archived", archived.Status);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Partner", w.Partner.Id, w.Material.Id, null, null, 9m))).StatusCode);
        var restored = await ReadAsync<TariffResponse>(await o.Client.PostAsJsonAsync(U(o, $"/tariffs/{line.Id}/restore"), new StatusChangeRequest(archived.RowVersion)));

        var edited = await ReadAsync<TariffResponse>(await o.Client.PutAsJsonAsync(U(o, $"/tariffs/{line.Id}"),
            Tariff("Partner", w.Partner.Id, w.Material.Id, w.Loading.Id, null, 3.500m, Today, rowVersion: restored.RowVersion)));
        Assert.Equal(14, edited.Specificity);
        // A stale row version is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PutAsJsonAsync(U(o, $"/tariffs/{line.Id}"),
            Tariff("Partner", w.Partner.Id, null, null, null, 1m, rowVersion: restored.RowVersion))).StatusCode);

        var history = (await ReadAsync<TariffDetails>(await o.Client.GetAsync(U(o, $"/tariffs/{line.Id}")))).History;
        Assert.Equal(["Update", "Restore", "Archive", "Create"], history.Select(h => h.Action));
        Assert.All(history, h => Assert.Equal("Test Owner", h.User));
        var update = history[0];
        // Readable (DT-188): labels, amounts with 3 decimals, "All" for an open scope, statuses in words, the event as a sentence.
        Assert.Contains(update.Changes, c => c.Field == "Price (per ton)" && c.OldValue == "3.250" && c.NewValue == "3.500");
        Assert.Contains(update.Changes, c => c.Field == "Loading site" && c.OldValue == "All" && c.NewValue == "Ghor Al-Safi");
        Assert.Contains(history[2].Changes, c => c.Field == "Status" && c.OldValue == "Active" && c.NewValue == "Archived");
        Assert.Contains(history[3].Changes, c => c.Field == "Party" && c.OldValue == "—" && c.NewValue == "Al-Noor Transport");
        Assert.Equal("Test Owner edited the tariff", update.Text);
        Assert.Equal("Test Owner archived the tariff", history[2].Text);

        // History rows are insert-only and are not duplicated into core.AuditLogs; the tariff itself is audited.
        var (rateRows, auditOfHistory) = await f.DbAsync(async db => (
            await db.Set<RateHistory>().IgnoreQueryFilters().CountAsync(h => h.SourceType == "Tariff" && h.NewValues.Contains("Al-Noor Transport")),
            await db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.EntityType == "TRN.RateHistory")));
        Assert.True(rateRows >= 4);
        Assert.Equal(0, auditOfHistory);

        // The partner line is referenced: the partner cannot be deleted (BR-040).
        var del = await o.Client.PostAsJsonAsync(U(o, $"/partners/{w.Partner.Id}/delete"), new DeleteRequest("test"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, del.StatusCode);
    }

    [Fact]
    public async Task Archived_master_records_cannot_be_chosen_on_new_tariff_lines()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var line = await AddAsync(o, Tariff("Customer", w.Customer2.Id, null, null, null, 2m));
        var cust = await ReadAsync<CustomerProfile>(await o.Client.GetAsync(U(o, $"/customers/{w.Customer2.Id}")));
        await (await o.Client.PostAsJsonAsync(U(o, $"/customers/{w.Customer2.Id}/archive"), new StatusChangeRequest(cust.Customer.RowVersion))).EnsureOkAsync();
        var refused = await o.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Customer", w.Customer2.Id, w.Material.Id, null, null, 2m));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("record.archived", await refused.ProblemCodeAsync());
        // The stored line keeps its archived customer when edited.
        await (await o.Client.PutAsJsonAsync(U(o, $"/tariffs/{line.Id}"), Tariff("Customer", w.Customer2.Id, null, null, null, 2.5m, rowVersion: line.RowVersion))).EnsureOkAsync();
    }

    [Fact]
    public async Task Tariff_import_and_export_FR019()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var csv = "نوع الطرف,الطرف,المادة,موقع التحميل,موقع التنزيل,السعر للطن,ساري من\n" +
                  "عميل,Arab Potash,Potash,الكل,,6.250,2026-01-01\n" +
                  "Partner,Al-Noor Transport,,,,3.000,\n" +
                  "عميل,Unknown Co,,,,1.000,\n" +
                  "عميل,Arab Potash,Potash,الكل,,7.000,2026-01-01\n";
        MultipartFormDataContent Form() => new() { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "tariffs.csv" } };
        var preview = await ReadAsync<MasterImportPreview>(await o.Client.PostAsync(U(o, "/tariffs/import/preview"), Form()));
        Assert.Equal(4, preview.TotalRows);
        Assert.Equal(2, preview.RejectedRows);
        Assert.NotEmpty(preview.Rows.Single(r => r.Row == 4).Problems);
        Assert.NotEmpty(preview.Rows.Single(r => r.Row == 5).Problems);
        Assert.Equal("import.hasErrors", await (await o.Client.PostAsync(U(o, "/tariffs/import"), Form())).ProblemCodeAsync());

        csv = string.Join('\n', csv.Split('\n').Take(3)) + "\n";
        var result = await ReadAsync<ImportResult>(await o.Client.PostAsync(U(o, "/tariffs/import"), Form()));
        Assert.Equal(2, result.ImportedRows);
        var lines = await ReadAsync<PagedResult<TariffResponse>>(await o.Client.GetAsync(U(o, "/tariffs?sort=price")));
        Assert.Equal([3.000m, 6.250m], lines.Items.Select(t => t.Price));
        Assert.Equal(12, lines.Items[1].Specificity);
        Assert.Equal("Create", (await ReadAsync<List<RateHistoryEntry>>(await o.Client.GetAsync(U(o, $"/tariffs/{lines.Items[0].Id}/history")))).Single().Action);

        // A line already in the company (archived included) is a duplicate; an explicit mapping matches other headers.
        var archived = lines.Items[0];
        await (await o.Client.PostAsJsonAsync(U(o, $"/tariffs/{archived.Id}/archive"), new StatusChangeRequest(archived.RowVersion))).EnsureOkAsync();
        csv = "Kind,Who,Rate\nPartner,Al-Noor Transport,3.000\nCustomer,Jordan Phosphate,4.500\n";
        MultipartFormDataContent Mapped() => new()
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "tariffs.csv" },
            { new StringContent("{\"partyType\":\"Kind\",\"party\":\"Who\",\"price\":\"Rate\"}"), "mapping" },
        };
        var mapped = await ReadAsync<MasterImportPreview>(await o.Client.PostAsync(U(o, "/tariffs/import/preview"), Mapped()));
        Assert.Equal(1, mapped.RejectedRows);
        Assert.Contains(mapped.Rows.Single(r => r.Row == 2).Problems, x => x.Contains("already exists", StringComparison.Ordinal));
        Assert.Equal("Kind", mapped.Columns.Matched["partyType"]);
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsync(U(o, "/tariffs/import"), Mapped())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsync(U(o, "/tariffs/import/preview"), Mapped())).StatusCode);

        var export = await o.Client.GetAsync(U(o, "/tariffs/export?format=csv"));
        await export.EnsureOkAsync();
        var text = await export.Content.ReadAsStringAsync();
        Assert.Contains("Arab Potash", text, StringComparison.Ordinal);
        Assert.Contains("All", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ monthly site rates (FR-027 … FR-037)

    [Fact]
    public async Task Month_strip_entry_no_inheritance_and_history_FR027_FR028_FR029_FR036()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        const string month = "2026-03";
        var empty = await ReadAsync<MonthRatesResponse>(await o.Client.GetAsync(U(o, $"/monthly-rates/{month}")));
        Assert.Equal(MonthRateStatus.None, empty.Status);
        Assert.Equal(2, empty.Sites.Count);
        Assert.All(empty.Sites, s => Assert.Null(s.OwnFleetRate));

        var saved = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.500m, 4.250m, null)])));
        Assert.Equal(1, saved.Changed);
        Assert.Empty(saved.Warnings);
        Assert.Equal(MonthRateStatus.Partial, saved.Month.Status);
        saved = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading2.Id, 2.000m, null, null)])));
        Assert.Equal(MonthRateStatus.Complete, saved.Month.Status);

        // The next month inherits nothing (FR-029).
        Assert.Equal(MonthRateStatus.None, (await ReadAsync<MonthRatesResponse>(await o.Client.GetAsync(U(o, "/monthly-rates/2026-04")))).Status);
        var strip = await ReadAsync<List<MonthStatusResponse>>(await o.Client.GetAsync(U(o, "/monthly-rates/months?from=2026-02&to=2026-04")));
        Assert.Equal(["None", "Complete", "None"], strip.Select(m => m.Status));

        // Validation: rates > 0, at most the currency's decimals; month format; an unknown site.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 0m, 1.00001m, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.GetAsync(U(o, "/monthly-rates/2026-13"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Unloading.Id, 1m, null, null)]))).StatusCode);

        // Edit, then clear a site without trips (soft delete) — both in the history (old → new, user, time).
        var row = saved.Month.Sites.Single(s => s.LoadingSite.Id == w.Loading.Id);
        // Changing stored rates without the row version last seen is refused (several managers edit the grid).
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.750m, 4.250m, null)]))).StatusCode);
        await (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.750m, 4.250m, row.RowVersion)]))).EnsureOkAsync();
        // A stale row version is refused.
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.800m, 4.250m, row.RowVersion)]))).StatusCode);
        var row2 = saved.Month.Sites.Single(s => s.LoadingSite.Id == w.Loading2.Id);
        var cleared = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading2.Id, null, null, row2.RowVersion)])));
        Assert.Equal(MonthRateStatus.Partial, cleared.Month.Status);
        Assert.Null(cleared.Month.Sites.Single(s => s.LoadingSite.Id == w.Loading2.Id).Id);
        var history = await ReadAsync<List<RateHistoryEntry>>(await o.Client.GetAsync(U(o, $"/monthly-rates/{month}/history")));
        Assert.Equal(["Clear", "Update", "Create", "Create"], history.Select(h => h.Action));
        Assert.Equal("Eshidiya", history[0].Subject);
        Assert.Contains(history[1].Changes, c => c.Field == "Own fleet rate" && c.OldValue == "3.500" && c.NewValue == "3.750");
        Assert.Equal("Test Owner cleared the monthly rate Eshidiya", history[0].Text);
        Assert.Equal(2, (await ReadAsync<List<RateHistoryEntry>>(await o.Client.GetAsync(U(o, $"/monthly-rates/{month}/history?loadingSite={w.Loading.Id}")))).Count);

        // A cleared site can get rates again (the cleared row is soft-deleted, outside the unique index).
        var again = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading2.Id, 2.500m, null, null)])));
        Assert.Equal(MonthRateStatus.Complete, again.Month.Status);
        again = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading2.Id, null, null, again.Month.Sites.Single(s => s.LoadingSite.Id == w.Loading2.Id).RowVersion)])));
        Assert.Equal(MonthRateStatus.Partial, again.Month.Status);

        // Renaming the site keeps its rates (linked by id, FR-035).
        var site = await ReadAsync<RecordDetails<SiteResponse>>(await o.Client.GetAsync(U(o, $"/loading-sites/{w.Loading.Id}")));
        await (await o.Client.PutAsJsonAsync(U(o, $"/loading-sites/{w.Loading.Id}"), new SiteRequest("Safi North", null, null, site.Record.RowVersion))).EnsureOkAsync();
        var renamed = await ReadAsync<MonthRatesResponse>(await o.Client.GetAsync(U(o, $"/monthly-rates/{month}")));
        Assert.Equal(3.750m, renamed.Sites.Single(s => s.LoadingSite.Name == "Safi North").OwnFleetRate);

        var export = await o.Client.GetAsync(U(o, $"/monthly-rates/{month}/export?format=csv"));
        await export.EnsureOkAsync();
        Assert.Contains("Safi North", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A loading site with monthly rates cannot be deleted (BR-040).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await o.Client.PostAsJsonAsync(U(o, $"/loading-sites/{w.Loading.Id}/delete"), new DeleteRequest("x"))).StatusCode);
    }

    [Fact]
    public async Task Months_with_trips_warn_and_refuse_clearing_a_used_site_FR034_FR035()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        const string month = "2026-05";
        await (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.500m, 4.000m, null), new SiteRateRequest(w.Loading2.Id, 2.000m, null, null)]))).EnsureOkAsync();
        await InsertTripAsync(o, w, new DateOnly(2026, 5, 10));

        var current = await ReadAsync<MonthRatesResponse>(await o.Client.GetAsync(U(o, $"/monthly-rates/{month}")));
        Assert.True(current.HasTrips);
        var used = current.Sites.Single(s => s.LoadingSite.Id == w.Loading.Id);
        Assert.Equal(1, used.TripCount);
        Assert.True((await ReadAsync<List<MonthStatusResponse>>(await o.Client.GetAsync(U(o, $"/monthly-rates/months?from={month}&to={month}"))))[0].HasTrips);

        var saved = await ReadAsync<MonthRatesSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.600m, 4.000m, used.RowVersion)])));
        Assert.Contains(saved.Warnings, x => x.Contains("only new trips", StringComparison.Ordinal));

        string Version(SiteResponse site) => saved.Month.Sites.Single(s => s.LoadingSite.Id == site.Id).RowVersion!;
        var refused = await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, null, null, Version(w.Loading))]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("rates.inUse", await refused.ProblemCodeAsync());
        // The unused site in the same month can still be cleared.
        await (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{month}"), new MonthRatesRequest([new SiteRateRequest(w.Loading2.Id, null, null, Version(w.Loading2))]))).EnsureOkAsync();
    }

    [Fact]
    public async Task Quote_takes_the_monthly_driver_rate_by_fleet_class_and_names_missing_rates_FR030_FR037()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var date = new DateOnly(2026, 6, 12);
        var missing = await QuoteAsync(w, date, driver: w.OwnDriver.Id);
        Assert.Null(missing.DriverRate!.Rate);
        Assert.Equal("Own", missing.DriverRate.FleetClass);
        Assert.Equal("2026-06", missing.DriverRate.RateMonth);
        Assert.Contains("Ghor Al-Safi", missing.DriverRate.Missing, StringComparison.Ordinal);
        Assert.Contains("own fleet", missing.DriverRate.Missing, StringComparison.Ordinal);
        Assert.True(missing.DriverRate.CanEnterManualRate);

        await (await o.Client.PutAsJsonAsync(U(o, "/monthly-rates/2026-06"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 3.500m, null, null)]))).EnsureOkAsync();
        var own = await QuoteAsync(w, date, driver: w.OwnDriver.Id);
        Assert.Equal(3.500m, own.DriverRate!.Rate);
        Assert.Equal("Monthly", own.DriverRate.Source);

        // The partner driver: partner pre-filled from the driver (partner trip → driver rate 0) and the partner rate from the tariff.
        await AddAsync(o, Tariff("Partner", w.Partner.Id, null, null, null, 2.750m));
        var partnerTrip = await QuoteAsync(w, date, driver: w.PartnerDriver.Id);
        Assert.True(partnerTrip.DriverRate!.IsPartnerTrip);
        Assert.Equal(0m, partnerTrip.DriverRate.Rate);
        Assert.Equal("External", partnerTrip.DriverRate.FleetClass);
        Assert.Equal(2.750m, partnerTrip.PartnerRate!.Price);
        Assert.False(partnerTrip.TariffLock);
        Assert.True(partnerTrip.CanDeviateFromTariff);

        // Missing-rates warning for the dashboard: the second loading site has no rate in June.
        var warn = await ReadAsync<MissingRatesResponse>(await o.Client.GetAsync(U(o, "/monthly-rates/missing?month=2026-06")));
        Assert.Equal(["Eshidiya"], warn.Sites.Select(s => s.Name));
        Assert.Equal(2, warn.ActiveSites);
        var current = (await ReadAsync<MissingRatesResponse>(await o.Client.GetAsync(U(o, "/monthly-rates/missing")))).Month;
        Assert.Contains(current, new[] { PricingMonth(-1), PricingMonth(0), PricingMonth(1) }); // company time zone may differ from UTC

        // An own-fleet driver cannot carry a partner trip (DT-48).
        var q = $"/pricing/quote?driver={w.OwnDriver.Id}&partner={w.Partner.Id}&loadingSite={w.Loading.Id}&loadingDate=2026-06-12";
        var refused = await o.Client.GetAsync(U(o, q));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("driver.partnerOwnFleet", await refused.ProblemCodeAsync());
    }

    private static string PricingMonth(int days) => Today.AddDays(days).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ permissions (PRD 3.4, BR-004, DT-50)

    [Fact]
    public async Task Viewers_read_pricing_dispatchers_manage_it_accountants_cannot_change_it()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var line = await AddAsync(o, Tariff("Customer", null, null, null, null, 4m));
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var accountant = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Accountant);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);

        await (await viewer.Client.GetAsync(U(o, "/tariffs"))).EnsureOkAsync();
        await (await viewer.Client.GetAsync(U(o, "/monthly-rates/2026-01"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/tariffs"), Tariff("Customer", null, w.Material.Id, null, null, 1m))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.GetAsync(U(o, "/pricing/quote"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.PutAsJsonAsync(U(o, "/monthly-rates/2026-01"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 1m, null, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Client.PostAsJsonAsync(U(o, $"/tariffs/{line.Id}/archive"), new StatusChangeRequest(null))).StatusCode);

        await AddAsync(dispatcher, Tariff("Customer", null, w.Material.Id, null, null, 1m)) ;
        await (await dispatcher.Client.PutAsJsonAsync(U(o, "/monthly-rates/2026-01"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 1m, null, null)]))).EnsureOkAsync();
        var quote = await ReadAsync<PriceQuoteResponse>(await dispatcher.Client.GetAsync(U(o, $"/pricing/quote?driver={w.OwnDriver.Id}&loadingSite={w.Loading.Id}&loadingDate=2026-01-20")));
        Assert.Equal(1m, quote.DriverRate!.Rate);
    }

    [Fact]
    public async Task Another_company_cannot_reach_tariffs_rates_or_their_history()
    {
        var w = await WorldAsync();
        var line = await AddAsync(w.Owner, Tariff("Customer", w.Customer.Id, null, null, null, 4m));
        await (await w.Owner.Client.PutAsJsonAsync(U(w.Owner, "/monthly-rates/2026-02"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 1m, null, null)]))).EnsureOkAsync();
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/tariffs/{line.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/tariffs/{line.Id}/history"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync(U(other, $"/tariffs/{line.Id}"), Tariff("Customer", null, null, null, null, 1m))).StatusCode);
        Assert.Empty(await ReadAsync<List<RateHistoryEntry>>(await other.Client.GetAsync(U(other, "/monthly-rates/2026-02/history"))));
        Assert.Empty((await ReadAsync<MonthRatesResponse>(await other.Client.GetAsync(U(other, "/monthly-rates/2026-02")))).Sites);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.Client.PutAsJsonAsync(U(other, "/monthly-rates/2026-02"),
            new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 1m, null, null)]))).StatusCode);
        Assert.Empty((await ReadAsync<PagedResult<TariffResponse>>(await other.Client.GetAsync(U(other, $"/tariffs?party={w.Customer.Id}")))).Items);
    }

    /// <summary>Inserts a trip directly (the trips module comes in B5) so the month and site have a trip.</summary>
    private async Task InsertTripAsync(SignedInUser o, World w, DateOnly loadingDate)
    {
        await f.DbAsync(async db =>
        {
            var tokens = f.Services.GetRequiredService<IUrlTokenService>();
            var companyPid = tokens.Decode<Company>(o.CompanyId)!.Value;
            var company = await db.Set<Company>().IgnoreQueryFilters().SingleAsync(c => c.PublicId == companyPid);
            async Task<long> Id<T>(string token) where T : AuditableEntity
            {
                var g = tokens.Decode<T>(token)!.Value;
                return await db.Set<T>().IgnoreQueryFilters().Where(x => x.PublicId == g).Select(x => x.Id).SingleAsync();
            }
            db.Set<Trip>().Add(new Trip
            {
                TenantId = company.TenantId, CompanyId = company.Id, ReceiptNo = "R-1", ReceiptNoKey = "R-1", TripDate = loadingDate, LoadingDate = loadingDate,
                DriverId = await Id<Driver>(w.OwnDriver.Id), CustomerId = await Id<Customer>(w.Customer.Id), LoadingSiteId = await Id<LoadingSite>(w.Loading.Id),
                UnloadingSiteId = await Id<UnloadingSite>(w.Unloading.Id), MaterialId = await Id<Material>(w.Material.Id), TruckPlateAtSave = "10-11111",
                FleetClass = "Own", LoadedWeight = 30m, BillingQty = 30m, WageQty = 30m, CustomerPrice = 6m, DriverRate = 3.5m, RateSource = "Monthly",
                RateMonth = loadingDate.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), Revenue = 180m, DriverWage = 105m,
            });
            await db.SaveChangesAsync();
            return true;
        });
    }
}
