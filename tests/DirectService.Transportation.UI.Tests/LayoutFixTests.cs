using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Web.Client.Pages;
using DirectService.Transportation.Web.Client.Ui;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 21 Oct 2026: the profile's action buttons no longer share one non-wrapping row (DT-232); table cards scroll sideways inside
/// the card and the IBAN is grouped so it can wrap (DT-234).
/// </summary>
public sealed class LayoutFixTests : PageTestBase
{
    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Profile_puts_save_alone_and_the_account_actions_in_their_own_wrapping_row(string language)
    {
        L.SetLanguage(language);
        Respond("/me/2fa", new TwoFactorStatusResponse(true, false, false, 10, DateTime.UtcNow.AddDays(-1)));
        Respond("/me/sessions", Array.Empty<SessionResponse>());
        Shell.SetMe(Shell.Me! with { TwoFactorEnabled = true });
        var cut = Render<Profile>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["profile.tfaTitle"], Text(cut)));

        var save = cut.FindAll(".formbar").Single(b => b.TextContent.Contains(L["profile.save"], StringComparison.Ordinal));
        Assert.Single(save.QuerySelectorAll("button"));
        var actions = cut.Find(".formbar.start.acct-actions");
        Assert.Contains(L["profile.changePassword"], actions.TextContent, StringComparison.Ordinal);
        Assert.Contains(L["profile.changeEmail"], actions.TextContent, StringComparison.Ordinal);
        // The two-step verification buttons use the same wrapping bar.
        var tfa = cut.FindAll(".formbar.start").Single(b => b.TextContent.Contains(L["profile.tfaChangeApp"], StringComparison.Ordinal));
        Assert.Equal(3, tfa.QuerySelectorAll("button").Length);
    }

    [Fact]
    public void Table_cards_scroll_sideways_inside_the_card_and_fill_cards_keep_their_own_scroller()
    {
        var table = Render<Box>(p => p.Add(x => x.NoPad, true).Add(x => x.Title, "Banks").AddChildContent("<table class=\"grid\"></table>"));
        Assert.NotNull(table.Find("section.box > .box-x > table.grid"));
        var fill = Render<Box>(p => p.Add(x => x.NoPad, true).Add(x => x.Fill, true).AddChildContent("<div class=\"fill\"></div>"));
        Assert.Empty(fill.FindAll(".box-x"));
        var padded = Render<Box>(p => p.AddChildContent("<p>x</p>"));
        Assert.Empty(padded.FindAll(".box-x"));
    }

    [Theory]
    [InlineData("JO94CBJO0010000000000131000302", "JO94 CBJO 0010 0000 0000 0131 0003 02")]
    [InlineData("JO94 CBJO 0010", "JO94 CBJO 0010")]
    [InlineData(null, "")]
    public void The_IBAN_is_shown_in_groups_of_four(string? iban, string shown) => Assert.Equal(shown, Fmt.Iban(iban));
}
