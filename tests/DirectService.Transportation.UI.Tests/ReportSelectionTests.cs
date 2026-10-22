using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Web.Client.Pages.Reports;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 22 Oct 2026 (DT-248, DT-249): the open report is named above its filters and marked in the list, kept in the address
/// (refresh, Back / Forward, bookmarks) and in the browser tab's name; every filter box shows the value in use, defaults included.
/// </summary>
public sealed class ReportSelectionTests : PageTestBase
{
    private static readonly Dictionary<string, string> Period = new() { ["from"] = "2026-10-01", ["to"] = "2026-10-22" };

    private static readonly ReportCatalogEntry Breach = new("tariff-breach", "RPT-04", "operational", "مخالفات التعرفة", null,
        ["from", "to", "side", "customer", "partner", "loadingSite", "unloadingSite"], [], "الرحلات التي سُعّرت بغير سعر التعرفة.",
        new Dictionary<string, string>(Period) { ["side"] = "customer" });

    private static readonly ReportCatalogEntry Margin = new("margin", "RPT-10", "operational", "تحليل الهامش", null, ["from", "to", "groupBy"], [],
        "الإيراد والكلفة والهامش.", new Dictionary<string, string>(Period) { ["groupBy"] = "customer" });

    private static readonly ReportCatalogEntry Balances = new("driver-balances", "RPT-19", "financial", "أرصدة السائقين", null, ["asOf"], [],
        "رصيد كل سائق في تاريخ محدد.", new Dictionary<string, string> { ["asOf"] = "2026-10-22" });

