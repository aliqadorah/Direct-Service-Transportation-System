using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Side menu (owner request 21 Oct 2026): every entry has its own icon from the sprite (DT-235); the menu collapses to an icon rail whose
/// sections show their name and entries (DT-236); collapsed / open sections are remembered per user on this device and the current page's
/// section opens by itself (DT-237); long sections are split into labelled parts (DT-238); Ctrl+K finds menu pages by their Arabic or
/// English name (DT-239).
/// </summary>
public sealed partial class NavMenuTests : PageTestBase
{
    private IRenderedComponent<SideNav> RenderMenu(string? at = null)
    {
        if (at is not null) Services.GetRequiredService<NavigationManager>().NavigateTo(at);
        return Render<SideNav>(p => p.Add(x => x.Groups, NavModel.For(Shell.Permissions, setupIncomplete: false)).Add(x => x.Company, Company));
    }

    private static HashSet<string> SpriteIcons()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src"))) dir = dir.Parent;
        var svg = File.ReadAllText(Path.Combine(dir!.FullName, "src", "DirectService.Transportation.Web.Client", "wwwroot", "img", "icons.svg"));
        return [.. SymbolId().Matches(svg).Select(m => m.Groups[1].Value)];
    }

    [GeneratedRegex("<symbol [^>]*id=\"i-([a-z0-9-]+)\"")]
    private static partial Regex SymbolId();

    [Fact]
    public void Every_entry_has_its_own_icon_from_the_same_sprite_and_none_repeats_within_a_section()
    {
        var sprite = SpriteIcons();
        foreach (var g in NavModel.Company)
        {
            Assert.Contains(g.Icon, sprite);
            Assert.All(g.Items, i => Assert.True(sprite.Contains(i.Icon), $"{i.Key}: i-{i.Icon} missing"));
            Assert.Equal(g.Items.Count, g.Items.Select(i => i.Icon).Distinct().Count());
        }
        Assert.All(NavModel.Console, c => Assert.Contains(c.Icon, sprite));
        Assert.Equal(NavModel.Console.Count, NavModel.Console.Select(c => c.Icon).Distinct().Count());
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Entries_show_their_icon_and_name_and_the_current_one_is_marked(string language)
    {
        L.SetLanguage(language);
        var cut = RenderMenu("/c/c1/accounting/trial-balance");

        var accounting = cut.FindAll(".sec").Single(s => s.QuerySelector("button.g")?.TextContent.Contains(L["nav.accounting"], StringComparison.Ordinal) == true);
        Assert.Contains("open", accounting.ClassList);   // the current page's section opened by itself
        Assert.Contains("has-cur", accounting.ClassList);
        var items = accounting.QuerySelectorAll("a.it");
        Assert.Equal(NavModel.Company.Single(g => g.Key == "nav.accounting").Items.Count, items.Length);
        foreach (var a in items)
        {
            var use = a.QuerySelector("svg.ic use")!.GetAttribute("href")!;
            Assert.StartsWith("img/icons.svg#i-", use, StringComparison.Ordinal);
            Assert.Equal("true", a.QuerySelector("svg.ic")!.GetAttribute("aria-hidden"));   // decorative: the name is the link's text
        }
        var current = Assert.Single(cut.FindAll("a[aria-current=page]"));
        Assert.Equal(L["nav.trialBalance"], current.TextContent.Trim());
        Assert.Contains("cur", current.ClassList);
        Assert.Contains("#i-scale", current.QuerySelector("use")!.GetAttribute("href"), StringComparison.Ordinal);
    }

    private static readonly string[] AccountingParts = ["nav.part.entries", "nav.part.balances", "nav.part.analysis", "nav.part.acctSetup"];

    [Fact]
    public void Long_sections_are_split_into_labelled_parts()
    {
        foreach (var g in NavModel.Company.Where(g => g.Items.Count > 8))
            Assert.True(g.Items.Count(i => i.Part is null) <= 1, $"{g.Key}: entries outside a part");
        Assert.All(NavModel.Company.SelectMany(g => g.Items).Where(i => i.Part is not null), i => Assert.True(UiText.All.ContainsKey(i.Part!), i.Part));

        var cut = RenderMenu("/c/c1/accounting/trial-balance");
        var parts = cut.FindAll(".sec.open [role=group] > .part").Select(p => p.TextContent).ToList();
        Assert.Equal(AccountingParts.Select(k => L[k]), parts);
        // The dashboard stays first, outside the parts.
        Assert.Equal(L["nav.accountingDashboard"], cut.Find(".sec.open a.it").TextContent.Trim());
    }

    [Fact]
    public void Several_sections_stay_open_and_each_change_is_saved_for_this_user()
    {
        var cut = RenderMenu("/c/c1/trips");
        cut.FindAll("button.g").Single(b => b.TextContent.Contains(L["nav.reports"], StringComparison.Ordinal)).Click();

        Assert.Equal(2, cut.FindAll(".sec.open").Count);
        var saved = UiModule.Invocations.Where(i => i.Identifier == "setPref").Last();
        Assert.Equal("nav.u1", saved.Arguments[0]);
        Assert.Equal("mini=0;open=nav.operations,nav.reports", saved.Arguments[1]);
    }

    [Fact]
    public void Collapse_shows_icons_only_with_a_name_and_flyout_for_every_section_and_works_with_the_keyboard()
    {
        var cut = RenderMenu("/c/c1/trips");
        var toggle = cut.Find("button.collapse");
        Assert.Equal(L["shell.collapseMenu"], toggle.GetAttribute("aria-label"));
        toggle.Click();

        Assert.Contains("mini", cut.Find("aside.side").ClassList);
        Assert.Equal(L["shell.expandMenu"], cut.Find("button.collapse").GetAttribute("aria-label"));
        Assert.Equal("false", cut.Find("button.collapse").GetAttribute("aria-expanded"));
        Assert.Equal("nav.u1", UiModule.Invocations.Last(i => i.Identifier == "setPref").Arguments[0]);
        Assert.StartsWith("mini=1;", (string)UiModule.Invocations.Last(i => i.Identifier == "setPref").Arguments[1]!, StringComparison.Ordinal);
        // Every section and single entry has its name (tooltip) beside the icon; sections list all their entries in the flyout.
        foreach (var sec in cut.FindAll(".sec")) Assert.NotNull(sec.QuerySelector(".fly .ft"));
        var billing = cut.FindAll(".sec").Single(s => s.QuerySelector(".ft")!.TextContent == L["nav.billing"]);
        Assert.Equal(8, billing.QuerySelectorAll(".fly a.it").Length);
        Assert.Equal(L["nav.billing"], billing.QuerySelector(".fly")!.GetAttribute("aria-label"));

        // Enter / click opens the section's entries and moves focus to the first; Escape closes and returns to the section.
        var button = billing.QuerySelector("button.g")!;
        Assert.Equal("false", button.GetAttribute("aria-expanded"));
        cut.FindAll(".sec").Single(s => s.QuerySelector(".ft")!.TextContent == L["nav.billing"]).QuerySelector("button.g")!.Click();
        var flown = cut.Find(".sec.flown");
        Assert.Equal("true", flown.QuerySelector("button.g")!.GetAttribute("aria-expanded"));
        // Placed inside the window and the first entry focused.
        cut.WaitForAssertion(() => Assert.Contains(UiModule.Invocations, i => i.Identifier == "placeFly" && (string?)i.Arguments[0] == flown.QuerySelector(".fly")!.Id && (bool)i.Arguments[1]!));
        Assert.Contains(UiModule.Invocations, i => i.Identifier == "onOutside");
        cut.Find("nav").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Empty(cut.FindAll(".sec.flown"));
        Assert.Contains(UiModule.Invocations, i => i.Identifier == "focusById" && (string?)i.Arguments[0] == button.Id);
        Assert.Contains(UiModule.Invocations, i => i.Identifier == "offOutside");
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Remembered_state_is_restored_and_the_current_section_still_opens(string language)
    {
        L.SetLanguage(language);
        UiModule.Setup<string?>("getPref", "nav.u1").SetResult("mini=1;open=nav.reports,nav.unknown");
        var cut = RenderMenu("/c/c1/masters/drivers");

        cut.WaitForAssertion(() => Assert.Contains("mini", cut.Find("aside.side").ClassList));
        var open = cut.FindAll(".sec.open").Select(s => s.QuerySelector(".ft")!.TextContent).ToList();
        Assert.Equal(new[] { L["nav.reports"], L["nav.masterData"] }, open);
        Assert.Equal(L["shell.expandMenu"], cut.Find("button.collapse").GetAttribute("aria-label"));
    }

    [Fact]
    public void An_unreadable_preference_leaves_the_menu_expanded()
    {
        UiModule.Setup<string?>("getPref", "nav.u1").SetResult("garbage;;=;open=");
        var cut = RenderMenu("/c/c1/trips");
        cut.WaitForAssertion(() => Assert.Contains(UiModule.Invocations, i => i.Identifier == "getPref"));
        Assert.DoesNotContain("mini", cut.Find("aside.side").ClassList);
        Assert.Single(cut.FindAll(".sec.open"));   // the current page's section
    }

    [Fact]
    public void Provider_console_entries_have_icons_and_names_when_collapsed()
    {
        var cut = Render<SideNav>(p => p.Add(x => x.Provider, true));
        cut.Find("button.collapse").Click();
        var links = cut.FindAll(".sec > a.g");
        Assert.Equal(NavModel.Console.Count, links.Count);
        Assert.All(links, a => Assert.NotNull(a.QuerySelector("svg.ic use")));
        Assert.Equal(NavModel.Console.Select(c => L[c.Key]), cut.FindAll(".sec .fly .ft").Select(f => f.TextContent));
    }

    [Theory]
    [InlineData("ar", "Trial balance")]
    [InlineData("ar", "ميزان المراجعة")]
    [InlineData("en", "ميزان المراجعة")]
    [InlineData("en", "trial")]
    public async Task Ctrl_K_finds_menu_pages_by_their_Arabic_or_English_name(string language, string typed)
    {
        L.SetLanguage(language);
        Shell.SetMe(Shell.Me! with { LanguageCode = language });   // the shell starts in the profile's language
        var cut = Render<AppShell>();
        await cut.InvokeAsync(cut.Instance.OpenCommandBar);
        cut.Find("input[role=combobox]").Input(typed);

        var option = Assert.Single(cut.FindAll("li[role=option]"));
        Assert.Contains(L["nav.trialBalance"], option.TextContent, StringComparison.Ordinal);
        Assert.Contains(L["nav.accounting"], option.TextContent, StringComparison.Ordinal);
    }
}

/// <summary>Side menu and Ctrl+K for a role with trip permissions only (DT-235…DT-239): nothing the role cannot open is shown or found.</summary>
public sealed class NavMenuRoleTests() : PageTestBase(PermissionKeys.TripsView)
{
    private static readonly string[] TripPages = ["/c/c1/trips", "/c/c1/trips/shortage"];

    [Fact]
    public void Only_the_entries_of_the_role_are_shown_expanded_or_collapsed()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/c/c1/trips");
        var cut = Render<SideNav>(p => p.Add(x => x.Groups, NavModel.For(Shell.Permissions, setupIncomplete: false)).Add(x => x.Company, Company));
        cut.Find("button.collapse").Click();

        var hrefs = cut.FindAll("a.it").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(TripPages, hrefs.Where(h => h!.Contains("/trips", StringComparison.Ordinal)));
        Assert.DoesNotContain(hrefs, h => h!.Contains("accounting", StringComparison.Ordinal) || h.Contains("invoices", StringComparison.Ordinal)
            || h.Contains("trips/new", StringComparison.Ordinal) || h.Contains("settings", StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll(".ft"), f => f.TextContent == L["nav.accounting"] || f.TextContent == L["nav.tenant"]);
    }

    [Theory]
    [InlineData("trial")]
    [InlineData("ميزان")]
    [InlineData("New trip")]
    public async Task Ctrl_K_never_finds_a_page_the_role_cannot_open(string typed)
    {
        var cut = Render<AppShell>();
        await cut.InvokeAsync(cut.Instance.OpenCommandBar);
        cut.Find("input[role=combobox]").Input(typed);
        Assert.Empty(cut.FindAll("li[role=option]"));
    }

    [Fact]
    public async Task Ctrl_K_finds_the_pages_the_role_can_open_in_either_language()
    {
        var cut = Render<AppShell>();
        await cut.InvokeAsync(cut.Instance.OpenCommandBar);
        cut.Find("input[role=combobox]").Input("shortage");
        Assert.Contains(L["nav.shortage"], Assert.Single(cut.FindAll("li[role=option]")).TextContent, StringComparison.Ordinal);
    }
}
