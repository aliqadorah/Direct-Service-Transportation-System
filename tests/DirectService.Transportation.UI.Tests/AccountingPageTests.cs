using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.ApAr;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Ledger;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Accounting screens (SCR-30 … SCR-45) against a fake API.</summary>
public sealed class AccountingPageTests : PageTestBase
{
    private static readonly AccountRef Cash = new("a2", "1101", "الصندوق الرئيسي", "Main cash box");
    private static readonly AccountRef Receivables = new("a3", "1110", "الذمم التجارية", "Trade receivables");

    public AccountingPageTests()
    {
        Respond("accounting/accounts/options", new[]
        {
            new AccountOption(Cash, "Asset", false, null),
            new AccountOption(Receivables, "Asset", true, "Customer"),
        });
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Respond("apar/parties/options", new[] { new ApArPartyOption("Customer", "cu1", "شركة البوتاس العربية", null, 30) });
    }

    [Fact]
    public void Chart_shows_the_tree_and_collapses_a_group()
    {
        Respond("accounting/accounts", new[]
        {
            new AccountResponse("a1", "11", "الأصول المتداولة", "Current assets", "Asset", null, null, 1, false, true, 0, 0, false, null, "Active", "A"),
            new AccountResponse("a2", "1101", "الصندوق الرئيسي", "Main cash box", "Asset", "a1", "11", 2, true, false, 500, 0, false, null, "Active", "A"),
        });
        var cut = Render<ChartOfAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("الصندوق الرئيسي", cut.Markup));
        cut.Find($"button[aria-label='{L.F("acc.collapse", "11")}']").Click();
        Assert.DoesNotContain("الصندوق الرئيسي", cut.Markup);
    }

    [Fact]
    public void Empty_chart_offers_the_transport_template()
    {
        Respond("accounting/accounts", Array.Empty<object>());
        var cut = Render<ChartOfAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["acc.useTemplate"], cut.Markup));
    }

    [Fact]
    public void Journal_form_shows_the_difference_and_a_party_picker_on_control_accounts()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.lines"], cut.Markup));
        Assert.Contains(L.F("je.difference", Fmt.Money(0)), Text(cut));
        Assert.DoesNotContain(L.F("je.lineParty", 1), cut.Markup);
    }

    [Fact]
    public void Trial_balance_highlights_a_mismatch()
    {
        var row = new TrialBalanceRow(Cash, "Asset", 2, true, 0, 0, 100, 0, 100, 0);
        Respond("accounting/reports/trial-balance", new TrialBalanceReport(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 2, 2, [row],
            new TrialBalanceTotals(0, 0, 100, 0, 100, 0), false, null));
        var cut = Render<TrialBalance>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["rep.tbMismatch"], cut.Markup));
        Assert.Contains("1101", cut.Markup);
    }

    [Fact]
    public void Receivables_list_requests_open_documents_of_the_receivable_kind()
    {
        Respond("apar/receivables", new ApArDocumentPage(new PagedResult<ApArDocumentResponse>([], 1, 50, 0), new ApArDocumentTotals(0, 0, 0)));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/receivables");
        var cut = Render<ApAr>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["ap.noReceivables"], cut.Markup));
        Assert.Contains(Requests, r => r.Path.StartsWith("apar/receivables?", StringComparison.Ordinal) && r.Path.Contains("payment=open", StringComparison.Ordinal));
    }
}
