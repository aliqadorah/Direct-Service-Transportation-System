using DirectService.Transportation.Contracts.EInvoicing;
using DirectService.Transportation.Web.Client.Pages.Settings;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 20 Oct 2026, item 3 (DT-223): the JoFotara registration type offers only "Income tax" and "General sales tax" (special sales
/// tax is not supported), and a short description of the chosen type is shown under the list, in Arabic and English.
/// </summary>
public sealed class EInvoiceSettingsPageTests : PageTestBase
{
    public EInvoiceSettingsPageTests() =>
        Respond("settings/e-invoicing", new EInvoiceSettingsResponse(false, "JoFotara", "123456", EInvoiceCodes.Sales, "1", false, false,
            EInvoiceCodes.Test, 0, ["JoFotara"], "AAAA"));

    private IRenderedComponent<EInvoiceSettings> Page()
    {
        var cut = Render<EInvoiceSettings>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("select")));
        return cut;
    }

    private AngleSharp.Dom.IElement Registration<T>(IRenderedComponent<T> cut) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("select").Single(s => s.QuerySelectorAll("option").Any(o => o.TextContent.Trim() == L["eis.reg.Income"]));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Only_income_and_general_sales_tax_are_offered_each_with_its_description(string language)
    {
        L.SetLanguage(language);
        var cut = Page();
        var options = Registration(cut).QuerySelectorAll("option").Where(o => !string.IsNullOrEmpty(o.GetAttribute("value"))).Select(o => o.TextContent.Trim()).ToList();
        Assert.Equal([L["eis.reg.Income"], L["eis.reg.Sales"]], options);
        Assert.DoesNotContain(L["eis.reg.Special"], Text(cut));

        Assert.Contains(L["eis.regHelp.Sales"], Text(cut));   // the saved type, explained
        Assert.DoesNotContain(L["eis.regHelp.Income"], Text(cut));
        Registration(cut).Change("0");   // the first option: income tax
        cut.WaitForAssertion(() => Assert.Contains(L["eis.regHelp.Income"], Text(cut)));
        Assert.DoesNotContain(L["eis.regHelp.Sales"], Text(cut));
    }

    [Fact]
    public void The_descriptions_say_what_the_system_sends()
    {
        L.SetLanguage("en");
        Assert.Contains("not registered for sales tax", L["eis.regHelp.Income"], StringComparison.Ordinal);
        Assert.Contains("without sales tax", L["eis.regHelp.Income"], StringComparison.Ordinal);
        Assert.Contains("16%", L["eis.regHelp.Sales"], StringComparison.Ordinal);
        L.SetLanguage("ar");
        Assert.Contains("ضريبة الدخل", L["eis.reg.Income"], StringComparison.Ordinal);
        Assert.Contains("16%", L["eis.regHelp.Sales"], StringComparison.Ordinal);
    }
}
