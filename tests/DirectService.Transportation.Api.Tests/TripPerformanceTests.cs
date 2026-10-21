using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// NFR-01 / NFR-02 smoke test (build plan B5): one company with 500,000 trips; the Created Trips list (first page, filters and totals)
/// answers in under one second. The trips are bulk-inserted with one INSERT … SELECT (they carry no journal entries).
/// A manual performance check, not part of the normal or CI test run (DT-227): the test is explicit, so <c>dotnet test</c> leaves it out
/// and only <c>--explicit only</c> runs it — through <c>tools/perf/run-performance-check.ps1</c> (or <c>.sh</c>) on its own database,
/// before a release (docs/step9/Setup_and_Run.md §8). The limit stays 1 second.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TripPerformanceTests(ApiFactory f, ITestOutputHelper output)
{
    private const int TripCount = 500_000;

    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    [Fact(Explicit = true)]
    [Trait("Category", "Performance")]
    public async Task Created_trips_list_with_500000_trips_answers_under_one_second_NFR01()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        var customer = await (await c.PostAsJsonAsync(U(o, "/customers"), new CustomerRequest("Perf Customer", null, null, null, null, null, null, null, 0, null, null, null, null, null))).Content.ReadFromJsonAsync<CustomerResponse>();
        var material = await (await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Perf Material", null, null, null))).Content.ReadFromJsonAsync<MaterialResponse>();
        var loading = await (await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Perf Loading", null, null, null))).Content.ReadFromJsonAsync<SiteResponse>();
        var unloading = await (await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Perf Unloading", null, null, null))).Content.ReadFromJsonAsync<SiteResponse>();
        var types = await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types"));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var driver = await (await c.PostAsJsonAsync(U(o, "/drivers"), new DriverRequest("Perf Driver", "9000000001", "0791234567", null, null, "L", "C", today.AddYears(1), "Cash", null, null, null,
            "99-99999", null, null, 32m, types!.Single(t => t.IsBuiltIn).Id, null, today.AddYears(1), today.AddYears(1), today.AddYears(1), null, null, null, null))).Content.ReadFromJsonAsync<DriverResponse>();

        var tokens = f.Services.GetRequiredService<IUrlTokenService>();
        var sw = Stopwatch.StartNew();
        await f.DbAsync(async db =>
        {
            async Task<long> Id<T>(string token) where T : Domain.Common.AuditableEntity
            {
                var g = tokens.Decode<T>(token)!.Value;
                return await db.Set<T>().IgnoreQueryFilters().Where(x => x.PublicId == g).Select(x => x.Id).SingleAsync();
            }
            var companyPid = tokens.Decode<Company>(o.CompanyId)!.Value;
            var company = await db.Set<Company>().IgnoreQueryFilters().SingleAsync(x => x.PublicId == companyPid);
            var (d, cu, ls, us, m) = (await Id<Driver>(driver!.Id), await Id<Customer>(customer!.Id), await Id<LoadingSite>(loading!.Id), await Id<UnloadingSite>(unloading!.Id), await Id<Material>(material!.Id));
            db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
            // 500,000 trips spread over ~3 years ending today; every 7th unbilled-and-flagged for filter variety.
            await db.Database.ExecuteSqlAsync($"""
                WITH n AS (SELECT TOP ({TripCount}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c)
                INSERT INTO trn.TRN_Trips (PublicId, TenantId, CompanyId, ReceiptNo, ReceiptNoKey, TripDate, LoadingDate, DriverId, CustomerId, LoadingSiteId, UnloadingSiteId, MaterialId,
                    TruckPlateAtSave, FleetClass, LoadedWeight, UnloadedWeight, BillingQty, WageQty, CustomerPrice, DriverRate, RateSource, RateMonth, Revenue, DriverWage, PartnerCost,
                    ShortageWeight, ShortagePct, IsShortageFlagged, IsTariffBreach, IsManualRate, NeedsReview, Status, CreatedAt, IsDeleted)
                SELECT NEWID(), {company.TenantId}, {company.Id}, CONCAT('P-', i), CONCAT('P-', i), DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)),
                    DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)), {d}, {cu}, {ls}, {us}, {m}, '99-99999', 'Own', 32.000, 31.850, 31.850, 31.850, 6.0000, 3.5000, 'Monthly',
                    FORMAT(DATEADD(day, -(i % 1100), CAST(SYSUTCDATETIME() AS date)), 'yyyy-MM'), 191.100, 111.475, 0, 0.150, 0.4688, CASE WHEN i % 7 = 0 THEN 1 ELSE 0 END, 0, 0, 0,
                    'Active', SYSUTCDATETIME(), 0
                FROM n
                """);
            return true;
        });
        output.WriteLine($"Inserted {TripCount:N0} trips in {sw.Elapsed.TotalSeconds:F1} s");

        var month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        var queries = new[]
        {
            "/trips?pageSize=50",                                                      // default list: newest first, totals of all active trips
            $"/trips?from={month}-01&to={today:yyyy-MM-dd}",                          // this month
            "/trips?shortage=true&billing=unbilled&sort=-revenue",                    // flags + another sort
            "/trips?q=499999",                                                       // search by part of a receipt number
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
    }
}
