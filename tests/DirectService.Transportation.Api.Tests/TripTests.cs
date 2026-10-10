using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Acc;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B5 trips through the API: create with pricing, calculation and posting (FR-001…006, FR-016, FR-022, FR-030, FR-031), validation
/// (7.1.1), tariff lock and manual rates (FR-025, BR-004), edit with reverse + re-post (FR-010, FR-032), void (FR-011), Needs review
/// (FR-014), list filters and totals (FR-009), recent and quick-entry defaults (FR-007, FR-008), import (FR-013, FR-033), history
/// (FR-015) and permissions.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TripTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1); // safely not in the future in Amman
    private static string Month(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private sealed record World(SignedInUser Owner, CustomerResponse Customer, PartnerResponse Partner, MaterialResponse Material, SiteResponse Loading, SiteResponse Loading2,
        SiteResponse Unloading, DriverResponse OwnDriver, DriverResponse PartnerDriver);

    /// <summary>A company with the transport chart (mapping complete), master data, a customer tariff 6.000, a partner tariff 2.750 and this month's rates.</summary>
    private async Task<World> WorldAsync()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), new CustomerRequest("Arab Potash", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        var partner = await ReadAsync<PartnerResponse>(await c.PostAsJsonAsync(U(o, "/partners"), new PartnerRequest("Al-Noor Transport", null, null, null, null, null, null, null, null, null, null)));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Potash", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Ghor Al-Safi", null, null, null)));
        var loading2 = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Al-Hasa", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Aqaba Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var rented = await ReadAsync<OwnershipTypeResponse>(await c.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest("Rented", "External", null, null)));
        DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId, DateOnly? insurance = null) =>
            new(name, nid, "0791234567", null, null, "L-1", "C", Today.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                Today.AddYears(1), insurance ?? Today.AddYears(1), Today.AddYears(1), null, null, null, null);
        var own = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Ahmad Saleh", "9871234560", "10-11111", types.Single(t => t.IsBuiltIn).Id, null, Today.AddDays(-3))));
        var ext = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Sami Abbadi", "9871234561", "10-22222", rented.Id, partner.Id)));
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Customer", customer.Id, material.Id, null, null, 6.000m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null))).EnsureOkAsync();
        foreach (var m in new[] { Month(Today), Month(Today.AddMonths(-1)) }.Distinct())
            await (await c.PutAsJsonAsync(U(o, $"/monthly-rates/{m}"), new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]))).EnsureOkAsync();
        return new World(o, customer, partner, material, loading, loading2, unloading, own, ext);
    }

    private static TripRequest Trip(World w, string receipt, DriverResponse? driver = null, decimal loaded = 32.000m, decimal? unloaded = 31.850m, DateOnly? date = null,
        decimal? price = null, decimal? rate = null, string? loading = null, string? partner = null, string? rowVersion = null, string? clientId = null) =>
        new(receipt, date ?? Today, null, null, (driver ?? w.OwnDriver).Id, w.Customer.Id, loading ?? w.Loading.Id, w.Unloading.Id, w.Material.Id, partner,
            loaded, unloaded, price, rate, null, null, null, clientId, rowVersion);

    private static async Task<TripSaveResult> CreateAsync(SignedInUser o, TripRequest r) => await ReadAsync<TripSaveResult>(await o.Client.PostAsJsonAsync(U(o, "/trips"), r));

    private Task<List<JournalEntry>> EntriesAsync(long tripId) =>
        f.DbAsync(db => db.Set<JournalEntry>().IgnoreQueryFilters().Include(e => e.Lines).Where(e => e.SourceType == "Trip" && e.SourceId == tripId).OrderBy(e => e.Id).ToListAsync());

    /// <summary>Internal id of the latest trip with this receipt number (tests in the collection run one at a time).</summary>
    private Task<long> TripIdAsync(string receipt, SignedInUser o) =>
        f.DbAsync(db => db.Set<Trip>().IgnoreQueryFilters().Where(t => t.ReceiptNo == receipt).OrderByDescending(t => t.Id).Select(t => t.Id).FirstAsync());

    // ------------------------------------------------------------------ create, price, calculate, post

    [Fact]
    public async Task Trip_is_priced_calculated_frozen_and_posted_PRD_example()
    {
        var w = await WorldAsync();
        var saved = await CreateAsync(w.Owner, Trip(w, "TRP-10482"));
        var t = saved.Trip;
        Assert.Equal(6.000m, t.CustomerPrice);
        Assert.Equal(6.000m, t.TariffPrice);
        Assert.Equal(3.500m, t.DriverRate);
        Assert.Equal("Monthly", t.RateSource);
        Assert.Equal(Month(Today), t.RateMonth);
        Assert.Equal("Own", t.FleetClass);
        Assert.Equal("10-11111", t.TruckPlate);
        Assert.Equal(191.100m, t.Revenue);
        Assert.Equal(111.475m, t.DriverWage);
        Assert.Equal(79.625m, t.Margin);
        Assert.Equal(0.150m, t.ShortageWeight);
        Assert.False(t.IsShortageFlagged);
        Assert.False(t.IsTariffBreach);
        Assert.Equal("Unbilled", t.Billing);
        Assert.True(t.CanEdit);
        Assert.NotNull(t.JournalEntry);
        Assert.False(t.PostingPending);
        // Expired insurance → warning naming the document, save allowed (FR-155).
        Assert.Contains(saved.Warnings, x => x.Code == "trip.warnDocument" && x.Message.Contains("insurance", StringComparison.Ordinal));

        // Wage accrual: Dr driver wage expense / Cr driver payable with the driver as party (FR-016, 8.6).
        var entries = await EntriesAsync(await TripIdAsync("TRP-10482", w.Owner));
        var e = Assert.Single(entries);
        Assert.Equal(111.475m, e.TotalDebit);
        Assert.Contains(e.Lines, l => l.Credit == 111.475m && l.PartyType == "Driver");

        // Quick-entry defaults and recent trips come from this user's last trip (FR-007, FR-008).
        var quick = await ReadAsync<QuickEntryDefaults>(await w.Owner.Client.GetAsync(U(w.Owner, "/trips/quick-entry")));
        Assert.Equal(w.OwnDriver.Id, quick.Driver!.Id);
        Assert.Equal(w.Loading.Id, quick.LoadingSite!.Id);
        Assert.Equal("TRP-10482", Assert.Single(await ReadAsync<List<TripResponse>>(await w.Owner.Client.GetAsync(U(w.Owner, "/trips/recent")))).ReceiptNo);

        // Later rate changes never alter the saved trip (BR-030).
        var month = await ReadAsync<MonthRatesResponse>(await w.Owner.Client.GetAsync(U(w.Owner, $"/monthly-rates/{Month(Today)}")));
        var row = month.Sites.Single(s => s.LoadingSite.Id == w.Loading.Id);
        await (await w.Owner.Client.PutAsJsonAsync(U(w.Owner, $"/monthly-rates/{Month(Today)}"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, 9.000m, 4.250m, row.RowVersion)]))).EnsureOkAsync();
        var again = await ReadAsync<TripDetails>(await w.Owner.Client.GetAsync(U(w.Owner, $"/trips/{t.Id}")));
        Assert.Equal(3.500m, again.Trip.DriverRate);
        Assert.Equal("Create", again.History[^1].Action);
    }

    [Fact]
    public async Task Partner_trip_takes_the_partner_rate_driver_rate_zero_and_posts_partner_cost()
    {
        var w = await WorldAsync();
        var t = (await CreateAsync(w.Owner, Trip(w, "TRP-20001", w.PartnerDriver, 31.500m, 31.020m, partner: w.Partner.Id))).Trip;
        Assert.Equal("External", t.FleetClass);
        Assert.Equal(0m, t.DriverRate);
        Assert.Equal(2.750m, t.PartnerRate);
        Assert.Equal(85.305m, t.PartnerCost);
        Assert.Equal(100.815m, t.Margin);
        Assert.True(t.IsShortageFlagged);
        var e = Assert.Single(await EntriesAsync(await TripIdAsync("TRP-20001", w.Owner)));
        Assert.Contains(e.Lines, l => l.Credit == 85.305m && l.PartyType == "Partner");
        // An own-fleet driver cannot carry a partner trip (DT-48).
        var refused = await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/trips"), Trip(w, "TRP-20002", partner: w.Partner.Id));
        Assert.Equal("driver.partnerOwnFleet", await refused.ProblemCodeAsync());
    }

    [Fact]
    public async Task Trip_validation_and_rate_rules_7_1_1_FR030()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        await CreateAsync(o, Trip(w, "TRP-30001"));
        // Receipt number unique ignoring case and spaces.
        var dup = await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, " trp-30001 "));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("trip.receiptDuplicate", await dup.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // Future date; loaded weight > 0; unloading date before loading date.
        var bad = await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-30002", loaded: 0m, date: Today.AddDays(5)) with { UnloadingDate = Today.AddDays(-30) });
        var text = await bad.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("validation.futureDate", text, StringComparison.Ordinal);
        Assert.Contains("loadedWeight", text, StringComparison.Ordinal);
        // No monthly rate for the site → blocked naming site, month and fleet type.
        var missing = await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-30003", loading: w.Loading2.Id));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        Assert.Equal("rate.missing", await missing.ProblemCodeAsync());
        Assert.Contains("Al-Hasa", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        // The Owner (masters.manage) may type a rate: saved as Manual (FR-030).
        var manual = (await CreateAsync(o, Trip(w, "TRP-30004", loading: w.Loading2.Id, rate: 3.900m))).Trip;
        Assert.Equal("Manual", manual.RateSource);
        Assert.True(manual.IsManualRate);
        // A user with trips.edit but not masters.manage cannot type a different rate (BR-004).
        var clerk = await CustomRoleUserAsync(o, PermissionKeys.TripsView, PermissionKeys.TripsEdit);
        var notAllowed = await clerk.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-30005", rate: 4.000m));
        Assert.Equal("rate.manualNotAllowed", await notAllowed.ProblemCodeAsync());
        await CreateAsync(clerk, Trip(w, "TRP-30006", rate: 3.500m));   // same as the monthly rate → Monthly

        // Tariff lock: deviating prices only for masters.manage; deviations are audited (FR-025).
        var ops = await ReadAsync<OperationSettingsResponse>(await o.Client.GetAsync(U(o, "/settings/operations")));
        await (await o.Client.PutAsJsonAsync(U(o, "/settings/operations"), new OperationSettingsRequest(ops.BillOnUnloadedWeight, ops.PayDriverOnLoadedWeight, ops.ShortageTolerancePct,
            true, ops.InvoiceApproval, ops.AccrueDriverWage, true))).EnsureOkAsync();
        Assert.Equal("tariff.locked", await (await clerk.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-30007", price: 5.800m))).ProblemCodeAsync());
        var deviated = (await CreateAsync(o, Trip(w, "TRP-30008", price: 5.800m))).Trip;
        Assert.True(deviated.IsTariffBreach);
        Assert.Equal(-6.370m, deviated.BreachAmount);  // (5.800 − 6.000) × 31.850
        var history = (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{deviated.Id}")))).History;
        Assert.Contains(history, h => h.Reason != null && h.Reason.Contains("5.800", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Edit_reverses_and_reposts_void_reverses_and_closed_periods_refuse_FR010_FR011()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var t = (await CreateAsync(o, Trip(w, "TRP-40001"))).Trip;
        var edited = (await ReadAsync<TripSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), Trip(w, "TRP-40001", unloaded: 31.900m, rowVersion: t.RowVersion)))).Trip;
        Assert.Equal(111.650m, edited.DriverWage);
        Assert.Equal(3.500m, edited.DriverRate);   // kept: site, month and driver unchanged (FR-032)
        var id = await TripIdAsync("TRP-40001", o);
        var entries = await EntriesAsync(id);
        Assert.Equal(3, entries.Count);   // original (reversed), its reversal, the new entry (BR-035)
        Assert.Equal("Reversed", entries[0].Status);
        Assert.Equal(entries[0].Id, entries[1].ReversesEntryId);
        Assert.Equal(111.650m, entries[2].TotalDebit);
        var history = await ReadAsync<List<HistoryEntry>>(await o.Client.GetAsync(U(o, $"/trips/{t.Id}/history")));
        Assert.Contains(history.First(h => h.Action == "Update").Changes, c => c.Field == "unloadedWeight" && c.OldValue == "31.850" && c.NewValue == "31.900");

        // Changes need the row version last seen (DT-73): missing → 400, stale → 409.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), Trip(w, "TRP-40001", unloaded: 31.950m))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), Trip(w, "TRP-40001", unloaded: 31.950m, rowVersion: t.RowVersion))).StatusCode);
        // Changing the loading site resolves the rate again (FR-032); the resent old rate is not a typed rate: no rate there → blocked.
        Assert.Equal("rate.missing", await (await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"),
            Trip(w, "TRP-40001", unloaded: 31.900m, loading: w.Loading2.Id, rate: 3.500m, rowVersion: edited.RowVersion))).ProblemCodeAsync());
        // An edit of notes only keeps the posted entry (no reversal).
        var notesOnly = (await ReadAsync<TripSaveResult>(await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), Trip(w, "TRP-40001", unloaded: 31.900m, rowVersion: edited.RowVersion) with { Notes = "checked" }))).Trip;
        Assert.Equal(3, (await EntriesAsync(id)).Count);
        edited = notesOnly;

        // Void needs a reason; reversal posted; record kept with status Void.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, $"/trips/{t.Id}/void"), new TripVoidRequest("", null))).StatusCode);
        var voided = await ReadAsync<TripResponse>(await o.Client.PostAsJsonAsync(U(o, $"/trips/{t.Id}/void"), new TripVoidRequest("Entered twice", edited.RowVersion)));
        Assert.Equal("Void", voided.Status);
        Assert.False(voided.CanEdit);
        Assert.Equal(4, (await EntriesAsync(id)).Count);
        Assert.Equal("trip.void", await (await o.Client.PutAsJsonAsync(U(o, $"/trips/{t.Id}"), Trip(w, "TRP-40001"))).ProblemCodeAsync());

        // A trip dated in a closed period cannot be created or edited (BR-036).
        var last = Today.AddMonths(-1);
        var old = (await CreateAsync(o, Trip(w, "TRP-40002", date: new DateOnly(last.Year, last.Month, 10)))).Trip;
        await (await o.Client.PostAsJsonAsync(U(o, $"/accounting/periods/{Month(last)}/close"), new PeriodActionRequest("Month end", true))).EnsureOkAsync();
        Assert.Equal("period.closed", await (await o.Client.PutAsJsonAsync(U(o, $"/trips/{old.Id}"), Trip(w, "TRP-40002", date: new DateOnly(last.Year, last.Month, 10), unloaded: 31.000m, rowVersion: old.RowVersion))).ProblemCodeAsync());
        Assert.Equal("period.closed", await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{old.Id}/void"), new TripVoidRequest("x", old.RowVersion))).ProblemCodeAsync());
        Assert.Equal("period.closed", await (await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-40003", date: new DateOnly(last.Year, last.Month, 11)))).ProblemCodeAsync());
    }

    [Fact]
    public async Task List_filters_totals_and_clearing_rates_used_by_a_trip_is_refused_FR009_FR035()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        await CreateAsync(o, Trip(w, "TRP-50001"));
        await CreateAsync(o, Trip(w, "TRP-50002", w.PartnerDriver, 31.500m, 31.020m, partner: w.Partner.Id));
        await CreateAsync(o, Trip(w, "TRP-50003", loaded: 30.000m, unloaded: null));
        var all = await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips")));
        Assert.Equal(3, all.Totals.Trips);
        Assert.Equal(93.500m, all.Totals.LoadedWeight);
        Assert.Single((await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?partnerTrips=yes")))).Page.Items);
        Assert.Single((await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?shortage=true")))).Page.Items);
        Assert.Equal(2, (await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?fleet=own")))).Totals.Trips);
        Assert.Equal("TRP-50003", (await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?q=50003")))).Page.Items.Single().ReceiptNo);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.GetAsync(U(o, "/trips?billing=maybe"))).StatusCode);
        var export = await o.Client.GetAsync(U(o, "/trips/export?format=csv"));
        await export.EnsureOkAsync();
        Assert.Contains("TRP-50002", await export.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Rates used by trips cannot be cleared (FR-035).
        var month = await ReadAsync<MonthRatesResponse>(await o.Client.GetAsync(U(o, $"/monthly-rates/{Month(Today)}")));
        var row = month.Sites.Single(s => s.LoadingSite.Id == w.Loading.Id);
        Assert.True(month.HasTrips);
        Assert.Equal("rates.inUse", await (await o.Client.PutAsJsonAsync(U(o, $"/monthly-rates/{Month(Today)}"), new MonthRatesRequest([new SiteRateRequest(w.Loading.Id, null, null, row.RowVersion)]))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Import_strict_and_tolerant_with_rate_rules_and_review_FR013_FR033_FR014()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var d = Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var csv = "رقم السند,التاريخ,السائق,العميل,موقع التحميل,موقع التنزيل,المادة,الوزن المحمّل,الوزن المنزّل,أجرة السائق\n" +
                  $"IMP-1,{d},9871234560,Arab Potash,Ghor Al-Safi,Aqaba Port,Potash,32.000,31.900,\n" +           // monthly rate
                  $"IMP-2,{d},10-11111,Arab Potash,Ghor Al-Safi,Aqaba Port,Potash,32.000,abc,\n" +                 // bad unloaded: tolerant → review
                  $"IMP-3,{d},Ahmad Saleh,Arab Potash,Al-Hasa,Aqaba Port,Potash,32.000,31.000,\n" +                 // no rate → rejected
                  $"IMP-1,{d},Ahmad Saleh,Arab Potash,Ghor Al-Safi,Aqaba Port,Potash,32.000,31.000,\n" +            // duplicate in file
                  $"IMP-5,{d},Sami Abbadi,Arab Potash,Ghor Al-Safi,Aqaba Port,Potash,31.000,30.900,\n" +            // partner from driver
                  $"IMP-6,{d},Ahmad Saleh,Arab Potash,Ghor Al-Safi,Aqaba Port,Potash,32.000,32.000,3.800\n";        // manual rate (Owner)
        MultipartFormDataContent Form(string mode) => new() { { new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "trips.csv" }, { new StringContent(mode), "mode" } };

        var strict = await ReadAsync<TripImportPreview>(await o.Client.PostAsync(U(o, "/trips/import/preview"), Form("strict")));
        Assert.Equal(6, strict.TotalRows);
        Assert.Equal(3, strict.RejectedRows);   // bad unloaded, no rate, duplicate
        Assert.Equal(1, strict.DuplicateRows);
        Assert.Equal("import.hasErrors", await (await o.Client.PostAsync(U(o, "/trips/import"), Form("strict"))).ProblemCodeAsync());

        var tolerant = await ReadAsync<TripImportPreview>(await o.Client.PostAsync(U(o, "/trips/import/preview"), Form("tolerant")));
        Assert.Equal(4, tolerant.ValidRows);
        Assert.Equal(1, tolerant.ReviewRows);
        Assert.Contains(tolerant.Rows.Single(r => r.Row == 4).Problems, p => p.Contains("Al-Hasa", StringComparison.Ordinal));
        var result = await ReadAsync<TripImportResult>(await o.Client.PostAsync(U(o, "/trips/import"), Form("tolerant")));
        Assert.Equal(4, result.ImportedRows);
        Assert.Equal(2, result.RejectedRows);
        Assert.Equal(1, result.ReviewRows);

        var list = (await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?sort=receiptNo")))).Page.Items;
        Assert.Equal(["IMP-1", "IMP-2", "IMP-5", "IMP-6"], list.Select(t => t.ReceiptNo));
        Assert.Equal(w.Partner.Id, list.Single(t => t.ReceiptNo == "IMP-5").Partner!.Id);
        Assert.Equal("Manual", list.Single(t => t.ReceiptNo == "IMP-6").RateSource);
        // The manual rate of an imported trip is audited with its reason (review H1).
        var imp6 = await ReadAsync<List<HistoryEntry>>(await o.Client.GetAsync(U(o, $"/trips/{list.Single(t => t.ReceiptNo == "IMP-6").Id}/history")));
        Assert.Contains(imp6, h => h.Action == "Create" && h.Reason != null && h.Reason.Contains("3.800", StringComparison.Ordinal));
        var review = list.Single(t => t.ReceiptNo == "IMP-2");
        Assert.True(review.NeedsReview);
        Assert.Null(review.UnloadedWeight);
        Assert.Single((await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?review=true")))).Page.Items);

        // Clear Needs review after checking (FR-014); recorded in history.
        var cleared = await ReadAsync<TripResponse>(await o.Client.PostAsJsonAsync(U(o, $"/trips/{review.Id}/clear-review"), new TripReviewRequest(review.RowVersion)));
        Assert.False(cleared.NeedsReview);
        Assert.Contains(await ReadAsync<List<HistoryEntry>>(await o.Client.GetAsync(U(o, $"/trips/{review.Id}/history"))), h => h.Action == "Reviewed");
        // Every imported trip posted its entry.
        Assert.All(list, t => Assert.NotNull(t.JournalEntry));
    }

    [Fact]
    public async Task Permissions_and_isolation_for_trips()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var t = (await CreateAsync(o, Trip(w, "TRP-60001"))).Trip;
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        await (await viewer.Client.GetAsync(U(o, $"/trips/{t.Id}"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-60002"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PostAsJsonAsync(U(o, $"/trips/{t.Id}/void"), new TripVoidRequest("x", null))).StatusCode);
        await CreateAsync(dispatcher, Trip(w, "TRP-60003"));
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/trips/{t.Id}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync(U(other, $"/trips/{t.Id}/void"), new TripVoidRequest("x", null))).StatusCode);
        // Idempotent create by clientId (API-05).
        var cid = Guid.NewGuid().ToString();
        var first = await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-60004", clientId: cid));
        var second = await o.Client.PostAsJsonAsync(U(o, "/trips"), Trip(w, "TRP-60004", clientId: cid));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    /// <summary>Creates a custom role with the given keys, invites a user with it and signs them in.</summary>
    private async Task<SignedInUser> CustomRoleUserAsync(SignedInUser owner, params string[] keys)
    {
        var role = await ReadAsync<RoleResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/roles"), new RoleRequest("Clerk " + Guid.NewGuid().ToString("N")[..6], keys, null)));
        var email = $"clerk-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(email, role.Id))).EnsureOkAsync();
        var token = f.Emails.LinkParameters(email, "Invitation")["t"];
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(token, "Clerk User", ApiHelpers.Password))).EnsureOkAsync();
        return await f.SignInFirstTimeAsync(client, email, ApiHelpers.Password);
    }
}
