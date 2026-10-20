using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Web.Client.Pages;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 20 Oct 2026, item 4 (DT-224): the "Account mapping" step says in words how many posting accounts are still to be linked and
/// lists them by name on request — never the internal posting-key codes.
/// </summary>
public sealed class SetupChecklistPageTests : PageTestBase
{
    private static readonly string[] Names = ["إيرادات النقل", "الخصم المسموح به", "ذمم العملاء"];

    public SetupChecklistPageTests() =>
        Respond("setup-checklist", new SetupChecklistResponse(
        [
            new SetupStep("account", "إنشاء الحساب", "Done", null, "/me", false),
            new SetupStep("mapping", "ربط الحسابات", "InProgress", "3 من حسابات الترحيل لم تُربط بعد بدليل الحسابات", "/accounting/posting-keys", false, Names),
            new SetupStep("users", "المستخدمون", "NotStarted", null, "/users", false),
        ], 1, 3));

    [Fact]
    public void Mapping_step_shows_the_count_and_an_expandable_list_of_names()
    {
        var cut = Render<SetupChecklist>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("ربط الحسابات", Text(cut)));
        Assert.Contains("3 من حسابات الترحيل لم تُربط بعد بدليل الحسابات", Text(cut));
        var list = cut.Find("[data-testid=setup-items-mapping]");
        Assert.Equal("details", list.TagName, StringComparer.OrdinalIgnoreCase);   // closed until asked
        Assert.False(list.HasAttribute("open"));
        Assert.Equal(L["setup.showList"], list.QuerySelector("summary")!.TextContent.Trim());
        Assert.Equal(Names, list.QuerySelectorAll("li").Select(li => li.TextContent).ToArray());
        Assert.DoesNotContain("TRN.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("ACC.", cut.Markup, StringComparison.Ordinal);
        Assert.Single(cut.FindAll("details"));   // steps without items have no list
    }
}
