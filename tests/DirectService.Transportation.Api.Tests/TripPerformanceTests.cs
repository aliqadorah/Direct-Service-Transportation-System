using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// NFR-01 / NFR-02 smoke test (build plan B5): one company with 500,000 trips; the Created Trips list (first page, filters and totals)
/// answers in under one second. The trips are bulk-inserted with one INSERT … SELECT (they carry no journal entries). Since DT-271 half of
/// them are partner trips of one partner, two fifths of whose cost is paid, so the "Settled / Unsettled" filters run the partner coverage
/// (a running total over 250,000 trips) within the same limit.
/// A manual performance check, not part of the normal or CI test run (DT-227): the test is explicit, so <c>dotnet test</c> leaves it out
/// and only <c>--explicit only</c> runs it — through <c>tools/perf/run-performance-check.ps1</c> (or <c>.sh</c>) on its own database,
/// before a release (docs/step9/Setup_and_Run.md §8). The limit stays 1 second.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TripPerformanceTests(ApiFactory f, ITestOutputHelper output)
{
    private const int TripCount = 500_000;
    private const decimal PartnerCost = 87.59m;   // two decimals: the bulk insert sends it as decimal(18,2)

    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    [Fact(Explicit = true)]
    [Trait("Category", "Performance")]
    public async Task Created_trips_list_with_500000_trips_answers_under_one_second_NFR01()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var c = o.Client;
        // The world's one trip gives the material and sites; the 500,000 below use the same references.
        var first = await w.Trip("PERF-0", w.Driver, null);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var tokens = f.Services.GetRequiredService<IUrlTokenService>();
        var sw = Stopwatch.StartNew();
        await f.DbAsync(async db =>
        {
            async Task<long> Id<T>(string token) where T : AuditableEntity
            {
                var g = tokens.Decode<T>(token)!.Value;
                return await db.Set<T>().IgnoreQueryFilters().Where(x => x.PublicId == g).Select(x => x.Id).SingleAsync();
            }
            var companyPid = tokens.Decode<Company>(o.CompanyId)!.Value;
            var company = await db.Set<Company>().IgnoreQueryFilters().SingleAsync(x => x.PublicId == companyPid);
            var (d, cu, ls, us, m) = (await Id<Driver>(w.Driver.Id), await Id<Customer>(w.Customer.Id), await Id<LoadingSite>(first.LoadingSite.Id),
                await Id<UnloadingSite>(first.UnloadingSite.Id), await Id<Material>(first.Material.Id));
            var (pd, pa) = (await Id<Driver>(w.PartnerDriver.Id), await Id<Partner>(w.Partner.Id));
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
            // 500,000 trips spread over ~3 years ending today; every 7th unbilled-and-flagged for filter variety; every 2nd a partner trip (DT-271).
            await db.Database.ExecuteSqlAsync($"""
                WITH n AS (SELECT TOP ({TripCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c)
                INSERT INTO trn.TRN_Trips (PublicId, TenantId, CompanyId, ReceiptNo, ReceiptNoKey, TripDate, LoadingDate, DriverId, CustomerId, LoadingSiteId, UnloadingSiteId, MaterialId,
                    PartnerId, TruckPlateAtSave, FleetClass, LoadedWeight, UnloadedWeight, BillingQty, WageQty, CustomerPrice, DriverRate, RateSource, RateMonth, Revenue, DriverWage,
                    PartnerCost, ShortageWeight, ShortagePct, IsShortageFlagged, IsTariffBreach, IsManualRate, NeedsReview, Status, CreatedAt, IsDeleted)
                SELECT NEWID(), {company.TenantId}, {company.Id}, CONCAT('P-', i), CONCAT('P-', i), DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)),
                    DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)), CASE WHEN i % 2 = 0 THEN {pd} ELSE {d} END, {cu}, {ls}, {us}, {m},
                    CASE WHEN i % 2 = 0 THEN {pa} END, '99-99999', CASE WHEN i % 2 = 0 THEN 'External' ELSE 'Own' END, 32.000, 31.850, 31.850, 31.850, 6.0000,
                    CASE WHEN i % 2 = 0 THEN 0 ELSE 3.5000 END, 'Monthly', FORMAT(DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)), 'yyyy-MM'), 191.100,
                    CASE WHEN i % 2 = 0 THEN 0 ELSE 111.475 END, CASE WHEN i % 2 = 0 THEN {PartnerCost} ELSE 0 END, 0.150, 0.4688, CASE WHEN i % 7 = 0 THEN 1 ELSE 0 END, 0, 0, 0,
                    'Active', SYSUTCDATETIME(), 0
                FROM n
                """);
            return true;
        });
        output.WriteLine($"Inserted {TripCount:N0} trips in {sw.Elapsed.TotalSeconds:F1} s");
        // Two fifths of the partner's 250,000 trips paid (the oldest ones).
        await (await c.PostAsJsonAsync(U(o, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, today.AddDays(-1), TripCount / 2 * 2 / 5 * PartnerCost, "Bank", null, w.Bank, "PERF", null, null))).EnsureOkAsync();

        var month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var queries = new[]
        {
            "/trips?pageSize=50",                                                      // default list: newest first, totals of all active trips
            $"/trips?from={month}-01&to={today:yyyy-MM-dd}",                          // this month
            "/trips?shortage=true&billing=unbilled&sort=-revenue",                    // flags + another sort
            "/trips?q=499999",                                                       // search by part of a receipt number
            "/trips?settled=no&pageSize=50",                                          // DT-271: unpaid driver and partner trips
            "/trips?settled=no&partnerTrips=yes&pageSize=50",                         // unpaid partner trips
            $"/trips?settled=yes&partner={w.Partner.Id}&sort=tripDate",              // the partner's paid trips, oldest first
        };
        foreach (var q in queries)
        {
            await (await c.GetAsync(U(o, q))).EnsureOkAsync();  // warm-up (plan cache, buffer pool)
            sw.Restart();
            var page = await (await c.GetAsync(U(o, q))).Content.ReadFromJsonAsync<TripPage>();
            var ms = sw.ElapsedMilliseconds;
            output.WriteLine($"{q} → {page!.Totals.Trips:N0} trips, {ms} ms");
            Assert.True(ms < 1000, $"{q} took {ms} ms (NFR-01: under 1 second)");
        }
        // The split is right: 100,000 partner trips paid, 150,000 open (+ the world's driver trip PERF-0 and 250,000 unsettled driver trips).
        var paid = await (await c.GetAsync(U(o, "/trips?settled=yes&partnerTrips=yes&pageSize=1"))).Content.ReadFromJsonAsync<TripPage>();
        var open = await (await c.GetAsync(U(o, "/trips?settled=no&partnerTrips=yes&pageSize=1"))).Content.ReadFromJsonAsync<TripPage>();
        Assert.Equal(100_000, paid!.Totals.Trips);
        Assert.Equal(150_000, open!.Totals.Trips);
    }
}
