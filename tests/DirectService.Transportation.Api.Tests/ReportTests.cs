using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Configuration;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B10 reports and dashboard: standard reports (FR-142), report builder with grouping, pivot, filters, custom fields, top-N and detail
/// (FR-143 … FR-147), saved reports and pins with thresholds (FR-140, FR-145, FR-146), dashboard figures, alerts and cards by permission
/// (FR-137 … FR-141). Ledger: three trips today — own driver 31.850 t × 6.000 (wage 3.500), own driver 29.500 t × 6.500 (tariff 6.000,
/// breach 14.750; shortage 1.67 %), partner driver 32.000 t × 6.000 (partner 2.750).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ReportTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static readonly DateOnly Now = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));

    private sealed record World(SignedInUser Owner, CustomerResponse Customer, PartnerResponse Partner, DriverResponse Driver, DriverResponse PartnerDriver, MaterialResponse Material,
        string CashBox, List<TripResponse> Trips);

    private async Task<World> WorldAsync()
    {
        var o = await f.SignUpOwnerAsync();
        var c = o.Client;
        await (await c.PostAsJsonAsync(U(o, "/accounting/accounts/template"), new { })).EnsureOkAsync();
        var box = (await c.GetFromJsonAsync<List<CashBoxResponse>>(U(o, "/accounting/cash-boxes")))!.First();
        await (await c.PostAsJsonAsync(U(o, "/custom-fields/TRN.Trip"), new CustomFieldRequest("customsNo", "رقم الكشف الجمركي", "Customs no.", "Text", null, null, null, false, false, null, false, null, null)))
            .EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/custom-fields/TRN.Trip"), new CustomFieldRequest("tolls", "رسوم الطرق", "Tolls", "Number", null, null, null, false, false, null, false, null, null)))
            .EnsureOkAsync();
        var customer = await ReadAsync<CustomerResponse>(await c.PostAsJsonAsync(U(o, "/customers"), new CustomerRequest("Arab Potash", null, null, "0781234567", null, null, null, null, 30, null, null, null, null, null)));
        var partner = await ReadAsync<PartnerResponse>(await c.PostAsJsonAsync(U(o, "/partners"), new PartnerRequest("Al-Noor Transport", null, null, null, null, null, null, null, null, null, null)));
        var material = await ReadAsync<MaterialResponse>(await c.PostAsJsonAsync(U(o, "/materials"), new MaterialRequest("Potash", null, null, null)));
        var loading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/loading-sites"), new SiteRequest("Ghor Al-Safi", null, null, null)));
        var unloading = await ReadAsync<SiteResponse>(await c.PostAsJsonAsync(U(o, "/unloading-sites"), new SiteRequest("Aqaba Port", null, null, null)));
        var types = (await c.GetFromJsonAsync<List<OwnershipTypeResponse>>(U(o, "/ownership-types")))!;
        var rented = await ReadAsync<OwnershipTypeResponse>(await c.PostAsJsonAsync(U(o, "/ownership-types"), new OwnershipTypeRequest("Rented", "External", null, null)));
        DriverRequest Driver(string name, string nid, string plate, string type, string? partnerId) =>
            new(name, nid, "0791234567", null, null, "L-1", "C", Now.AddYears(1), "Cash", null, null, null, plate, "Volvo", null, 32m, type, partnerId,
                Now.AddYears(1), Now.AddDays(10), Now.AddYears(1), null, null, null, null);   // insurance expiring in 10 days
        var own = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Ahmad Saleh", "9871234560", "10-11111", types.Single(t => t.IsBuiltIn).Id, null)));
        var ext = await ReadAsync<DriverResponse>(await c.PostAsJsonAsync(U(o, "/drivers"), Driver("Sami Abbadi", "9871234561", "10-22222", rented.Id, partner.Id)));
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Customer", customer.Id, material.Id, null, null, 6.000m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PostAsJsonAsync(U(o, "/tariffs"), new TariffRequest("Partner", partner.Id, null, null, null, 2.750m, null, null, null, null, null))).EnsureOkAsync();
        await (await c.PutAsJsonAsync(U(o, $"/monthly-rates/{Now.ToString("yyyy-MM", CultureInfo.InvariantCulture)}"),
            new MonthRatesRequest([new SiteRateRequest(loading.Id, 3.500m, 4.250m, null)]))).EnsureOkAsync();
        async Task<TripResponse> Trip(string receipt, DriverResponse driver, string? partnerId, decimal loaded, decimal unloaded, decimal? price, string customs) =>
            (await ReadAsync<TripSaveResult>(await c.PostAsJsonAsync(U(o, "/trips"), new TripRequest(receipt, Now, null, null, driver.Id, customer.Id, loading.Id, unloading.Id,
                material.Id, partnerId, loaded, unloaded, price, null, null, null,
                new Dictionary<string, JsonElement> { ["customsNo"] = JsonSerializer.SerializeToElement(customs), ["tolls"] = JsonSerializer.SerializeToElement(loaded / 10m) }, null, null)))).Trip;
        var trips = new List<TripResponse>
        {
            await Trip("R-1", own, null, 32.000m, 31.850m, null, "C-1"),
            await Trip("R-2", own, null, 30.000m, 29.500m, 6.500m, "C-1"),
            await Trip("R-3", ext, partner.Id, 32.000m, 32.000m, null, "C-2"),
        };
        return new World(o, customer, partner, own, ext, material, box.Id, trips);
    }

    private static decimal Num(object? v) => v is JsonElement e && e.ValueKind == JsonValueKind.Number ? e.GetDecimal() : throw new InvalidOperationException($"not a number: {v}");

    private static string? Str(object? v) => v is JsonElement e && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    [Fact]
    public async Task Standard_reports_compute_from_trips_and_documents_FR142()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        Assert.Equal(191.750m, w.Trips[1].Revenue);
        var catalog = await ReadAsync<List<ReportCatalogEntry>>(await o.Client.GetAsync(U(o, "/reports/catalog")));
        Assert.Equal(23, catalog.Count);                                           // RPT-01 … RPT-23 for an Owner
        Assert.Equal("/apar/ageing/receivables", catalog.Single(c => c.Code == "RPT-17").Path);
        // The customer statement cannot run without its customer: the screen checks it first (owner feedback 13 Oct 2026, RPT-12).
        Assert.Equal(["customer"], catalog.Single(c => c.Code == "RPT-12").Required);
        Assert.Equal("validation.failed", await (await o.Client.GetAsync(U(o, "/reports/standard/customer-statement"))).ProblemCodeAsync());

        var prod = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/driver-productivity")));
        var ahmad = prod.Rows.Single(r => Str(r.Values[0]) == "Ahmad Saleh");
        Assert.Equal(2m, Num(ahmad.Values[1]));
        Assert.Equal(62.000m, Num(ahmad.Values[2]));                           // tonnage = loaded weight (DT-114)
        Assert.Equal(382.850m, Num(ahmad.Values[3]));
        Assert.Equal(214.725m, Num(ahmad.Values[4]));
        Assert.Equal("trips", ahmad.Link?.Type);                                 // drill-down to the filtered trips list (FR-149)
        Assert.Equal(w.Driver.Id, ahmad.Link?.Filters?["driver"]);
        Assert.Equal(574.850m, Num(prod.Totals![3]));

        var breach = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/tariff-breach")));
        Assert.Equal("R-2", Str(Assert.Single(breach.Rows).Values[0]));
        Assert.Equal(14.750m, Num(breach.Totals![7]));
        Assert.Equal("trip", breach.Rows[0].Link?.Type);
        var shortage = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/shortage")));
        Assert.Equal(1m, Num(Assert.Single(shortage.Rows).Values[2]));
        var unbilled = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/unbilled-trips")));
        Assert.Equal(574.850m, Num(Assert.Single(unbilled.Rows).Values[2]));
        var margin = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/margin?groupBy=route")));
        Assert.Equal(272.125m, Num(margin.Totals![4]));                          // 574.850 − 214.725 − 88.000
        var monthly = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/monthly-summary")));
        Assert.Equal(3m, Num(Assert.Single(monthly.Rows).Values[1]));
        var shipments = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/partner-shipments")));
        Assert.Equal(88.000m, Num(Assert.Single(shipments.Rows).Values[5]));    // balance due
        var partners = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/partner-balances")));
        Assert.Equal(88.000m, Num(Assert.Single(partners.Rows).Values[3]));
        var drivers = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/driver-balances")));
        Assert.Equal(214.725m, Num(Assert.Single(drivers.Rows).Values[4]));
        foreach (var key in new[] { "customer-revenue", "routes", "materials" })
            Assert.NotEmpty((await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, "/reports/standard/" + key)))).Rows);

        // Customer statement: an on-account receipt is shown as unapplied.
        await (await o.Client.PostAsJsonAsync(U(o, "/receipts"), new ReceiptRequest(w.Customer.Id, Now, null, 75m, "Cash", w.CashBox, null, null, 0, null, null))).EnsureOkAsync();
        var statement = await ReadAsync<ReportResult>(await o.Client.GetAsync(U(o, $"/reports/standard/customer-statement?customer={w.Customer.Id}")));
        Assert.Equal(-75m, Num(statement.Totals![5]));
        Assert.Contains("75.000", statement.Note, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.GetAsync(U(o, "/reports/standard/customer-statement"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await o.Client.GetAsync(U(o, "/reports/standard/no-such-report"))).StatusCode);

        var xlsx = await o.Client.GetAsync(U(o, "/reports/standard/driver-productivity/export"));
        await xlsx.EnsureOkAsync();
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
        var html = await (await o.Client.GetAsync(U(o, "/reports/standard/tariff-breach/export?format=html"))).Content.ReadAsStringAsync();
        Assert.Contains("14.750", html, StringComparison.Ordinal);
    }

    private static ReportDefinition ByCustomer(params ReportMeasure[] measures) =>
        new([new ReportGrouping("customer", null)], null, measures, null, null, null, false, null);

    /// <summary>
    /// Owner feedback 13 Oct 2026 (SCR-52): Trips, a column grouping "Trip date by month" but no row grouping, measures Average of loaded
    /// weight and Count → "value of field «definition.rows» is out of range". Every refusal of a definition now names the setting as the side
    /// panel does and says what to change, in Arabic and English; the field path stays only for the screen to mark the setting.
    /// </summary>
    [Fact]
    public async Task Report_builder_refusals_name_the_setting_and_the_fix_never_an_internal_path()
    {
        var o = await f.SignUpOwnerAsync();
        async Task<(string Field, string Code, string Message)> Refused(ReportRunRequest body, string language)
        {
            // Signed-in requests speak the user's language (preferences).
            await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new Contracts.Identity.PreferencesRequest(language, null, o.Me.DateFormat))).EnsureOkAsync();
            var r = await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"), body);
            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            var error = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors")[0];
            var message = error.GetProperty("message").GetString()!;
            Assert.DoesNotContain("definition", message, StringComparison.Ordinal);
            return (error.GetProperty("field").GetString()!, error.GetProperty("code").GetString()!, message);
        }
        ReportRunRequest Run(ReportDefinition d) => new("trips", d, null, null);

        // The owner's report: a column grouping only.
        var owners = Run(new ReportDefinition([], new ReportGrouping("tripDate", "month"), [new ReportMeasure("avg", "loadedWeight"), new ReportMeasure("count", null)],
            null, null, null, false, null));
        var en = await Refused(owners, "en");
        Assert.Equal(("definition.rows", "report.rowsNone"), (en.Field, en.Code));
        Assert.Equal("Choose a field under “Row grouping” (Grouping 1), or turn on “Detail mode”. A column grouping alone is not enough.", en.Message);
        Assert.Equal("اختر حقلاً في «تجميع الصفوف» (التجميع 1)، أو شغّل «وضع التفاصيل». «تجميع الأعمدة» وحده لا يكفي.", (await Refused(owners, "ar")).Message);
        // Detail mode with the same groupings and measures runs: they are ignored there, only the columns count.
        var detail = await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"), Run(new ReportDefinition([new ReportGrouping("tripDate", "month")], null,
            [new ReportMeasure("avg", "loadedWeight"), new ReportMeasure("count", null)], null, null, null, true, ["tripDate", "loadedWeight"])));
        await detail.EnsureOkAsync();

        // Limits say the limit and what to change; settings are named with their number.
        var noColumns = await Refused(Run(new ReportDefinition(null, null, null, null, null, null, true, [])), "en");
        Assert.Equal(("definition.columns", "In “Detail mode” tick between 1 and 30 columns, or turn “Detail mode” off."), (noColumns.Field, noColumns.Message));
        Assert.Equal("Rows can be grouped by at most 2 fields. Remove the extra grouping.", (await Refused(Run(new ReportDefinition(
            [new ReportGrouping("customer", null), new ReportGrouping("driver", null), new ReportGrouping("material", null)], null, [new ReportMeasure("count", null)],
            null, null, null, false, null)), "en")).Message);
        var measure = await Refused(Run(new ReportDefinition([new ReportGrouping("customer", null)], null, [new ReportMeasure("count", null), new ReportMeasure("sum", "customer")],
            null, null, null, false, null)), "ar");
        Assert.Equal(("definition.measures[1].field", "المقياس 2: اختر حقلاً رقمياً، أو استخدم «العدد»."), (measure.Field, measure.Message));
        // A filter keeps its index as sent, also when the period replaces an earlier filter on the trip date.
        var filter = await Refused(new ReportRunRequest("trips", new ReportDefinition([new ReportGrouping("customer", null)], null, [new ReportMeasure("count", null)],
            [new ReportFilter("tripDate", "ge", ["2026-01-01"]), new ReportFilter("loadedWeight", "gt", ["heavy"])], null, null, false, null), Now, Now), "en");
        Assert.Equal("definition.filters[1].values", filter.Field);
        Assert.StartsWith("Filter 2: the value does not fit the field.", filter.Message, StringComparison.Ordinal);
        Assert.Equal("Filter 1: enter the value.", (await Refused(Run(new ReportDefinition([new ReportGrouping("customer", null)], null, [new ReportMeasure("count", null)],
            [new ReportFilter("loadedWeight", "gt", [""])], null, null, false, null)), "en")).Message);
        Assert.Equal("“Top N” must be between 1 and 5000. Change it or clear it.", (await Refused(Run(new ReportDefinition([new ReportGrouping("customer", null)], null,
            [new ReportMeasure("count", null)], null, null, 0, false, null)), "en")).Message);
        Assert.Equal("Grouping 1: choose a field from the list.", (await Refused(Run(new ReportDefinition([new ReportGrouping("nope", null)], null,
            [new ReportMeasure("count", null)], null, null, null, false, null)), "en")).Message);

        // Saving checks the same definition, and its own name with the limit.
        await (await o.Client.PutAsJsonAsync("/api/v1/me/preferences", new Contracts.Identity.PreferencesRequest("en", null, o.Me.DateFormat))).EnsureOkAsync();
        var saved = await (await o.Client.PostAsJsonAsync(U(o, "/reports/saved"), new SavedReportRequest(" ", "trips", owners.Definition, false, null))).Content.ReadAsStringAsync();
        Assert.Contains("Enter the report name (at most 150 characters).", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("«name»", saved, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_builder_groups_pivots_filters_and_lists_details_FR143_FR144()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var sources = await ReadAsync<List<DataSourceInfo>>(await o.Client.GetAsync(U(o, "/reports/builder/sources")));
        Assert.Equal(10, sources.Count);
        var trips = sources.Single(s => s.Key == "trips");
        Assert.Contains(trips.Fields, x => x.Key == "cf.customsNo" && x.Custom && x.Groupable);   // custom fields included (FR-143)

        // Revenue and count by customer.
        var grouped = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("trips", ByCustomer(new ReportMeasure("sum", "revenue"), new ReportMeasure("count", null), new ReportMeasure("avg", "loadedWeight")), null, null)));
        var row = Assert.Single(grouped.Rows);
        Assert.Equal("Arab Potash", Str(row.Values[0]));
        Assert.Equal(574.850m, Num(row.Values[1]));
        Assert.Equal(3m, Num(row.Values[2]));
        Assert.Equal(31.333m, Math.Round(Num(row.Values[3]), 3));
        Assert.Equal(w.Customer.Id, row.Link?.Filters?["customer"]);
        // Numeric custom field as a measure (TRY_CONVERT over JSON_VALUE): 3.2 + 3.0 + 3.2.
        var tolls = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("trips", ByCustomer(new ReportMeasure("sum", "cf.tolls"), new ReportMeasure("count", "unloadedWeight")), null, null)));
        Assert.Equal(9.4m, Num(tolls.Rows[0].Values[1]));
        Assert.Equal(3m, Num(tolls.Rows[0].Values[2]));

        // Custom field grouping, pivot by month, filter on the partner driver, top-N.
        var byCustoms = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"), new ReportRunRequest("trips",
            new ReportDefinition([new ReportGrouping("cf.customsNo", null)], new ReportGrouping("tripDate", "month"), [new ReportMeasure("sum", "loadedWeight")], null,
                null, null, false, null), null, null)));
        Assert.Equal(2, byCustoms.Rows.Count);
        Assert.Equal(62.000m, Num(byCustoms.Rows.Single(r => Str(r.Values[0]) == "C-1").Values[^1]));
        Assert.Contains(byCustoms.Columns, c => c.Header.StartsWith(Now.ToString("yyyy-MM", CultureInfo.InvariantCulture), StringComparison.Ordinal));
        var filtered = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"), new ReportRunRequest("trips",
            new ReportDefinition([new ReportGrouping("driver", null)], null, [new ReportMeasure("sum", "partnerCost")],
                [new ReportFilter("driver", "eq", [w.PartnerDriver.Id]), new ReportFilter("shortageFlag", "eq", ["false"])], new ReportSort("measure:0", true), 5, false, null),
            null, null)));
        Assert.Equal(88.000m, Num(Assert.Single(filtered.Rows).Values[1]));

        // Detail mode: one row per trip with totals; a void trip is left out by default.
        var trip = w.Trips[0];
        await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{trip.Id}/void"), new { reason = "Duplicate", rowVersion = trip.RowVersion })).EnsureOkAsync();
        var detail = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"), new ReportRunRequest("trips",
            new ReportDefinition(null, null, null, [new ReportFilter("loadedWeight", "ge", ["30"])], new ReportSort("receiptNo", false), null, true,
                ["receiptNo", "customer", "revenue", "tariffBreach", "cf.customsNo"]), Now, Now)));
        Assert.Equal(["R-2", "R-3"], detail.Rows.Select(r => Str(r.Values[0])).ToArray());
        Assert.Equal(383.750m, Num(detail.Totals![2]));
        Assert.Equal("trip", detail.Rows[0].Link?.Type);
        Assert.NotNull(detail.Rows[0].Link?.Id);

        // Invalid definitions.
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("trips", ByCustomer(new ReportMeasure("sum", "customer")), null, null))).StatusCode);   // not numeric
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("trips", ByCustomer(), null, null))).StatusCode);                                       // no measure
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("nope", ByCustomer(new ReportMeasure("count", null)), null, null))).StatusCode);
        var csv = await o.Client.PostAsJsonAsync(U(o, "/reports/builder/export?format=csv"), new ReportRunRequest("trips", ByCustomer(new ReportMeasure("count", null)), null, null));
        await csv.EnsureOkAsync();

        // Journal lines need acct.view (FR-147): a Dispatcher has reports.view but not the source.
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var dispatcherSources = await ReadAsync<List<DataSourceInfo>>(await dispatcher.Client.GetAsync(U(o, "/reports/builder/sources")));
        Assert.DoesNotContain(dispatcherSources, s => s.Key == "journal-lines");
        Assert.Equal(HttpStatusCode.Forbidden, (await dispatcher.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("journal-lines", new ReportDefinition([new ReportGrouping("account", null)], null, [new ReportMeasure("sum", "net")], null, null, null, false, null), null, null)))
            .StatusCode);
    }

    [Fact]
    public async Task Saved_reports_pins_and_dashboard_by_permission_FR137_FR146()
    {
        var w = await WorldAsync();
        var o = w.Owner;
        var report = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
            new SavedReportRequest("Monthly customer revenue", "trips", ByCustomer(new ReportMeasure("sum", "revenue")), true, null)));
        Assert.True(report.CanEdit);
        var run = await ReadAsync<ReportResult>(await o.Client.PostAsync(U(o, $"/reports/saved/{report.Id}/run"), null));
        Assert.Equal("Monthly customer revenue", run.Title);
        Assert.NotNull((await ReadAsync<SavedReportResponse>(await o.Client.GetAsync(U(o, $"/reports/saved/{report.Id}")))).LastRunAt);
        await (await o.Client.PostAsync(U(o, $"/reports/saved/{report.Id}/export?format=html"), null)).EnsureOkAsync();

        // Pin with thresholds (FR-140): 574.850 ≥ critical 500 → exceeded.
        var pin = await ReadAsync<DashboardPinResponse>(await o.Client.PostAsJsonAsync(U(o, "/dashboard/pins"), new DashboardPinRequest(report.Id, 0, 100m, 500m)));
        Assert.Equal(574.850m, pin.Value);
        Assert.Equal("exceeded", pin.State);
        Assert.Equal(HttpStatusCode.BadRequest, (await o.Client.PostAsJsonAsync(U(o, "/dashboard/pins"), new DashboardPinRequest(report.Id, 3, null, null))).StatusCode);
        var second = await ReadAsync<DashboardPinResponse>(await o.Client.PostAsJsonAsync(U(o, "/dashboard/pins"), new DashboardPinRequest(report.Id, 0, 1000m, null)));
        Assert.Equal("normal", second.State);
        var ordered = await ReadAsync<List<DashboardPinResponse>>(await o.Client.PostAsJsonAsync(U(o, "/dashboard/pins/order"), new PinOrderRequest([second.Id, pin.Id])));
        Assert.Equal([second.Id, pin.Id], ordered.Select(p => p.Id).ToArray());
        await (await o.Client.PostAsync(U(o, $"/dashboard/pins/{second.Id}/delete"), null)).EnsureOkAsync();

        // Dashboard (FR-137 … FR-139).
        var dash = await ReadAsync<DashboardResponse>(await o.Client.GetAsync(U(o, "/dashboard?period=today")));
        Assert.Equal(3, dash.Figures.Trips);
        Assert.Equal(94.000m, dash.Figures.Tonnage);
        Assert.Equal(574.850m, dash.Figures.Revenue);
        Assert.Equal(272.125m, dash.Figures.Margin);
        Assert.Equal(0m, dash.Figures.Invoiced);
        var unbilled = dash.Alerts.Single(a => a.Key == "unbilled-trips");
        Assert.Equal(3, unbilled.Count);
        Assert.Equal("unbilled", unbilled.Link.Filters?["billing"]);
        Assert.Contains(dash.Alerts, a => a.Key == "shortage" && a.Count == 1);
        Assert.Contains(dash.Alerts, a => a.Key == "tariff-breach" && a.Count == 1);
        Assert.Contains(dash.Alerts, a => a.Key == "driver-documents" && a.Count == 2 && a.Severity == "warning");   // insurance within 30 days
        Assert.DoesNotContain(dash.Alerts, a => a.Key == "missing-rates");
        Assert.Single(dash.CashAndBanks!);
        Assert.Equal("exceeded", Assert.Single(dash.Pins).State);

        // Shared report: a Viewer runs it but cannot edit. A Dispatcher sees the cash cards through reports.view (DT-117) but no invoiced figure.
        var viewer = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Viewer);
        var mine = await ReadAsync<PagedResult<SavedReportResponse>>(await viewer.Client.GetAsync(U(o, "/reports/saved?scope=shared")));
        Assert.False(Assert.Single(mine.Items).CanEdit);
        await (await viewer.Client.PostAsync(U(o, $"/reports/saved/{report.Id}/run"), null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.Client.PutAsJsonAsync(U(o, $"/reports/saved/{report.Id}"),
            new SavedReportRequest("Hijack", "trips", ByCustomer(new ReportMeasure("count", null)), true, report.RowVersion))).StatusCode);
        var dispatcher = await SecurityTests.InviteAndAcceptAsync(f, o, PresetRoles.Dispatcher);
        var dd = await ReadAsync<DashboardResponse>(await dispatcher.Client.GetAsync(U(o, "/dashboard")));
        Assert.Equal(3, dd.Figures.Trips);
        Assert.Null(dd.Figures.Invoiced);                                       // inv.view
        Assert.NotNull(dd.CashAndBanks);
        Assert.DoesNotContain(dd.Alerts, a => a.Key == "pending-postings");     // je.view
        Assert.Empty(dd.Pins);                                                  // pins are personal

        // A private report is invisible to others; the owner deletes it with its pins.
        var priv = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
            new SavedReportRequest("Mine only", "trips", ByCustomer(new ReportMeasure("count", null)), false, null)));
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.Client.GetAsync(U(o, $"/reports/saved/{priv.Id}"))).StatusCode);
        await (await o.Client.PostAsync(U(o, $"/reports/saved/{report.Id}/delete"), null)).EnsureOkAsync();
        Assert.Empty(await ReadAsync<List<DashboardPinResponse>>(await o.Client.GetAsync(U(o, "/dashboard/pins"))));
        var other = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.GetAsync(U(other, $"/reports/saved/{priv.Id}"))).StatusCode);
    }
}
