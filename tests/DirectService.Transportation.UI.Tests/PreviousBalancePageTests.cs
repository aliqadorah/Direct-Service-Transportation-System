using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Cash;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Trips;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Pages.Billing;
using DirectService.Transportation.Web.Client.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner findings of 27 Oct 2026: the previous balance from manual / opening entries on the settlement and the driver account (DT-276), the
/// accounting dashboard's "What we owe" / "Owed to us" (DT-278), the journal's "Cancelled" view (DT-279) and the journal form's note for a
/// driver or partner party (DT-280) — against a fake API, events through <c>Act</c>.
/// </summary>
public sealed class PreviousBalancePageTests : PageTestBase
{
    private static readonly DateOnly Day = new(2026, 10, 27);
    private static readonly PreviousBalanceEntry Opening = new(new DocRef("je42", "JV-00042"), new DateOnly(2026, 10, 1), "سلفة افتتاحية", -100m);

    private static SettlementPreview Preview(decimal previous) => new(new Ref("d1", "سامي العبادي"), "10-22222", Day, [], [], [], 0m, 0m, previous, previous,
        previous == 0 ? [] : [Opening]);

    // ------------------------------------------------------------------ settlement (DT-276)

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Settlement_shows_the_previous_balance_as_its_own_line_settles_it_without_trips_and_can_leave_it_out(string language)
    {
        L.SetLanguage(language);
        Respond("settlements/preview", Preview(-100m));
        Respond("settlements", new SettlementDetails(new SettlementResponse("s1", "STL-00009", new Ref("d1", "سامي العبادي"), Day, 0m, 0m, 0m, 0m, "Cash",
            new MoneyAccountRef("CashBox", "cb1", "الصندوق"), null, "Active", null, false, null, DateTime.UtcNow, "RV", 100m, 0m, null, -100m), [], [], [], "words", [], [Opening]));
        var cut = Render<SettlementForm>(p => p.Add(x => x.Company, Company).Add(x => x.DriverId, "d1"));
        cut.WaitForAssertion(() => Assert.Contains(L["pb.title"], Text(cut)));
        var text = Text(cut);
        Assert.Contains(L.F("pb.owedBy", "سامي العبادي", Fmt.Money(100m)), text);              // who owes whom, without a minus
        Assert.Contains("JV-00042", text);
        Assert.Contains(L["pb.owesCompany"], text);
        Assert.Single(cut.FindAll(".pb-line"));                                                   // the line in the totals
        Assert.Contains(L.F("drv.netNegativeRepay", Fmt.Money(100m)), text);                      // deducted like an advance → repaid or carried
        AngleSharp.Dom.IElement Confirm() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["drv.confirmSettlement"], StringComparison.Ordinal));
        Assert.False(Confirm().HasAttribute("disabled"));                                         // no trips needed

        Act(() => cut.Find(".pb-include").Change(false));                                         // left out: nothing to settle
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".pb-line")));
        Assert.True(Confirm().HasAttribute("disabled"));

        Act(() => cut.Find(".pb-include").Change(true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".pb-line")));
        Act(() => Confirm().Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "settlements"));
        var body = Requests.Last(r => r.Method == "POST" && r.Path == "settlements").Body!;
        Assert.Contains("\"includePreviousBalance\":true", body, StringComparison.Ordinal);
        Assert.Contains("\"repaidAmount\":100", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Driver_account_shows_the_previous_balance_and_says_nothing_open_only_when_everything_is_zero()
    {
        Respond("drivers/d1/account", new DriverAccountResponse(new Ref("d1", "سامي العبادي"), "10-22222", [], [], 0m, 0m, -100m, -100m, [], -100m, [Opening]));
        var cut = Render<DriverAccount>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "d1"));
        cut.WaitForAssertion(() => Assert.Contains("JV-00042", cut.Markup));                     // opened on its own tab
        var text = Text(cut);
        Assert.Contains(cut.FindAll("[role=tab]"), t => t.TextContent.Contains(L["pb.tab"], StringComparison.Ordinal));
        Assert.Contains(L["drv.owesCompanyFigure"], text);                                        // never "Net due −100"
        Assert.DoesNotContain(Fmt.Money(-100m), text);
        Assert.DoesNotContain(L["drv.noOpenTrips"], text);

        Act(() => cut.FindAll("[role=tab]").First(t => t.TextContent.Contains(L["md.openTrips"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["drv.noOpenTripsOnly"], Text(cut)));
        Assert.DoesNotContain(L["drv.noOpenTrips"], Text(cut));

        Respond("drivers/d2/account", new DriverAccountResponse(new Ref("d2", "أحمد صالح"), null, [], [], 0m, 0m, 0m, 0m, []));
        var zero = Render<DriverAccount>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "d2"));
        zero.WaitForAssertion(() => Assert.Contains(L["drv.noOpenTrips"], Text(zero)));
        Assert.DoesNotContain(zero.FindAll("[role=tab]"), t => t.TextContent.Contains(L["pb.tab"], StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ accounting dashboard (DT-278)

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Dashboard_separates_what_we_owe_from_what_is_owed_to_us_with_links_to_each_side(string language)
    {
        L.SetLanguage(language);
        var full = AccountingDashboardPageTests.Full();
        Respond("accounting/dashboard", full with { Figures = full.Figures! with { OwedToUs = new OwedToUs(0m, 100m, 30m, 130m) } });
        var cut = Render<AccountingDashboard>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["adash.owedToUsTitle"], cut.Markup));
        var text = Text(cut);
        Assert.Contains(L["adash.owedTitle"], text);
        Assert.Contains(L["adash.payables"], text);                                               // top cards: we owe …
        Assert.Contains(L["adash.owedToUs"], text);                                               // … and they owe us
        Assert.Contains(Fmt.Money(130m), cut.Find(".adash-owed-to-us").TextContent);
        Assert.Contains(Fmt.Money(700m), cut.Find(".adash-owed").TextContent);
        Assert.Contains($"/c/{Company}/driver-accounts?side=owed", cut.Markup);
        Assert.Contains($"/c/{Company}/driver-accounts?side=owes", cut.Markup);
        Assert.Contains($"/c/{Company}/partner-accounts?side=owed", cut.Markup);
        Assert.Contains($"/c/{Company}/partner-accounts?side=owes", cut.Markup);
        Assert.DoesNotContain(Fmt.Money(-100m), text);
    }

    [Fact]
    public void Driver_accounts_list_opens_on_the_side_in_the_address_and_asks_for_it()
    {
        Respond("reports/standard/driver-balances", new DirectService.Shared.Contracts.ReportResult("driver-balances", "أرصدة السائقين", [],
            [new("driver", "السائق", "text"), new("owed", "المستحق للسائق", "money"), new("owes", "على السائق للشركة", "money")],
            [new DirectService.Shared.Contracts.ResultRow(["سامي العبادي", 0m, 100m], null)], null, null, false));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/driver-accounts?side=owes");
        var cut = Render<DriverAccounts>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("سامي العبادي", cut.Markup));
        Assert.Contains(Requests, r => r.Path == "reports/standard/driver-balances?side=owes");
        Assert.Contains(L["bal.sideOwes"], cut.Markup);
    }

    // ------------------------------------------------------------------ journal (DT-279, DT-280)

    private static PagedResult<JournalEntrySummary> Journal() => new(
    [
        new JournalEntrySummary("e2", "JV-00073", Day, "2026-10", "إلغاء القيد JV-00072", "ACC", "Manual", true, "Posted", 12, 12, "JV-00072", null),
        new JournalEntrySummary("e1", "JV-00072", Day, "2026-10", "قيد خاطئ", "ACC", "Manual", true, "Void", 12, 12, null, "JV-00073", null, "أُدخل مرتين"),
    ], 1, 50, 2);

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Journal_has_one_cancelled_view_with_the_reason_and_the_reversal_labels(string language)
    {
        L.SetLanguage(language);
        Respond("accounting/journal-entries", Journal());
        var cut = Render<JournalList>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("JV-00072", cut.Markup));
        var tabs = cut.FindAll("[role=tab]").Select(t => t.TextContent.Trim()).ToList();
        Assert.Contains(tabs, t => t.Contains(L["je.cancelledView"], StringComparison.Ordinal));
        Assert.DoesNotContain(tabs, t => t == L["st.reversed"] || t == L["st.void"]);              // the two old tabs are gone
        Assert.Contains(L.F("je.reverses", "JV-00072"), Text(cut));                               // "Reversal of JV-00072"
        Assert.Contains(L.F("je.reversedBy", "JV-00073"), Text(cut));                             // "Cancelled by JV-00073"
        Assert.DoesNotContain(L["je.cancelReason"], Text(cut));                                   // the reason column only in Cancelled

        Act(() => cut.FindAll("[role=tab]").First(t => t.TextContent.Contains(L["je.cancelledView"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("accounting/journal-entries?", StringComparison.Ordinal) && r.Path.Contains("status=Cancelled", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => Assert.Contains(L["je.cancelReason"], Text(cut)));
        Assert.Contains("أُدخل مرتين", Text(cut));
        Assert.Contains(L["je.cancelledHelp"], Text(cut));
    }

    [Fact]
    public void An_old_void_or_reversed_view_in_the_address_opens_the_cancelled_view()
    {
        Respond("accounting/journal-entries", Journal());
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal?view=Void");
        var cut = Render<JournalList>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.Contains("status=Cancelled", StringComparison.Ordinal)));
        Assert.DoesNotContain(Requests, r => r.Path.Contains("status=Void", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Driver", "je.driverPartyNote", "je.partnerPartyNote")]
    [InlineData("Partner", "je.partnerPartyNote", "je.driverPartyNote")]
    public void Journal_form_notes_the_document_to_use_for_a_driver_or_partner_party_without_blocking(string partyType, string shown, string hidden)
    {
        var control = new AccountRef("a4", partyType == "Driver" ? "1104" : "2103", "حساب", "Account");
        var cash = new AccountRef("a2", "110101", "الصندوق الرئيسي", "Main cash box");
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Respond("apar/parties/options", Array.Empty<object>());
        Respond("accounting/tax-codes/options", Array.Empty<object>());
        Respond("accounting/accounts/options", new[] { new AccountOption(control, "Asset", true, partyType), new AccountOption(cash, "Asset", false, null) });
        Respond("accounting/journal-templates/tt", new JournalTemplateResponse("tt", "T", "رصيد", null,
            [new JournalTemplateLineResponse(control, 100, 0, null, null, null), new JournalTemplateLineResponse(cash, 0, 100, null, null, null)], DateTime.UtcNow, "TV"));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new?template=tt");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L[shown], Text(cut)));
        Assert.DoesNotContain(L[hidden], Text(cut));
        Assert.Single(cut.FindAll(".je-party-note"));
    }
}
