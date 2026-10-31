using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using DirectService.Transportation.Web.Client.Pages.Settings;
using DirectService.Platform.UI.Pages.Settings;
using DirectService.Shared.UI.Pages.Settings;
using DirectService.Transportation.Web.Client.Ui;
using DirectService.Shared.UI.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner request 25 Oct 2026 (DT-254…DT-258): the journal entry form's line reference and tax columns (rate, tax, gross read-only; the tax
/// account only among tax accounts; the automatic tax line previewed; balance with tax), the copy of an entry, the entry view and
/// Settings → Tax codes.
/// </summary>
public sealed class JournalTaxPageTests : PageTestBase
{
    private static readonly AccountRef Supplies = new("a5", "5299", "مصاريف أخرى", "Other expenses");
    private static readonly AccountRef Cash = new("a2", "110101", "الصندوق الرئيسي", "Main cash box");
    private static readonly AccountRef InputVat = new("a9", "1105", "ضريبة المبيعات على المشتريات (مدخلات)", "Sales tax input");
    private static readonly TaxCodeRef Vat16 = new("t16", "VAT_JO_16", 16, "ضريبة المبيعات 16%", "VAT 16%");
    private static readonly TaxCodeRef Vat0 = new("t0", "VAT_JO_0", 0, "ضريبة المبيعات 0%", "VAT 0%");

    private static JournalTemplateResponse Template(decimal credit) => new("tt", "Supplies", "قرطاسية", "INV-1",
        [new JournalTemplateLineResponse(Supplies, 100, 0, null, null, "INV-1", Vat16, InputVat), new JournalTemplateLineResponse(Cash, 0, credit, null, null, "PAY-7")],
        DateTime.UtcNow, "TV");

    public JournalTaxPageTests()
    {
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Respond("apar/parties/options", Array.Empty<object>());
        Respond("accounting/tax-codes/options", new[] { Vat0, Vat16 });
        Respond("accounting/journal-templates/tt", Template(116));
    }

    private void WithAccounts(bool taxAccount = true) => Respond("accounting/accounts/options", taxAccount
        ? new[] { new AccountOption(Supplies, "Expense", false, null), new AccountOption(Cash, "Asset", false, null), new AccountOption(InputVat, "Asset", false, null, true) }
        : new[] { new AccountOption(Supplies, "Expense", false, null), new AccountOption(Cash, "Asset", false, null) });

