using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Trips;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner findings of 27 Oct 2026. DT-271: "Settled / Unsettled" means paid / not yet paid for every trip — a driver trip by its settlement, a
/// partner trip by the partner's payments allocated to its active trips oldest first (trip date, receipt number), the DT-230 rule; voided
/// trips, deductions and repayments as on the partner account; list filter, totals and export agree. DT-275: a pin offers "Number of rows"
/// and every numeric total of each data source, its value equals the report's total, and it warns above or below its thresholds.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PaidTripsTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).AddDays(-1);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<TripPage> ListAsync(SignedInUser o, string query) => await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?pageSize=200&" + query)));

    private static async Task<TripResponse> GetAsync(SignedInUser o, TripResponse t) => (await ReadAsync<TripDetails>(await o.Client.GetAsync(U(o, $"/trips/{t.Id}")))).Trip;

    private static async Task<PartnerPaymentDetails> PayAsync(CashTests.World w, decimal amount, string? kind = null) =>
        await ReadAsync<PartnerPaymentDetails>(await w.Owner.Client.PostAsJsonAsync(U(w.Owner, "/partner-payments"),
            new PartnerPaymentRequest(w.Partner.Id, Today, amount, "Bank", null, w.Bank, "TRF-" + Guid.NewGuid().ToString("N")[..6], null, null, kind)));

    private static async Task VoidAsync(SignedInUser o, TripResponse t, string? handling) =>
        await (await o.Client.PostAsJsonAsync(U(o, $"/trips/{t.Id}/void"), new TripVoidRequest("Load cancelled by the customer", (await GetAsync(o, t)).RowVersion, handling))).EnsureOkAsync();

    private static async Task<PartnerAccountResponse> AccountAsync(CashTests.World w) =>
        await ReadAsync<PartnerAccountResponse>(await w.Owner.Client.GetAsync(U(w.Owner, $"/partners/{w.Partner.Id}/account")));

    /// <summary>The open parts of the partner's active trips add up to what the company still owes the partner (the account's balance due).</summary>
    private static async Task AssertOpenEqualsBalanceAsync(CashTests.World w)
    {
        var trips = (await ListAsync(w.Owner, $"partner={w.Partner.Id}")).Page.Items;
        var account = await AccountAsync(w);
        Assert.Equal(Math.Max(0, account.BalanceDue), trips.Sum(t => t.PartnerOpen ?? 0));
    }

    // ------------------------------------------------------------------ DT-271

    [Fact]
    public async Task An_own_fleet_trip_is_settled_once_it_is_in_a_driver_settlement()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var settled = await w.Trip("OWN-1", w.Driver, null);
        await (await o.Client.PostAsJsonAsync(U(o, "/settlements"), new SettlementRequest(w.Driver.Id, Today, [settled.Id], [], [], "Cash", w.CashBox, null, null, null))).EnsureOkAsync();
        var open = await w.Trip("OWN-2", w.Driver, null);

        var yes = await ListAsync(o, $"driver={w.Driver.Id}&settled=yes");
        var no = await ListAsync(o, $"driver={w.Driver.Id}&settled=no");
        Assert.Equal(["OWN-1"], yes.Page.Items.Select(t => t.ReceiptNo));
        Assert.Equal(["OWN-2"], no.Page.Items.Select(t => t.ReceiptNo));
        Assert.Equal(("Settled", true), (yes.Page.Items[0].Payment, yes.Page.Items[0].IsSettled));
        Assert.NotNull(yes.Page.Items[0].Settlement);
        Assert.Equal(("Unsettled", false), (no.Page.Items[0].Payment, no.Page.Items[0].IsSettled));
        Assert.Null((await GetAsync(o, open)).PartnerOpen);
        // Partner trips are never driver-settled; with "partner trips" the own-fleet trips drop out of either filter.
        Assert.Empty((await ListAsync(o, $"driver={w.Driver.Id}&settled=no&partnerTrips=yes")).Page.Items);
    }

    [Fact]
    public async Task Partner_trips_are_paid_oldest_first_fully_partly_or_not_and_the_filter_totals_equal_the_rows()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var b1 = await w.Trip("PB-1", w.PartnerDriver, w.Partner.Id);
        var b2 = await w.Trip("PB-2", w.PartnerDriver, w.Partner.Id);
        var b3 = await w.Trip("PB-3", w.PartnerDriver, w.Partner.Id);
        // Entered last but one day older: the partner's payments cover it first (trip date before receipt number).
        var old = (await ReadAsync<TripSaveResult>(await o.Client.PostAsJsonAsync(U(o, "/trips"), new TripRequest("PZ-OLD", Today.AddDays(-1), null, null, w.PartnerDriver.Id,
            w.Customer.Id, b1.LoadingSite.Id, b1.UnloadingSite.Id, b1.Material.Id, w.Partner.Id, 32.000m, 31.850m, null, null, null, null, null, null, null)))).Trip;
        var c = b1.PartnerCost;
        Assert.True(c > 0);
        Assert.Equal("PartnerUnpaid", (await GetAsync(o, b1)).Payment);

        await PayAsync(w, 2 * c + 43.794m);   // PZ-OLD and PB-1 in full, 43.794 of PB-2

        Assert.Equal(("PartnerPaid", 0m), ((await GetAsync(o, old)).Payment, (await GetAsync(o, old)).PartnerOpen));
        Assert.Equal("PartnerPaid", (await GetAsync(o, b1)).Payment);
        var partly = await GetAsync(o, b2);
        Assert.Equal(("PartnerPartlyPaid", c - 43.794m, false), (partly.Payment, partly.PartnerOpen, partly.IsSettled));
        Assert.Equal(("PartnerUnpaid", c), ((await GetAsync(o, b3)).Payment, (await GetAsync(o, b3)).PartnerOpen));

        // "Partner trips" + "Unsettled": the partly paid and the unpaid trip; the totals are those of the listed rows.
        var open = await ListAsync(o, "settled=no&partnerTrips=yes");
        Assert.Equal(["PB-2", "PB-3"], open.Page.Items.Select(t => t.ReceiptNo).Order());
        Assert.Equal(open.Page.Items.Count, open.Totals.Trips);
        Assert.Equal(open.Page.Items.Count, open.Page.TotalCount);
        Assert.Equal(open.Page.Items.Sum(t => t.PartnerCost), open.Totals.PartnerCost);
        Assert.Equal(open.Page.Items.Sum(t => t.Revenue), open.Totals.Revenue);
        Assert.Equal(open.Page.Items.Sum(t => t.LoadedWeight), open.Totals.LoadedWeight);
        var paid = await ListAsync(o, $"settled=yes&partner={w.Partner.Id}");
        Assert.Equal(["PB-1", "PZ-OLD"], paid.Page.Items.Select(t => t.ReceiptNo).Order());
        Assert.Equal(paid.Page.Items.Sum(t => t.PartnerCost), paid.Totals.PartnerCost);
        // Paging keeps the filter: page 2 of 1 row each.
        var page2 = await ReadAsync<TripPage>(await o.Client.GetAsync(U(o, "/trips?settled=no&partnerTrips=yes&pageSize=1&page=2&sort=receiptNo")));
        Assert.Equal((2, "PB-3"), (page2.Page.TotalCount, page2.Page.Items.Single().ReceiptNo));
        await AssertOpenEqualsBalanceAsync(w);

        // Export: the same rows with the invoice, status and open columns.
        var csv = await (await o.Client.GetAsync(U(o, "/trips/export?format=csv&settled=no&partnerTrips=yes"))).Content.ReadAsStringAsync();
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Settlement / payment", lines[0], StringComparison.Ordinal);
        Assert.Contains("Open to partner", lines[0], StringComparison.Ordinal);
        Assert.Contains("Fleet type", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("PB-2", StringComparison.Ordinal) && l.Contains("Partly paid", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("PB-3", StringComparison.Ordinal) && l.Contains("Not paid to partner", StringComparison.Ordinal) && l.Contains("Partner", StringComparison.Ordinal));
        await (await o.Client.GetAsync(U(o, "/trips/export?format=pdf&settled=no&partnerTrips=yes"))).EnsureOkAsync();

        // The report builder's "Settled / paid" field uses the same rule.
        var run = await ReadAsync<ReportResult>(await o.Client.PostAsJsonAsync(U(o, "/reports/builder/run"),
            new ReportRunRequest("trips", new ReportDefinition(null, null, null, null, null, null, true, ["receiptNo", "settled", "partnerOpen"]), null, null)));
        var byReceipt = run.Rows.ToDictionary(r => r.Values[0]!.ToString()!, r => (Settled: r.Values[1]!.ToString(), Open: decimal.Parse(r.Values[2]!.ToString()!, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(("Yes", 0m), byReceipt["PB-1"]);
        Assert.Equal(("No", c - 43.794m), byReceipt["PB-2"]);
    }

    [Fact]
    public async Task A_paid_trip_voided_with_deduct_then_newer_trips_and_a_voided_payment_follow_the_partner_balance()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var p1 = await w.Trip("PD-1", w.PartnerDriver, w.Partner.Id);
        var p2 = await w.Trip("PD-2", w.PartnerDriver, w.Partner.Id);
        var c = p1.PartnerCost;
        await PayAsync(w, 2 * c);
        Assert.Equal("PartnerPaid", (await GetAsync(o, p2)).Payment);

        // PD-1 voided after payment, its amount deducted from the next payment(s) (DT-230): PD-2 stays paid; a voided trip is not paid.
        await VoidAsync(o, p1, "Deduct");
        var voided = await GetAsync(o, p1);
        Assert.Equal((null, false), (voided.Payment, voided.IsSettled));
        Assert.Equal("PartnerPaid", (await GetAsync(o, p2)).Payment);
        Assert.DoesNotContain((await ListAsync(o, "settled=yes&partnerTrips=yes&status=all")).Page.Items, t => t.ReceiptNo == "PD-1");
        // A newer trip absorbs what was paid for PD-1 (the partner's balance is 0 again): paid.
        var p3 = await w.Trip("PD-3", w.PartnerDriver, w.Partner.Id);
        Assert.Equal("PartnerPaid", (await GetAsync(o, p3)).Payment);
        Assert.Equal(0m, (await AccountAsync(w)).BalanceDue);
        // The next one is unpaid until the next payment, which records the deduction and pays it.
        var p4 = await w.Trip("PD-4", w.PartnerDriver, w.Partner.Id);
        Assert.Equal("PartnerUnpaid", (await GetAsync(o, p4)).Payment);
        await AssertOpenEqualsBalanceAsync(w);
        var next = await PayAsync(w, c);
        Assert.Equal("PartnerPaid", (await GetAsync(o, p4)).Payment);
        await AssertOpenEqualsBalanceAsync(w);

        // That payment voided: PD-4 is unpaid again.
        await (await o.Client.PostAsJsonAsync(U(o, $"/partner-payments/{next.Payment.Id}/void"), new CashVoidRequest("Entered twice", next.Payment.RowVersion))).EnsureOkAsync();
        Assert.Equal("PartnerUnpaid", (await GetAsync(o, p4)).Payment);
        await AssertOpenEqualsBalanceAsync(w);
    }

    [Fact]
    public async Task A_paid_trip_voided_with_return_and_the_partner_repayment_keep_the_trips_paid_as_the_balance_says()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        var p1 = await w.Trip("PR-1", w.PartnerDriver, w.Partner.Id);
        var p2 = await w.Trip("PR-2", w.PartnerDriver, w.Partner.Id);
        var c = p1.PartnerCost;
        await PayAsync(w, 2 * c);
        await VoidAsync(o, p1, "Return");
        Assert.Equal(-c, (await AccountAsync(w)).BalanceDue);   // the partner owes the company
        Assert.Equal("PartnerPaid", (await GetAsync(o, p2)).Payment);
        Assert.Empty((await ListAsync(o, "settled=no&partnerTrips=yes")).Page.Items);

        // The partner repays: payments net c, still enough for PR-2.
        await PayAsync(w, c, "Repayment");
        Assert.Equal(0m, (await AccountAsync(w)).BalanceDue);
        Assert.Equal("PartnerPaid", (await GetAsync(o, p2)).Payment);
        // A newer trip is owed in full.
        var p3 = await w.Trip("PR-3", w.PartnerDriver, w.Partner.Id);
        Assert.Equal(("PartnerUnpaid", c), ((await GetAsync(o, p3)).Payment, (await GetAsync(o, p3)).PartnerOpen));
        Assert.Equal(["PR-3"], (await ListAsync(o, "settled=no&partnerTrips=yes")).Page.Items.Select(t => t.ReceiptNo));
        await AssertOpenEqualsBalanceAsync(w);
    }

    // ------------------------------------------------------------------ DT-275

    [Fact]
    public async Task Every_data_source_offers_number_of_rows_and_its_numeric_totals_for_a_pin()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        await w.Trip("PM-1", w.Driver, null);
        var sources = await ReadAsync<List<DataSourceInfo>>(await o.Client.GetAsync(U(o, "/reports/builder/sources")));
        Assert.NotEmpty(sources);
        foreach (var s in sources)
        {
            var numeric = s.Fields.Where(x => x.Measurable).Take(4).Select(x => x.Key).ToList();
            var columns = new[] { s.Fields[0].Key }.Concat(numeric).Distinct().ToList();
            var detail = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
                new SavedReportRequest("Detail " + s.Key, s.Key, new ReportDefinition(null, null, null, null, null, null, true, columns), false, null)));
            var options = await ReadAsync<List<PinMeasureOption>>(await o.Client.GetAsync(U(o, $"/reports/saved/{detail.Id}/pin-measures")));
            Assert.Equal(("rows", "Number of rows", "number"), (options[0].Key, options[0].Label, options[0].Kind));
            Assert.NotNull(options[0].Value);
            Assert.Equal(numeric.Select(k => "sum:" + k), options.Skip(1).Select(x => x.Key));
            Assert.All(options.Skip(1), x => Assert.StartsWith("Sum of ", x.Label, StringComparison.Ordinal));

            var group = s.Fields.First(x => x.Groupable);
            var measures = new List<ReportMeasure> { new("count", null) };
            if (numeric.Count > 0) measures.Add(new("sum", numeric[0]));
            var summary = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
                new SavedReportRequest("Summary " + s.Key, s.Key, new ReportDefinition([new ReportGrouping(group.Key, null)], null, measures, null, null, null, false, null), false, null)));
            var summaryOptions = await ReadAsync<List<PinMeasureOption>>(await o.Client.GetAsync(U(o, $"/reports/saved/{summary.Id}/pin-measures")));
            // The plain count is the number of rows already; the sum keeps its measure index.
            Assert.Equal(numeric.Count > 0 ? ["rows", "1"] : ["rows"], summaryOptions.Select(x => x.Key));
        }
    }

    [Fact]
    public async Task A_pinned_value_equals_the_reports_total_and_warns_above_or_below_its_thresholds()
    {
        var w = await CashTests.WorldAsync(f);
        var o = w.Owner;
        await w.Trip("PV-1", w.Driver, null);
        await w.Trip("PV-2", w.Driver, null);
        var report = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
            new SavedReportRequest("Trips detail", "trips", new ReportDefinition(null, null, null, null, null, null, true, ["receiptNo", "loadedWeight", "revenue"]), false, null)));
        var run = await ReadAsync<ReportResult>(await o.Client.PostAsync(U(o, $"/reports/saved/{report.Id}/run"), null));
        var revenueTotal = decimal.Parse(run.Totals![2]!.ToString()!, System.Globalization.CultureInfo.InvariantCulture);
        Task<HttpResponseMessage> Pin(string key, decimal? warning, decimal? critical, string? warnWhen) =>
            o.Client.PostAsJsonAsync(U(o, "/dashboard/pins"), new DashboardPinRequest(report.Id, null, warning, critical, key, warnWhen));

        var revenue = await ReadAsync<DashboardPinResponse>(await Pin("sum:revenue", null, null, null));
        Assert.Equal((revenueTotal, "Sum of Revenue", "money", "Above", "normal"), (revenue.Value!.Value, revenue.MeasureLabel, revenue.ValueKind, revenue.WarnWhen, revenue.State));
        // Two rows: below 5 → warning, not below 1 → not exceeded.
        var rows = await ReadAsync<DashboardPinResponse>(await Pin("rows", 5m, 1m, "Below"));
        Assert.Equal((run.Rows.Count, "Number of rows", "number", "Below", "warning"), ((int)rows.Value!.Value, rows.MeasureLabel, rows.ValueKind, rows.WarnWhen, rows.State));
        // Above: 2 reaches the critical 2 → exceeded.
        var above = await ReadAsync<DashboardPinResponse>(await Pin("rows", 1m, 2m, "Above"));
        Assert.Equal("exceeded", above.State);
        // Changing the direction recalculates the state.
        var changed = await ReadAsync<DashboardPinResponse>(await o.Client.PutAsJsonAsync(U(o, $"/dashboard/pins/{above.Id}"), new DashboardPinRequest(null, null, 1m, 0m, null, "Below")));
        Assert.Equal(("Below", "normal"), (changed.WarnWhen, changed.State));

        // Refusals: the critical threshold on the wrong side for the direction, an unknown measure, an unknown direction.
        var wrongBelow = await Pin("rows", 1m, 5m, "Below");
        Assert.Equal((HttpStatusCode.BadRequest, "report.thresholdsBelow"), (wrongBelow.StatusCode, await wrongBelow.ErrorCodeAsync()));
        var wrongAbove = await Pin("rows", 5m, 1m, "Above");
        Assert.Equal((HttpStatusCode.BadRequest, "report.thresholds"), (wrongAbove.StatusCode, await wrongAbove.ErrorCodeAsync()));
        var unknown = await Pin("sum:nothing", null, null, null);
        Assert.Equal((HttpStatusCode.BadRequest, "report.measure"), (unknown.StatusCode, await unknown.ErrorCodeAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Pin("rows", null, null, "Sideways")).StatusCode);
        // Both thresholds are optional on either side.
        await (await Pin("rows", null, 3m, "Below")).EnsureOkAsync();

        // The dashboard shows the same values; an older pin (measure index of a summary report, no direction) still works as "Above".
        var summary = await ReadAsync<SavedReportResponse>(await o.Client.PostAsJsonAsync(U(o, "/reports/saved"),
            new SavedReportRequest("Revenue by customer", "trips", new ReportDefinition([new ReportGrouping("customer", null)], null, [new ReportMeasure("sum", "revenue")], null, null, null, false, null), false, null)));
        var old = await ReadAsync<DashboardPinResponse>(await o.Client.PostAsJsonAsync(U(o, "/dashboard/pins"), new DashboardPinRequest(summary.Id, 0, 100m, 200m)));
        Assert.Equal((revenueTotal, "0", "Above", "exceeded"), (old.Value!.Value, old.MeasureKey, old.WarnWhen, old.State));
        var dash = await ReadAsync<DashboardResponse>(await o.Client.GetAsync(U(o, "/dashboard")));
        Assert.Equal(revenueTotal, dash.Pins.Single(p => p.Id == revenue.Id).Value);
        Assert.Equal(2m, dash.Pins.Single(p => p.Id == rows.Id).Value);

        // Unpin.
        await (await o.Client.PostAsync(U(o, $"/dashboard/pins/{revenue.Id}/delete"), null)).EnsureOkAsync();
        Assert.DoesNotContain((await ReadAsync<List<DashboardPinResponse>>(await o.Client.GetAsync(U(o, "/dashboard/pins")))), p => p.Id == revenue.Id);
    }
}
