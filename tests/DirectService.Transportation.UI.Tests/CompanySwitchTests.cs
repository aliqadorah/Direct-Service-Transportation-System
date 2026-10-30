using DirectService.Platform.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Invoicing;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Transportation.Web.Client.Pages;
using DirectService.Transportation.Web.Client.Pages.Billing;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Shared.UI.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Company switch (DT-187): after the address names another company, every page asks the new company only, the old company's answers that
/// arrive late are never shown, and the shell's figures (notifications badge) follow the new company. The layout is driven as the router
/// drives it: the router re-renders the layout with the new page before the layout's own location handler runs.
/// </summary>
public sealed class CompanySwitchTests : PageTestBase
{
    private const string Other = "c2";

    public CompanySwitchTests()
    {
        AddCompany(Other, "Ali Transportation");
        Respond("/me", Shell.Me);
        Respond("/announcements", Array.Empty<object>());
        Respond("setup-checklist", new SetupChecklistResponse([], 0, 0));
        RespondFor(Other, "setup-checklist", new SetupChecklistResponse([], 0, 0));
        Respond("notifications", new NotificationPage([], 7));
        RespondFor(Other, "notifications", new NotificationPage([], 2));
        Respond("dashboard", Dash(662.244m, "الصندوق الرئيسي", 212.469m));
        RespondFor(Other, "dashboard", Dash(15.500m, "صندوق علي", 3.250m));
    }

