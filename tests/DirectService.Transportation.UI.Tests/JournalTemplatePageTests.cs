using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Revision 6 part B, feature 2: "Use template" and "Save as template" on the new entry, the templates list and the template page.</summary>
public sealed class JournalTemplatePageTests : PageTestBase
{
    private static readonly AccountRef Rent = new("a5", "5202", "الإيجارات", "Rent");
    private static readonly AccountRef Cash = new("a2", "1101", "الصندوق الرئيسي", "Main cash box");

    private static readonly JournalTemplateResponse Template = new("t1", "إيجار شهري", "إيجار المكتب", "عقد 12",
        [new JournalTemplateLineResponse(Rent, 500, 0, new Ref("cc1", "HQ – Head office"), "إيجار الشهر"), new JournalTemplateLineResponse(Cash, 0, 500, null, null)],
        DateTime.UtcNow, "TV1");

    public JournalTemplatePageTests()
    {
        Respond("accounting/accounts/options", new[] { new AccountOption(Rent, "Expense", false, null), new AccountOption(Cash, "Asset", false, null) });
        Respond("accounting/cost-centres/options", new[] { new { id = "cc1", name = "HQ – Head office" } });
        Respond("apar/parties/options", Array.Empty<object>());
        Respond("accounting/journal-templates/t1", Template);
        Respond("accounting/journal-templates", r => r.Method == HttpMethod.Post ? Template : new[]
        {
            new JournalTemplateSummary("t1", "إيجار شهري", "إيجار المكتب", "عقد 12", 2, true, DateTime.UtcNow, "Test User", "TV1"),
        });
    }

    private IRenderedComponent<JournalEntryForm> NewEntry(string query = "")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new{query}");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.lines"], cut.Markup));
        return cut;
    }

    private static string Value(IRenderedComponent<JournalEntryForm> cut, string label) =>
        cut.Find("#" + cut.FindAll("label").Single(l => l.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).GetAttribute("for")).GetAttribute("value") ?? "";

    [Fact]
    public void Use_template_fills_description_reference_and_lines()
    {
        var cut = NewEntry();
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["tpl.use"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("إيجار شهري", cut.Markup));
        Act(() => cut.FindAll("tr").First(r => r.TextContent.Contains("إيجار شهري", StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Equal("إيجار المكتب", Value(cut, L["je.description"])));
        Assert.Equal("عقد 12", Value(cut, L["je.reference"]));
        Assert.Contains("500.000", cut.Markup);
        Assert.Contains(L["je.balanced"], cut.Markup);
    }

    [Fact]
    public void Template_link_opens_a_filled_new_entry()
    {
        var cut = NewEntry("?template=t1");
        cut.WaitForAssertion(() => Assert.Equal("إيجار المكتب", Value(cut, L["je.description"])));
        Assert.Contains(Requests, r => r.Path == "accounting/journal-templates/t1");
    }

    [Fact]
    public void Save_as_template_keeps_the_lines_without_amounts_when_asked()
    {
        var cut = NewEntry("?template=t1");
        cut.WaitForAssertion(() => Assert.Equal("إيجار المكتب", Value(cut, L["je.description"])));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["tpl.saveAs"], StringComparison.Ordinal)).Click());
        var name = cut.FindAll("label").Single(l => l.TextContent.Trim().StartsWith(L["tpl.name"], StringComparison.Ordinal)).GetAttribute("for");
        Act(() => cut.Find("#" + name).Change("Rent copy"));
        Act(() =>
        {
            var amounts = cut.FindAll("label").Single(l => l.TextContent.Contains(L["tpl.withAmounts"], StringComparison.Ordinal));
            (amounts.GetAttribute("for") is { } id ? cut.Find("#" + id) : amounts.QuerySelector("input")!).Change(false);
        });
        Act(() => cut.Find("form#tpl-save").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "accounting/journal-templates"));
        var body = Requests.Single(r => r.Method == "POST" && r.Path == "accounting/journal-templates").Body!;
        Assert.Contains("\"name\":\"Rent copy\"", body, StringComparison.Ordinal);
        Assert.Contains("\"reference\":\"عقد 12\"", System.Text.RegularExpressions.Regex.Unescape(body), StringComparison.Ordinal);
        Assert.Contains("\"costCentreId\":\"cc1\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("\"debit\":500", body, StringComparison.Ordinal);
        Assert.DoesNotContain(Requests, r => r.Method == "POST" && r.Path.StartsWith("accounting/journal-entries", StringComparison.Ordinal));   // nothing posted
    }

    [Fact]
    public void Templates_list_renames_with_the_lines_unchanged()
    {
        var cut = Render<JournalTemplates>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("إيجار شهري", cut.Markup));
        Act(() => cut.Find($"button[aria-label='{L.F("tpl.renameOne", "إيجار شهري")}']").Click());
        var name = cut.FindAll("label").Single(l => l.TextContent.Trim().StartsWith(L["tpl.name"], StringComparison.Ordinal)).GetAttribute("for");
        Act(() => cut.Find("#" + name).Change("Rent"));
        Act(() => cut.Find("form#tpl-rename").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "accounting/journal-templates/t1"));
        var body = Requests.Single(r => r.Method == "PUT").Body!;
        Assert.Contains("\"name\":\"Rent\"", body, StringComparison.Ordinal);
        Assert.Contains("\"rowVersion\":\"TV1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"debit\":500", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Template_page_edits_and_uses_the_template()
    {
        var cut = Render<JournalTemplateForm>(p => p.Add(x => x.Company, Company).Add(x => x.Id, "t1"));
        cut.WaitForAssertion(() => Assert.Contains(L["tpl.lineDescription"], cut.Markup));
        Assert.Contains(L["tpl.formHelp"], cut.Markup);
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "accounting/journal-templates/t1"));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["tpl.useShort"], StringComparison.Ordinal)).Click());
        Assert.EndsWith("accounting/journal/new?template=t1", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }
}