    private static readonly ReportResult Empty = new("x", "x", [], [new ResultColumn("a", "a", "text")], [], null, null, false);

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private IRenderedComponent<ReportsCatalog> Open(string address)
    {
        Respond("reports/catalog", new[] { Breach, Margin, Balances });
        Respond("reports/standard", Empty);
        Nav.NavigateTo(address);
        var cut = Render<ReportsCatalog>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".rep-list button")));
        return cut;
    }

    private static string Query(string uri) => new Uri(uri).Query;

    /// <summary>The form control whose label reads <paramref name="label"/>.</summary>
    private static AngleSharp.Dom.IElement Control(IRenderedComponent<ReportsCatalog> cut, string label) =>
        cut.Find("#" + cut.FindAll("label[for]").First(l => l.TextContent.Trim().TrimEnd('*').Trim() == label).GetAttribute("for"));

    private void Choose(IRenderedComponent<ReportsCatalog> cut, string code) =>
        Act(() => cut.FindAll(".rep-list button").Single(b => b.TextContent.Contains(code, StringComparison.Ordinal)).Click());

    [Fact]
    public void The_chosen_report_is_named_above_its_filters_and_marked_in_the_list()
    {
        var cut = Open($"/c/{Company}/reports/operational");
        Assert.Empty(cut.FindAll("#rep-head"));
        Assert.Empty(cut.FindAll(".rep-list [aria-current]"));

        Choose(cut, "RPT-04");
        cut.WaitForAssertion(() => Assert.Equal("RPT-04 · مخالفات التعرفة", cut.Find("#rep-head h2").TextContent.Trim()));
        Assert.Equal("الرحلات التي سُعّرت بغير سعر التعرفة.", cut.Find("#rep-head p").TextContent.Trim());
        var current = Assert.Single(cut.FindAll(".rep-list [aria-current]"));
        Assert.Equal("page", current.GetAttribute("aria-current"));
        Assert.Contains("RPT-04", current.TextContent);
        // The title comes before the filters in the results column (it stays above them when the list stacks on narrow screens).
        var main = cut.Find(".rep-main");
        Assert.Equal("rep-head", main.Children[0].Id);
        Assert.Equal("FORM", main.Children[1].TagName);
        UiModule.VerifyInvoke("revealStacked");

        Choose(cut, "RPT-10");
        cut.WaitForAssertion(() => Assert.Contains("RPT-10", cut.Find("#rep-head h2").TextContent));
        Assert.Contains("RPT-10", Assert.Single(cut.FindAll(".rep-list [aria-current]")).TextContent);
    }

    [Fact]
    public void The_browser_tab_is_named_after_the_open_report()
    {
        var head = Render<HeadOutlet>();
        var cut = Open($"/c/{Company}/reports/operational?report=tariff-breach");
        cut.WaitForAssertion(() => Assert.Contains("RPT-04 · مخالفات التعرفة · " + L["shell.product"], head.Markup));
        Choose(cut, "RPT-10");
        cut.WaitForAssertion(() => Assert.Contains("RPT-10 · تحليل الهامش", head.Markup));
    }

    [Fact]
    public void The_report_and_its_filters_are_kept_in_the_address_and_back_reopens_the_report_before()
    {
        var cut = Open($"/c/{Company}/reports/operational");
        Choose(cut, "RPT-04");
        cut.WaitForAssertion(() => Assert.Contains("report=tariff-breach", Nav.Uri));
        var breach = Nav.Uri;
        Assert.Contains("from=2026-10-01", Query(breach));
        Assert.Contains("to=2026-10-22", Query(breach));
        Assert.Contains("side=customer", Query(breach));

        Choose(cut, "RPT-10");
        cut.WaitForAssertion(() => Assert.Contains("report=margin", Nav.Uri));
        Assert.Contains("groupBy=customer", Query(Nav.Uri));
        // Choosing a report is a step of its own in the browser's history.
        Assert.Contains(((Bunit.TestDoubles.BunitNavigationManager)Nav).History, h => h.Uri.Contains("report=tariff-breach", StringComparison.Ordinal));

        // Back: the address of the report before → that report again, with its filters.
        Requests.Clear();
        Nav.NavigateTo(breach);
        cut.WaitForAssertion(() => Assert.Contains("RPT-04", cut.Find("#rep-head h2").TextContent));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("reports/standard/tariff-breach?", StringComparison.Ordinal)));
    }

    [Fact]
    public void A_bookmarked_address_reopens_the_report_with_its_filters()
    {
        var cut = Open($"/c/{Company}/reports/operational?report=tariff-breach&from=2026-09-01&to=2026-09-30&side=partner");
        cut.WaitForAssertion(() => Assert.Contains("RPT-04", cut.Find("#rep-head h2").TextContent));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == "reports/standard/tariff-breach?from=2026-09-01&to=2026-09-30&side=partner"));
        Assert.Equal("1", Control(cut, L["rep.side"]).GetAttribute("value"));            // Partners
        Assert.Equal("2026-09-01", Control(cut, L["trip.from"]).GetAttribute("value"));
    }

    [Fact]
    public void Every_filter_box_shows_the_value_in_use_defaults_included()
    {
        var cut = Open($"/c/{Company}/reports/operational?report=tariff-breach");
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("reports/standard/tariff-breach", StringComparison.Ordinal)));
        Assert.Equal("0", Control(cut, L["rep.side"]).GetAttribute("value"));            // Customers — what the result line says
        Assert.Equal("2026-10-01", Control(cut, L["trip.from"]).GetAttribute("value"));
        Assert.Equal("2026-10-22", Control(cut, L["trip.to"]).GetAttribute("value"));
        Assert.Contains(Requests, r => r.Path == "reports/standard/tariff-breach?from=2026-10-01&to=2026-10-22&side=customer");

        // A box the user empties shows the value the report uses again when it runs.
        Act(() => Control(cut, L["trip.from"]).Change(""));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["rep.run"]).Click());
        cut.WaitForAssertion(() => Assert.Equal("2026-10-01", Control(cut, L["trip.from"]).GetAttribute("value")));

        Choose(cut, "RPT-10");
        cut.WaitForAssertion(() => Assert.Equal("0", Control(cut, L["rep.groupBy"]).GetAttribute("value")));   // by customer
    }

    [Fact]
    public void The_as_of_date_shows_its_default()
    {
        var cut = Open($"/c/{Company}/reports/financial?report=driver-balances");
        cut.WaitForAssertion(() => Assert.Equal("2026-10-22", Control(cut, L["rep.asOf"]).GetAttribute("value")));
    }
}