    private static DashboardResponse Dash(decimal collections, string box, decimal balance) =>
        new("mtd", new DashboardFigures(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 14), 4, null, null, null, null, null, null, collections), [],
            [new DashboardCashCard("CashBox", "cb-" + box, box, balance)], [], null, DateTime.UtcNow);

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    /// <summary>The routed page of the address: dashboard or invoices, with the company of the address.</summary>
    private void Page(RenderTreeBuilder b)
    {
        var parts = new Uri(Nav.Uri).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        b.OpenComponent(0, parts.Length > 2 && parts[2] == "invoices" ? typeof(Invoices) : typeof(Dashboard));
        b.AddAttribute(1, "Company", parts[1]);
        b.CloseComponent();
    }

    /// <summary>The company layout with the routed page; like the router, a new address re-renders it with the new page first.</summary>
    private IRenderedComponent<CompanyLayout> Layout(string start)
    {
        Nav.NavigateTo(start);
        IRenderedComponent<CompanyLayout>? cut = null;
        var path = new Uri(Nav.Uri).AbsolutePath;
        Nav.LocationChanged += (_, e) =>
        {
            // A new page address (not a list writing its filters into the query, UX-12).
            var now = new Uri(e.Location).AbsolutePath;
            if (now == path) return;
            path = now;
            cut?.Render(p => p.Add(x => x.Body, (RenderFragment)Page));
        };
        cut = Render<CompanyLayout>(p => p.Add(x => x.Body, (RenderFragment)Page));
        return cut;
    }

    [Fact]
    public void Dashboard_after_a_switch_asks_the_new_company_and_never_shows_the_old_figures()
    {
        var cut = Layout($"/c/{Company}");
        cut.WaitForAssertion(() => Assert.Contains("662.244", cut.Markup));
        Assert.Contains("212.469", cut.Markup);
        var before = CompanyRequests.Count;

        Nav.NavigateTo($"/c/{Other}");   // the company menu

        cut.WaitForAssertion(() => Assert.Contains("15.500", cut.Markup));
        Assert.Contains("صندوق علي", cut.Markup);
        Assert.DoesNotContain("662.244", cut.Markup);
        Assert.DoesNotContain("212.469", cut.Markup);
        Assert.DoesNotContain("الصندوق الرئيسي", cut.Markup);
        var after = CompanyRequests.Skip(before).ToList();
        Assert.Contains(after, r => r.Company == Other && r.Path.StartsWith("dashboard", StringComparison.Ordinal));
        Assert.DoesNotContain(after, r => r.Company == Company);   // nothing of the old company is asked after the switch
        Assert.Equal(Other, Shell.Company?.Id);
        cut.WaitForAssertion(() => Assert.Equal(2, Shell.UnreadNotifications));   // the badge is the new company's
    }

    [Fact]
    public void Dashboard_ignores_the_old_companys_answer_that_arrives_after_the_switch()
    {
        CompanyDelay[Company] = TimeSpan.FromMilliseconds(400);   // the training company's figures are slow
        var cut = Render<Dashboard>(p => p.Add(x => x.Company, Company));
        Assert.Contains(CompanyRequests, r => r.Company == Company && r.Path.StartsWith("dashboard", StringComparison.Ordinal));

        cut.InvokeAsync(() => Shell.SelectCompany(Other));
        cut.Render(p => p.Add(x => x.Company, Other));

        cut.WaitForAssertion(() => Assert.Contains("15.500", cut.Markup));
        Thread.Sleep(600);   // the old answer has come and gone
        cut.Render();
        Assert.Contains("15.500", cut.Markup);
        Assert.DoesNotContain("662.244", cut.Markup);
        Assert.DoesNotContain(L["ui.loadFailed"], cut.Markup);
    }

    [Fact]
    public void Invoice_list_after_a_switch_asks_the_new_company_and_drops_the_late_old_answer()
    {
        Respond("invoices", new InvoicePage(new PagedResult<InvoiceResponse>([BillingPageTests.Invoice(number: "INV-00001")], 1, 50, 1), new InvoiceListTotals(1, 1, 0, 0, 1)));
        RespondFor(Other, "invoices", new InvoicePage(new PagedResult<InvoiceResponse>([BillingPageTests.Invoice(number: "INV-09999")], 1, 50, 1), new InvoiceListTotals(1, 1, 0, 0, 1)));
        CompanyDelay[$"{Company}/invoices"] = TimeSpan.FromMilliseconds(400);   // the old company's list is on its way when the user switches
        var cut = Layout($"/c/{Company}/invoices");
        cut.WaitForAssertion(() => Assert.Contains(CompanyRequests, r => r.Company == Company && r.Path.StartsWith("invoices?", StringComparison.Ordinal)));
        var before = CompanyRequests.Count;
        Nav.NavigateTo($"/c/{Other}/invoices");   // e.g. Back to the other company's list

        cut.WaitForAssertion(() => Assert.Contains("INV-09999", cut.Markup));
        Thread.Sleep(600);
        cut.Render();
        Assert.Contains("INV-09999", cut.Markup);
        Assert.DoesNotContain("INV-00001", cut.Markup);
        var after = CompanyRequests.Skip(before).ToList();
        Assert.Contains(after, r => r.Company == Other && r.Path.StartsWith("invoices?", StringComparison.Ordinal));
        Assert.DoesNotContain(after, r => r.Company == Company);
    }

    [Fact]
    public async Task Shell_figures_of_the_old_company_never_land_on_the_new_one()
    {
        var session = Services.GetRequiredService<AppSession>();
        await session.EnsureLoadedAsync();
        CompanyDelay[Company] = TimeSpan.FromMilliseconds(300);
        var old = session.EnterCompanyAsync(Company);   // its badge figures are slow
        Assert.True(await session.EnterCompanyAsync(Other));
        Assert.Equal(2, Shell.UnreadNotifications);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
        Assert.Equal(2, Shell.UnreadNotifications);
        Assert.Equal(Other, Shell.Company?.Id);
    }

    [Fact]
    public async Task Api_drops_a_read_of_a_company_left_meanwhile_and_lets_a_change_finish()
    {
        CompanyDelay[Company] = TimeSpan.FromMilliseconds(200);
        var api = Services.GetRequiredService<Api>();
        var read = api.GetAsync<DashboardResponse>(api.C("dashboard"), Xunit.TestContext.Current.CancellationToken);
        Respond("locks", new { id = "l1" });
        var change = api.PostAsync<object>(api.C("locks"), new { }, Xunit.TestContext.Current.CancellationToken);
        Shell.SelectCompany(Other);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
        await change;   // a change the user started finishes
        // A company named on purpose (the tenant's data export) is not tied to the company on screen.
        Respond("exports", new { id = "x1" });
        Assert.NotNull(await api.PostAsync<object>(Api.C(Company, "exports"), new { }, Xunit.TestContext.Current.CancellationToken));
        Assert.Equal("c1", Api.CompanyOf("api/v1/companies/c1/dashboard?period=mtd"));
        Assert.Null(Api.CompanyOf("api/v1/tenant/companies"));
    }
}
