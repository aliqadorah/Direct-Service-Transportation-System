using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Revision 6 part B, feature 3: the accounting dashboard against a fake API.</summary>
public sealed class AccountingDashboardPageTests : PageTestBase
{
    internal static AccountingDashboardResponse Full() => new(2026, 10, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 17), new DateOnly(2026, 1, 1),
        new AccountingFigures(5400, 2320, 0, new Payables(500, 120, 80, 700), new PeriodResult(1000, 300, 700), new PeriodResult(12000, 9000, 3000)),
        new BudgetUsage("b1", "موازنة 2026", 10000, 9000, 90, 15000, 12000, 80, 79.5m),
        Enumerable.Range(1, 12).Select(m => new MonthAmounts($"{(m < 3 ? 2025 : 2026)}-{((m + 9) % 12) + 1:00}", m * 100, m * 50)).ToList(),
        new AgeingBuckets(1000, 500, 320, 0, 500, 2320, 0),
        [
            new AccountingReminder(AccountingReminderKeys.DraftJournals, "info", 2, 150, "accounting/journal?view=Draft"),
            new AccountingReminder(AccountingReminderKeys.BillsDueSoon, "warning", 1, 70, "accounting/payables?view=due-soon"),
            new AccountingReminder(AccountingReminderKeys.DeductionsUnmapped, "error", 1, null, "settings/mapping"),
        ], DateTime.UtcNow);

    [Fact]
    public void Dashboard_shows_figures_charts_reminders_and_quick_links()
    {
        Respond("accounting/dashboard", Full());
        var cut = Render<AccountingDashboard>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["adash.reminders"], cut.Markup));
        var text = Text(cut);
        Assert.Contains(Fmt.Money(5400), text);                     // cash and banks
        Assert.Contains(Fmt.Money(2320), text);                     // receivables
        Assert.Contains("90%", text);                               // expense budget used
        Assert.Contains(Fmt.Money(3000), text);                     // net profit year to date
        Assert.Equal(12, cut.FindAll(".ch-trend .ch-col").Count);  // 12 months
        Assert.Equal(5, cut.FindAll(".ch-hrow").Count);             // ageing buckets
        Assert.Contains(L.F("adash.rem.draft-journals", 2), text);
        Assert.Contains($"/c/{Company}/accounting/payables?view=due-soon", cut.Markup);
        Assert.Contains($"/c/{Company}/settings/mapping", cut.Markup);
        Assert.Contains($"/c/{Company}/reports/financial?report=supplier-statement", cut.Markup);
        Assert.Contains($"/c/{Company}/accounting/trial-balance", cut.Markup);
        Assert.Contains("table class=\"sr-only\"", cut.Markup);       // chart values for screen readers
        // Owner decision 18 Oct 2026: owed to suppliers, drivers and partners, each linking to its accounts page, with the total.
        Assert.Contains(Fmt.Money(700), text);
        Assert.Contains($"/c/{Company}/driver-accounts", cut.Markup);
        Assert.Contains($"/c/{Company}/partner-accounts", cut.Markup);
        Assert.Contains($"href=\"/c/{Company}/accounting/payables\"", cut.Markup);
    }

    [Fact]
    public void Choosing_another_month_asks_for_it()
    {
        Respond("accounting/dashboard", Full());
        var cut = Render<AccountingDashboard>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["adash.reminders"], cut.Markup));
        var month = cut.FindAll("label").First(l => l.TextContent.Trim() == L["bud.month"]).GetAttribute("for");
        Act(() => cut.Find("#" + month).Change("2"));   // option index 2 = March
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == "accounting/dashboard?year=2026&month=3"));
    }

    [Fact]
    public void Nothing_to_remind_and_no_figures_for_a_reminders_only_user()
    {
        Respond("accounting/dashboard", Full() with { Figures = null, Budget = null, Trend = null, ReceivablesAgeing = null, Reminders = [] });
        var cut = Render<AccountingDashboard>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["adash.noReminders"], cut.Markup));
        Assert.Empty(cut.FindAll(".ch-trend"));
        Assert.DoesNotContain(L["adash.cash"], cut.Markup);
        Assert.DoesNotContain(L["adash.owedTitle"], cut.Markup);
    }

    [Fact]
    public void Accounting_dashboard_is_the_first_accounting_page_and_follows_permissions()
    {
        var accounting = NavModel.Company.Single(g => g.Key == "nav.accounting");
        Assert.Equal("accounting/dashboard", accounting.Items[0].Route);
        Assert.True(NavModel.RouteAllowed([PermissionKeys.BudgetView], $"/c/{Company}/accounting/dashboard"));
        Assert.False(NavModel.RouteAllowed([PermissionKeys.TripsView], $"/c/{Company}/accounting/dashboard"));
        Assert.True(NavModel.RouteAllowed([PermissionKeys.AcctPost], $"/c/{Company}/accounting/journal-templates"));
        Assert.False(NavModel.RouteAllowed([PermissionKeys.AcctView], $"/c/{Company}/accounting/journal-templates"));
    }

    [Fact]
    public void Payables_list_opens_the_due_soon_view_from_the_address()
    {
        Respond("apar/bills", new ApArDocumentPage(new DirectService.Shared.Contracts.PagedResult<ApArDocumentResponse>([], 1, 50, 0), new ApArDocumentTotals(0, 0, 0)));
        Respond("apar/parties/options", Array.Empty<object>());
        Respond("accounting/accounts/options", Array.Empty<object>());
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/payables?view=due-soon");
        var cut = Render<ApAr>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("apar/bills?", StringComparison.Ordinal) && r.Path.Contains("payment=due-soon", StringComparison.Ordinal)));
    }
}
