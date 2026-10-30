using System.Net;
using System.Text;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Web.Client.Pages.Reports;
using DirectService.Shared.UI.Pages.Reports;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Owner feedback 13 Oct 2026 (RPT-12): a report's required filters are checked before loading and marked on their field; one message.</summary>
public sealed class ReportPageTests : PageTestBase
{
    private static readonly ReportCatalogEntry Statement = new("customer-statement", "RPT-12", "financial", "كشف حساب عميل", null, ["customer", "from", "to"], ["customer"]);
    private static readonly ReportCatalogEntry Balances = new("driver-balances", "RPT-19", "financial", "أرصدة السائقين", null, ["asOf"], []);

    [Fact]
    public void Customer_statement_waits_for_its_customer_and_marks_the_field_without_asking_the_server()
    {
        Respond("reports/catalog", new[] { Statement, Balances });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/reports/financial?report=customer-statement");
        var cut = Render<ReportsCatalog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["rep.chooseFilters"], cut.Markup));
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("reports/standard", StringComparison.Ordinal));

        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["rep.run"]).Click());
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("input[role=combobox]").GetAttribute("aria-invalid")));
        Assert.Contains(L.F("ui.requiredField", L["trip.customer"]), Text(cut));
        Assert.DoesNotContain(L["ui.loadFailed"], cut.Markup);
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("reports/standard", StringComparison.Ordinal));
        Assert.Empty(Services.GetRequiredService<ToastService>().Items);
    }

    /// <summary>Owner decision 17 Oct 2026: the supplier statement (RPT-24) has a supplier picker, required before it runs.</summary>
    [Fact]
    public void Supplier_statement_asks_for_its_supplier()
    {
        var supplierStatement = new ReportCatalogEntry("supplier-statement", "RPT-24", "financial", "كشف حساب مورد", null, ["supplier", "from", "to"], ["supplier"]);
        Respond("reports/catalog", new[] { supplierStatement });
        Respond("apar/parties/options", new[] { new Contracts.ApAr.ApArPartyOption("Supplier", "s1", "المناصير", null, 30) });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/reports/financial?report=supplier-statement");
        var cut = Render<ReportsCatalog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["rep.supplier"], cut.Markup));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["rep.run"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("ui.requiredField", L["rep.supplier"]), Text(cut)));
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("reports/standard", StringComparison.Ordinal));
    }

    [Fact]
    public void A_filter_refused_by_the_server_is_marked_on_its_field_not_shown_as_could_not_load()
    {
        ResponseDelay = TimeSpan.FromMilliseconds(30);   // as over a network: the filters are on screen when the answer comes
        Respond("reports/catalog", new[] { Statement, Balances });
        Respond("reports/standard/driver-balances", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"status\":400,\"code\":\"validation.failed\",\"detail\":\"Please correct the highlighted fields.\",\"errors\":[{\"field\":\"asOf\",\"code\":\"validation.range\",\"message\":\"As of is out of range.\"}]}",
                Encoding.UTF8, "application/problem+json"),
        });
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/reports/financial?report=driver-balances");
        var cut = Render<ReportsCatalog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("As of is out of range.", Text(cut)));
        Assert.DoesNotContain(L["ui.loadFailed"], cut.Markup);
        Assert.Empty(Services.GetRequiredService<ToastService>().Items);
    }

    [Fact]
    public void The_same_toast_is_shown_once()
    {
        var toasts = Services.GetRequiredService<ToastService>();
        toasts.Error("Please correct the highlighted fields.");
        toasts.Error("Please correct the highlighted fields.");
        toasts.Error("Please correct the highlighted fields.");
        Assert.Single(toasts.Items);
        toasts.Error("Another problem.");
        Assert.Equal(2, toasts.Items.Count);
    }

    // ------------------------------------------------------------ report builder (owner feedback 13 Oct 2026, SCR-52)

    private static readonly DataSourceInfo TripsSource = new("trips", "الرحلات", [
        new SourceField("receiptNo", "رقم الإيصال", "text", true, false, false),
        new SourceField("tripDate", "تاريخ الرحلة", "date", true, false, false),
        new SourceField("loadedWeight", "الوزن المحمّل", "quantity", false, true, false)]);

    private static readonly ReportResult DetailRows = new("trips", "الرحلات", [], [new ResultColumn("receiptNo", "رقم الإيصال", "text")],
        [new ResultRow(["R-1"], null), new ResultRow(["R-2"], null)], null, null, false);

    private IRenderedComponent<ReportBuilder> Builder()
    {
        Respond("reports/builder/sources", new[] { TripsSource });
        var cut = Render<ReportBuilder>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => cut.Find("select"));
        Act(() => cut.Find("select").Change("0"));
        return cut;
    }

    /// <summary>The select or input whose label reads <paramref name="label"/> (the first one).</summary>
    private static AngleSharp.Dom.IElement Control(IRenderedComponent<ReportBuilder> cut, string label) =>
        cut.Find("#" + cut.FindAll("label[for]").First(l => l.TextContent.Trim().TrimEnd('*').Trim() == label).GetAttribute("for"));

    private void Click(IRenderedComponent<ReportBuilder> cut, string text) => Act(() => cut.FindAll("button").First(b => b.TextContent.Trim() == text).Click());

    private int Runs => Requests.Count(r => r.Path == "reports/builder/run");

    [Fact]
    public void Builder_with_only_a_column_grouping_is_stopped_on_the_screen_and_the_earlier_result_is_marked_not_up_to_date()
    {
        Respond("reports/builder/run", DetailRows);
        var cut = Builder();
        // A detail report first: its rows are on screen.
        Act(() => cut.Find("[role=switch]").Click());
        Assert.Contains(L["rb.detailOnHint"], Text(cut));
        Act(() => cut.FindAll("input[type=checkbox]")[0].Change(true));
        Click(cut, L["rep.run"]);
        cut.WaitForAssertion(() => Assert.Contains("R-2", Text(cut)));
        Assert.Equal(1, Runs);

        // The owner's settings: summary, "Trip date by month" chosen as the column grouping, Average of loaded weight and Count.
        Act(() => cut.Find("[role=switch]").Click());
        Assert.NotNull(Control(cut, L.F("rb.groupN", 1)));                     // a row grouping is offered at once
        Act(() => Control(cut, L["rb.field"]).Change("1"));                                 // the column grouping's field: tripDate
        Act(() => Control(cut, L.F("rb.measureN", 1)).Change("2"));                         // average
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("label[for]").Count(l => l.TextContent.Trim() == L["rb.field"])));
        Act(() => cut.FindAll("label[for]").Where(l => l.TextContent.Trim() == L["rb.field"]).Select(l => cut.Find("#" + l.GetAttribute("for"))).Last().Change("0"));
        Click(cut, L["rb.addMeasure"]);
        Click(cut, L["rep.run"]);

        Assert.Equal(1, Runs);                                                   // nothing sent
        var text = Text(cut);
        Assert.Contains(L.F("rb.notShown", L["rb.needRow"]), text);
        Assert.DoesNotContain("definition", text, StringComparison.Ordinal);
        Assert.Equal("true", Control(cut, L.F("rb.groupN", 1)).GetAttribute("aria-invalid"));
        Assert.Contains(L["rb.stale"], text);
        Assert.Equal("true", cut.Find(".stale").GetAttribute("aria-hidden"));

        // Choosing the row grouping runs it and clears both marks.
        Act(() => Control(cut, L.F("rb.groupN", 1)).Change("1"));
        Click(cut, L["rep.run"]);
        cut.WaitForAssertion(() => Assert.Equal(2, Runs));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".stale")));
        Assert.DoesNotContain(L["rb.stale"], Text(cut));
        Assert.Contains("\"rows\":[{\"field\":\"tripDate\",\"dateUnit\":\"month\"}]", Requests.Last().Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_shows_server_refusals_by_setting_and_never_an_internal_name()
    {
        Respond("reports/builder/run", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""
                {"status":400,"code":"validation.failed","detail":"Please correct the highlighted fields.","errors":[
                 {"field":"definition.filters[0].values","code":"report.filterValueFormat","message":"المرشح 1: القيمة لا تناسب الحقل."},
                 {"field":"definition.rows[0].field","code":"validation.format","message":"قيمة الحقل «definition.rows[0].field» غير صحيحة."}]}
                """, Encoding.UTF8, "application/problem+json"),
        });
        var cut = Builder();
        Act(() => Control(cut, L.F("rb.groupN", 1)).Change("0"));
        Click(cut, L["rb.addFilter"]);
        Click(cut, L["rb.addFilter"]);
        Act(() => Control(cut, L.F("rb.filterN", 2)).Change("2"));                         // filter 2 on loaded weight; filter 1 left empty (not sent)
        Act(() => cut.FindAll("label[for]").Where(l => l.TextContent.Trim() == L["rb.value"]).Select(l => cut.Find("#" + l.GetAttribute("for"))).Last().Change("heavy"));
        Click(cut, L["rep.run"]);

        cut.WaitForAssertion(() => Assert.Contains("المرشح 1: القيمة لا تناسب الحقل.", Text(cut)));
        var text = Text(cut);
        Assert.DoesNotContain("definition", text, StringComparison.Ordinal);
        Assert.Contains(L.F("rb.checkSetting", L.F("rb.groupN", 1)), text);
        Assert.Equal("true", Control(cut, L.F("rb.groupN", 1)).GetAttribute("aria-invalid"));
        // The server's filter 1 is the second filter on the screen (the first was empty and not sent).
        var values = cut.FindAll("label[for]").Where(l => l.TextContent.Trim() == L["rb.value"]).Select(l => cut.Find("#" + l.GetAttribute("for"))).ToList();
        Assert.Null(values[0].GetAttribute("aria-invalid"));
        Assert.Equal("true", values[1].GetAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll(".stale"));                                    // nothing was shown before
    }

    [Theory]
    [InlineData("definition.rows", "row", 0)]
    [InlineData("definition.rows[1].dateUnit", "row", 1)]
    [InlineData("definition.column.field", "column", 0)]
    [InlineData("definition.columns", "columns", 0)]
    [InlineData("definition.columns[4]", "columns", 4)]
    [InlineData("definition.measures[2].field", "measure", 2)]
    [InlineData("definition.filters[3].operator", "filter", 3)]
    [InlineData("definition.top", "top", 0)]
    [InlineData("from", "from", 0)]
    public void Report_problem_paths_point_at_their_setting(string field, string kind, int index) =>
        Assert.Equal(new ReportProblems.Place(kind, index), ReportProblems.PlaceOf(field));
}
