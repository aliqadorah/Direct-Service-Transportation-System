using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Web.Client.Pages.Reports;
using DirectService.Transportation.Web.Client.Ui;
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

        cut.FindAll("button").Single(b => b.TextContent.Trim() == L["rep.run"]).Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("input[role=combobox]").GetAttribute("aria-invalid")));
        Assert.Contains(L.F("ui.requiredField", L["trip.customer"]), Text(cut));
        Assert.DoesNotContain(L["ui.loadFailed"], cut.Markup);
        Assert.DoesNotContain(Requests, r => r.Path.StartsWith("reports/standard", StringComparison.Ordinal));
        Assert.Empty(Services.GetRequiredService<ToastService>().Items);
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
}
