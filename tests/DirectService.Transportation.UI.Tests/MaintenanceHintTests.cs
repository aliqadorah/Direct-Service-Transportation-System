using DirectService.Transportation.Contracts.Accounting;
using DirectService.Accounting.Contracts.Accounting;
using DirectService.Transportation.Web.Client.Pages.Accounting;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// DT-240: in the maintenance zone, "Delete selected entries" with nothing ticked shows a hint where the preview will be — the server is not
/// asked (its refusal named the internal field «entryIds»); once entries are ticked the preview is shown as before. The other actions show
/// their preview at once.
/// </summary>
public sealed class MaintenanceHintTests : PageTestBase
{
    public MaintenanceHintTests()
    {
        Respond("accounting/maintenance/log", Array.Empty<MaintenanceLogEntry>());
        Respond("accounting/maintenance/", new MaintenancePreview("x", new Dictionary<string, int> { ["JournalEntries"] = 1 }, 0, [], "حذف"));
    }

    private static JournalEntrySummary Entry(string id, string number) =>
        new(id, number, new DateOnly(2026, 10, 20), "2026-10", "Fuel", "ACC", "Manual", true, "Posted", 50m, 50m, null, null);

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Nothing_ticked_shows_a_hint_and_ticking_entries_shows_the_preview(string language)
    {
        L.SetLanguage(language);
        Respond("accounting/journal-entries", new PagedResult<JournalEntrySummary>([Entry("je1", "JE-1"), Entry("je2", "JE-2")], 1, 200, 2));
        var cut = Render<Maintenance>(p => p.Add(x => x.Company, Company));
        Act(() => cut.FindAll("input[type=radio]")[0].Change(true));   // delete selected entries

        cut.WaitForAssertion(() => Assert.Contains(L["mnt.pickHint"], cut.Find(".mnt-hint").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain(Requests, r => r.Path.Contains("/preview", StringComparison.Ordinal));
        Assert.Empty(cut.FindAll(".banner.err, [role=alert]"));
        Assert.DoesNotContain("entryIds", cut.Markup, StringComparison.Ordinal);

        Act(() => cut.FindAll("tbody input[type=checkbox]")[0].Change(true));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == "accounting/maintenance/delete-entries/preview"));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".mnt-hint")));
        Assert.Contains("je1", Requests.Last(r => r.Path.EndsWith("/preview", StringComparison.Ordinal)).Body!, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#mnt-word")));
    }

    /// <summary>DT-246: the list says how many entries are ticked, and only the first tick brings the (stacked) preview into view.</summary>
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_count_of_ticked_entries_is_shown_and_the_first_tick_reveals_the_preview(string language)
    {
        L.SetLanguage(language);
        Respond("accounting/journal-entries", new PagedResult<JournalEntrySummary>([Entry("je1", "JE-1"), Entry("je2", "JE-2")], 1, 200, 2));
        var cut = Render<Maintenance>(p => p.Add(x => x.Company, Company));
        Act(() => cut.FindAll("input[type=radio]")[0].Change(true));
        cut.WaitForAssertion(() => Assert.Equal(L["mnt.pickedNone"], cut.Find(".mnt-picked[role=status]").TextContent.Trim()));
        int Reveals() => UiModule.Invocations.Count(i => i.Identifier == "revealStacked");

        Act(() => cut.FindAll("tbody input[type=checkbox]")[0].Change(true));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#mnt-word")));
        Assert.Equal(L.F("mnt.picked", "1"), cut.Find(".mnt-picked").TextContent.Trim());
        cut.WaitForAssertion(() => Assert.Equal(1, Reveals()));
        Assert.Equal(".mnt-preview", UiModule.Invocations.Last(i => i.Identifier == "revealStacked").Arguments[0]);
        Assert.Contains("mnt-preview", cut.Find("section.box.mnt-preview").ClassName, StringComparison.Ordinal);

        // A second tick updates the count and the preview but does not move the page again.
        Act(() => cut.FindAll("tbody input[type=checkbox]")[1].Change(true));
        cut.WaitForAssertion(() => Assert.Equal(L.F("mnt.picked", "2"), cut.Find(".mnt-picked").TextContent.Trim()));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#mnt-word")));
        Assert.Equal(1, Reveals());
    }

    [Fact]
    public void Without_manual_entries_the_hint_says_there_are_none()
    {
        Respond("accounting/journal-entries", new PagedResult<JournalEntrySummary>([], 1, 200, 0));
        var cut = Render<Maintenance>(p => p.Add(x => x.Company, Company));
        Act(() => cut.FindAll("input[type=radio]")[0].Change(true));
        cut.WaitForAssertion(() => Assert.Equal(L["mnt.noManual"], cut.Find(".mnt-hint").TextContent.Trim()));
        Assert.DoesNotContain(Requests, r => r.Path.Contains("/preview", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "delete-all-entries")]
    [InlineData(2, "replace-chart")]
    [InlineData(3, "reset")]
    public void The_other_actions_show_their_preview_at_once(int index, string action)
    {
        var cut = Render<Maintenance>(p => p.Add(x => x.Company, Company));
        Act(() => cut.FindAll("input[type=radio]")[index].Change(true));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == $"accounting/maintenance/{action}/preview"));
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#mnt-word")));
        Assert.Empty(cut.FindAll(".mnt-hint"));
    }
}