    private IRenderedComponent<JournalEntryForm> Form(string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new{query}");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.lines"], cut.Markup));
        return cut;
    }

    [Fact]
    public void A_taxed_line_shows_rate_tax_gross_and_the_automatic_tax_line_and_posts_the_tax_code_and_account()
    {
        WithAccounts();
        Respond("accounting/journal-entries", new JournalEntryResponse("e1", "JV-1", new DateOnly(2026, 10, 25), "2026-10", "قرطاسية", "ACC", "Manual", null, true, "Posted",
            116, 116, null, null, null, null, null, null, DateTime.UtcNow, null, [], "RV"));
        var cut = Form("?template=tt");
        cut.WaitForAssertion(() => Assert.Contains(L["je.balancedWithTax"], cut.Markup));
        var text = Text(cut);
        Assert.Contains("16%", text);
        Assert.Contains(Fmt.Money(16), text);
        Assert.Contains(Fmt.Money(116), text);
        Assert.Contains(L["je.netTotal"], text);
        Assert.Contains(L["je.grossTotal"], text);
        var auto = cut.Find("tr.auto");
        Assert.Contains(L.F("je.autoTaxFor", 1), auto.TextContent);
        Assert.Contains("1105", auto.TextContent);
        Assert.Contains(L["je.taxHelp"], cut.Markup);
        // The tax account list offers the tax accounts only.
        Assert.Equal(InputVat.Code + " · " + InputVat.NameAr, cut.Find($"input[aria-label='{L.F("je.lineTaxAccount", 1)}']").GetAttribute("value"));

        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["je.post"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "accounting/journal-entries"));
        var body = Requests.Single(r => r.Method == "POST" && r.Path == "accounting/journal-entries").Body!;
        Assert.Contains("\"taxCodeId\":\"t16\",\"taxAccountId\":\"a9\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reference\":\"PAY-7\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_the_tax_on_the_other_side_the_difference_including_tax_is_shown()
    {
        WithAccounts();
        Respond("accounting/journal-templates/tt", Template(100));
        var cut = Form("?template=tt");
        cut.WaitForAssertion(() => Assert.Contains(L.F("je.differenceWithTax", Fmt.Money(16)), Text(cut)));
    }

    [Fact]
    public void With_no_tax_account_in_the_chart_the_line_offers_to_add_the_default_tax_accounts()
    {
        WithAccounts(taxAccount: false);
        Respond("accounting/accounts/default-tax-accounts", new DefaultTaxAccountsResult(1, 2, [InputVat]));
        var cut = Form("?template=tt");
        cut.WaitForAssertion(() => Assert.Contains(L["acc.addTaxAccounts"], cut.Markup));
        Assert.Empty(cut.FindAll("tr.auto"));   // no tax account chosen yet: nothing previewed
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["acc.addTaxAccounts"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "accounting/accounts/default-tax-accounts"));
    }

    [Fact]
    public void New_lines_start_with_the_entry_reference_and_follow_a_change_of_it()
    {
        WithAccounts();
        var cut = Form();
        var header = cut.FindAll("label").Single(l => l.TextContent.Trim() == L["je.reference"]).GetAttribute("for");
        Act(() => cut.Find("#" + header).Change("HDR-1"));
        cut.WaitForAssertion(() => Assert.Equal("HDR-1", cut.Find($"input[aria-label='{L.F("je.lineReference", 1)}']").GetAttribute("value")));
        Act(() => cut.Find($"input[aria-label='{L.F("je.lineReference", 2)}']").Change("OWN-2"));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["je.addLine"]).Click());
        cut.WaitForAssertion(() => Assert.Equal("HDR-1", cut.Find($"input[aria-label='{L.F("je.lineReference", 3)}']").GetAttribute("value")));
        Act(() => cut.Find("#" + header).Change("HDR-2"));
        cut.WaitForAssertion(() => Assert.Equal("HDR-2", cut.Find($"input[aria-label='{L.F("je.lineReference", 1)}']").GetAttribute("value")));
        Assert.Equal("OWN-2", cut.Find($"input[aria-label='{L.F("je.lineReference", 2)}']").GetAttribute("value"));
        Assert.Equal("HDR-2", cut.Find($"input[aria-label='{L.F("je.lineReference", 3)}']").GetAttribute("value"));
    }

    private static JournalEntryResponse Posted() => new("e1", "JV-000001", new DateOnly(2026, 10, 25), "2026-10", "قرطاسية", "ACC", "Manual", null, true, "Posted",
        116, 116, null, null, null, null, null, null, DateTime.UtcNow, "أنا",
        [
            new JournalLineResponse(1, Supplies, 100, 0, null, null, null, "INV-1", Vat16, 16, 16, InputVat),
            new JournalLineResponse(2, Cash, 0, 116, null, null, null, "PAY-7"),
            new JournalLineResponse(3, InputVat, 16, 0, null, null, "ضريبة المبيعات 16% على السطر 1 / VAT 16% on line 1", "INV-1", Vat16, 16, null, null, 1),
        ], "RV", "INV-1");

    [Fact]
    public void Copy_fills_a_new_entry_with_the_entered_lines_their_references_and_tax_codes_but_not_the_automatic_tax_line()
    {
        WithAccounts();
        Respond("accounting/journal-entries/e1", Posted());
        var cut = Form("?copy=e1");
        cut.WaitForAssertion(() => Assert.Contains(L.F("je.copyTitle", "JV-000001"), Text(cut)));
        Assert.Contains(L.F("je.copyHelp", "JV-000001"), Text(cut));
        Assert.Equal(2, cut.FindAll("input[aria-label^='" + L.F("je.lineAccount", 1)[..^1] + "']").Count);
        Assert.Equal("PAY-7", cut.Find($"input[aria-label='{L.F("je.lineReference", 2)}']").GetAttribute("value"));
        Assert.Single(cut.FindAll("tr.auto"));   // previewed again, posted again
        Assert.Contains(L["je.balancedWithTax"], cut.Markup);
    }

    [Fact]
    public void The_entry_view_marks_the_automatic_tax_line_shows_line_references_and_offers_copy()
    {
        WithAccounts();
        Respond("accounting/journal-entries/e1/history", Array.Empty<object>());
        Respond("accounting/journal-entries/e1", Posted());
        var cut = Render<JournalEntryView>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "e1"));
        cut.WaitForAssertion(() => Assert.Contains("JV-000001", cut.Markup));
        var auto = cut.Find("tr.auto");
        Assert.Contains(L["je.autoTax"], auto.TextContent);
        Assert.Contains(L.F("je.autoTaxFor", 1), auto.TextContent);
        Assert.Contains("PAY-7", Text(cut));
        Assert.Contains(L["je.taxCode"], cut.Markup);
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href")?.EndsWith("accounting/journal/new?copy=e1", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Tax_codes_settings_list_the_codes_and_lock_code_and_rate_of_a_code_in_use()
    {
        Respond("accounting/accounts/options", Array.Empty<object>());
        Respond("accounting/tax-codes", r => r.Method == HttpMethod.Post
            ? new TaxCodeResponse("tn", "VAT_X", 3, "ضريبة 3%", "VAT 3%", "Active", 100, false, "R3")
            : new[]
            {
                new TaxCodeResponse("t0", "VAT_JO_0", 0, "ضريبة المبيعات 0%", "VAT 0%", "Active", 10, false, "R0"),
                new TaxCodeResponse("t16", "VAT_JO_16", 16, "ضريبة المبيعات 16%", "VAT 16%", "Active", 90, true, "R16"),
            });
        var cut = Render<TaxCodes>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("VAT_JO_16", cut.Markup));
        Assert.Contains(L["tax.inUse"], cut.Markup);

        Act(() => cut.FindAll("tr").First(r => r.TextContent.Contains("VAT_JO_16", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["tax.inUseHelp"], cut.Markup));
        var code = cut.FindAll("label").Single(l => l.TextContent.Trim().StartsWith(L["tax.code"], StringComparison.Ordinal)).GetAttribute("for");
        Assert.True(cut.Find("#" + code).HasAttribute("readonly"));
        Assert.DoesNotContain(cut.FindAll(".dialog button"), b => b.TextContent.Contains(L["ml.delete"], StringComparison.Ordinal));
        Assert.Contains(cut.FindAll(".dialog button"), b => b.TextContent.Contains(L["acc.deactivate"], StringComparison.Ordinal));
    }
}
