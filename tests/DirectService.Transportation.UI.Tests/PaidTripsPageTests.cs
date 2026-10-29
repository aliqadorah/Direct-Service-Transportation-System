using System.Text.Json;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Client.Pages.Reports;
using DirectService.Transportation.Web.Client.Pages.Trips;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner findings of 27 Oct 2026: the settlement / payment badges of a trip (DT-272), the optional columns of the Created Trips list with
/// Partner shown by default (DT-273), and the pin dialog listing the report's measures with "warn when" and threshold checks (DT-275).
/// </summary>
public sealed class PaidTripsPageTests : PageTestBase
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static TripResponse Trip(string id, string receipt, string? payment, decimal? open = null, bool partner = true, string? invoice = null) =>
        new(id, receipt, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 12), null, new Ref("d1", "سامي العبادي"), "10-22222", new Ref("cu1", "شركة البوتاس العربية"),
            new Ref("l1", "غور الصافي"), new Ref("u1", "ميناء العقبة"), new Ref("m1", "بوتاس"), partner ? new Ref("p1", "النور للنقل") : null, partner ? "External" : "Own",
            32m, 31.85m, 31.85m, 31.85m, 6m, 6m, 0m, partner ? 0m : 3.5m, "Monthly", "2026-10", partner ? 2.75m : null, 191.1m, partner ? 0m : 111.475m, partner ? 87.588m : 0m,
            partner ? 103.512m : 79.625m, 0.15m, 0.47m, false, false, false, false, null, "Active", invoice is null ? "Unbilled" : "Invoiced",
            payment is "Settled" or "PartnerPaid", false, invoice is null ? null : new DocRef("i1", invoice), payment == "Settled" ? new DocRef("s1", "STL-00001") : null, null, false,
            null, partner ? "حمولة ثانية" : null, new Dictionary<string, JsonElement>(), DateTime.UtcNow, "AAAA", payment, open);

    // ------------------------------------------------------------------ status badges (DT-272)

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Status_badges_show_settled_paid_to_partner_and_partly_paid_with_the_open_amount(string language)
    {
        L.SetLanguage(language);
        IRenderedComponent<TripTags> Tags(TripResponse t) => Render<TripTags>(p => p.Add(x => x.Trip, t));

        var settled = Tags(Trip("t1", "R-1", "Settled", partner: false));
        Assert.Contains(L["st.settled"], Text(settled));
        var paid = Tags(Trip("t2", "R-2", "PartnerPaid", 0m));
        Assert.Contains(L["st.partnerPaid"], Text(paid));
        Assert.NotNull(paid.Find(".badge.b-ok"));
        var partly = Tags(Trip("t3", "R-3", "PartnerPartlyPaid", 12.5m));
        Assert.Contains(L.F("trip.partlyOpen", Fmt.Money(12.5m)), Text(partly));
        Assert.NotNull(partly.Find(".badge.b-warn"));
        // In the status column the unpaid states add nothing; the "Settlement / payment" column names them.
        var unpaid = Trip("t4", "R-4", "PartnerUnpaid", 87.588m);
        Assert.DoesNotContain(L["st.partnerUnpaid"], Text(Tags(unpaid)));
        Assert.Contains(L["st.partnerUnpaid"], Text(Render<TripPaymentTag>(p => p.Add(x => x.Trip, unpaid).Add(x => x.All, true))));
        Assert.Contains(L["st.unsettled"], Text(Render<TripPaymentTag>(p => p.Add(x => x.Trip, Trip("t5", "R-5", "Unsettled", partner: false)).Add(x => x.All, true))));
        // Every tag's text has its words in both languages (light and dark use the same token classes).
        foreach (var key in new[] { "st.partnerPaid", "st.partlyPaid", "st.partnerUnpaid", "st.unsettled", "trip.partlyOpen", "trip.payment" })
            Assert.False(string.IsNullOrWhiteSpace(L[key]) || L[key] == key, key);
    }

    // ------------------------------------------------------------------ columns (DT-273)

    private IRenderedComponent<TripListView> TripList()
    {
        Respond("column-views/TRN.Trips", new ColumnViewResponse("TRN.Trips", "BuiltIn", [], null));
        Respond("trips", new TripPage(new PagedResult<TripResponse>([Trip("t2", "9496", "PartnerPaid", 0m, invoice: "INV-00004"), Trip("t1", "R-1", "Settled", partner: false)], 1, 50, 2),
            new TripTotals(2, 64m, 63.7m, 382.2m, 111.475m, 87.588m, 183.137m)));
        return Render<TripListView>(p => p.Add(x => x.Company, Company));
    }

    private static List<string> Headers(IRenderedComponent<TripListView> cut) => cut.FindAll("thead th").Select(th => th.TextContent.Trim()).Where(t => t.Length > 0).ToList();

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_partner_column_shows_by_default_and_can_be_moved_shown_and_saved(string language)
    {
        L.SetLanguage(language);
        var cut = TripList();
        var headers = Headers(cut);
        Assert.Contains(L["md.partner"], headers);
        Assert.DoesNotContain(L["trip.invoiceNo"], headers);   // optional columns start hidden
        Assert.DoesNotContain(L["trip.payment"], headers);
        Assert.Contains("النور للنقل", Text(cut));

        Respond("column-views/TRN.Trips", r => new ColumnViewResponse("TRN.Trips", "Personal",
            JsonSerializer.Deserialize<ColumnViewRequest>(r.Content!.ReadAsStringAsync().Result, Web)!.Columns!, null));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["cols.title"], StringComparison.Ordinal)).Click());
        var rows = cut.FindAll("li label[for^='col-']").Select(l => l.TextContent).ToList();
        Assert.Equal(L["md.partner"], rows[5]);   // after the customer, in the default order
        // Move Partner up above the customer, show "Invoice no." and "Settlement / payment", save for me.
        Act(() => cut.Find($"button[aria-label='{L.F("lay.up", L["md.partner"])}']").Click());
        Act(() => cut.Find("#col-TRN\\.Trips-invoice").Change(true));
        Act(() => cut.Find("#col-TRN\\.Trips-payment").Change(true));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["cols.saveMine"], StringComparison.Ordinal)).Click());

        var put = Requests.Last(r => r.Method == "PUT" && r.Path.StartsWith("column-views/TRN.Trips", StringComparison.Ordinal));
        var sent = JsonSerializer.Deserialize<ColumnViewRequest>(put.Body!, Web)!.Columns!.Select(c => c.Key).ToList();
        Assert.True(sent.IndexOf("partner") < sent.IndexOf("customer"));
        Assert.Contains("invoice", sent);
        Assert.Contains("payment", sent);
        Assert.DoesNotContain("notes", sent);
        cut.WaitForAssertion(() =>
        {
            var shown = Headers(cut);
            Assert.True(shown.IndexOf(L["md.partner"]) < shown.IndexOf(L["trip.customer"]));
            Assert.Contains(L["trip.invoiceNo"], shown);
            Assert.Contains(L["trip.payment"], shown);
        });
        Assert.Contains("INV-00004", Text(cut));
        Assert.Contains(L["st.partnerPaid"], Text(cut));
        // The invoice number links to the invoice.
        Assert.EndsWith($"/c/{Company}/invoices/i1", cut.FindAll("a").First(a => a.TextContent == "INV-00004").GetAttribute("href"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ pin dialog (DT-275)

    private IRenderedComponent<SavedReports> PinDialog()
    {
        var definition = new ReportDefinition(null, null, null, null, null, null, true, ["tripDate", "revenue", "partnerCost"]);
        Respond("reports/builder/sources", Array.Empty<object>());
        Respond("dashboard/pins", Array.Empty<object>());
        Respond("reports/saved", new PagedResult<SavedReportResponse>([new SavedReportResponse("r1", "رحلات الشهر", "trips", definition, false, new Ref("u1", "Test User"), true, false, null,
            DateTime.UtcNow, "AAAA")], 1, 50, 1));
        Respond("reports/saved/r1/pin-measures", new[]
        {
            new PinMeasureOption("rows", "Number of rows", "number", 14m), new PinMeasureOption("sum:revenue", "Sum of Revenue", "money", 2675.4m),
            new PinMeasureOption("sum:partnerCost", "Sum of Partner cost", "money", 475.475m),
        });
        var cut = Render<SavedReports>(p => p.Add(x => x.Company, Company));
        Act(() => cut.Find($"button[aria-label='{L.F("sr.pinOf", "رحلات الشهر")}']").Click());
        return cut;
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_pin_dialog_lists_rows_and_every_total_and_previews_the_card(string language)
    {
        L.SetLanguage(language);
        var cut = PinDialog();
        cut.WaitForAssertion(() => Assert.Equal(["Number of rows", "Sum of Revenue", "Sum of Partner cost"], cut.FindAll("#pin-measure option").Select(o => o.TextContent).ToList()));
        Assert.Equal([L["sr.warnAbove"], L["sr.warnBelow"]], cut.FindAll("#pin-direction option").Select(o => o.TextContent).ToList());
        Assert.Contains(L["sr.preview"], Text(cut));
        Assert.Contains("14", cut.Find(".pin-preview .pin-card b").TextContent);
        // Sum of revenue, warn below 3,000 → the preview turns to a warning.
        Act(() => cut.Find("#pin-measure").Change("1"));
        Act(() => cut.Find("#pin-direction").Change("1"));
        Act(() => cut.Find("#pin-warning").Change("3000"));
        Assert.Equal(Fmt.Money(2675.4m), cut.Find(".pin-preview .pin-card b").TextContent);
        Assert.Equal("warning", cut.Find(".pin-preview .pin-card").GetAttribute("data-state"));
        Assert.Contains(L.F("dash.pinWarningBelow", Fmt.Money(3000m)), Text(cut));
    }

    [Fact]
    public void Thresholds_must_be_numbers_and_the_critical_one_beyond_the_warning_in_the_chosen_direction()
    {
        L.SetLanguage("en");
        var cut = PinDialog();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#pin-measure option")));
        Respond("dashboard/pins", r => r.Method == HttpMethod.Post ? new { } : Array.Empty<object>());
        void PinClick() => Act(() => cut.FindAll(".dialog .df button").Last(b => b.TextContent.Contains(L["sr.pin"], StringComparison.Ordinal)).Click());
        int Posts() => Requests.Count(r => r.Method == "POST" && r.Path.StartsWith("dashboard/pins", StringComparison.Ordinal));

        // Not a number: the field says so and nothing is sent.
        Act(() => cut.Find("#pin-warning").Change("abc"));
        Assert.Contains(L.F("ui.notANumber", L["sr.warning"]), Text(cut));
        PinClick();
        Assert.Equal(0, Posts());

        // Above: critical below the warning is refused on the critical field.
        Act(() => cut.Find("#pin-warning").Change("100"));
        Act(() => cut.Find("#pin-critical").Change("50"));
        Assert.Contains(L["sr.critAbove"], Text(cut));
        PinClick();
        Assert.Equal(0, Posts());

        // Below: the same numbers are right (critical 50 is beyond warning 100 going down) and are sent with the measure key and direction.
        Act(() => cut.Find("#pin-direction").Change("1"));
        Assert.DoesNotContain(L["sr.critAbove"], Text(cut));
        Assert.DoesNotContain(L["sr.critBelow"], Text(cut));
        Act(() => cut.Find("#pin-measure").Change("2"));
        PinClick();
        var body = Requests.Last(r => r.Method == "POST" && r.Path.StartsWith("dashboard/pins", StringComparison.Ordinal)).Body!;
        var sent = JsonSerializer.Deserialize<DashboardPinRequest>(body, Web)!;
        Assert.Equal(("r1", "sum:partnerCost", "Below", 100m, 50m), (sent.SavedReportId, sent.MeasureKey, sent.WarnWhen, sent.WarningThreshold, sent.CriticalThreshold));

        // Both thresholds are optional.
        Assert.True(PinStates.Ordered(null, 5m, "Above") && PinStates.Ordered(5m, null, "Below"));
    }
}
