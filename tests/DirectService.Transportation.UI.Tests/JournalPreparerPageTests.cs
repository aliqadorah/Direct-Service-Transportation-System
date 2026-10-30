using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Platform.UI.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Transportation.Web.Client.Pages.Accounting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>DT-201: a user with "prepare" (acct.post) but not "Post journal entries" saves drafts and templates; the Post button is not shown.</summary>
public sealed class JournalPreparerPageTests : PageTestBase
{
    private static readonly AccountRef Rent = new("a5", "5202", "الإيجارات", "Rent");

    public JournalPreparerPageTests() : base(PermissionKeys.AcctView, PermissionKeys.JeView, PermissionKeys.AcctPost)
    {
        Respond("accounting/accounts/options", new[] { new AccountOption(Rent, "Expense", false, null) });
        Respond("accounting/cost-centres/options", Array.Empty<object>());
        Respond("apar/parties/options", Array.Empty<object>());
    }

    [Fact]
    public void Preparer_sees_save_as_draft_and_templates_but_no_post()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}/accounting/journal/new");
        var cut = Render<JournalEntryForm>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["je.lines"], cut.Markup));

        var buttons = cut.FindAll("button").Select(b => b.TextContent).ToList();
        Assert.DoesNotContain(buttons, b => b.Contains(L["je.post"], StringComparison.Ordinal));
        var draft = cut.FindAll("button").Single(b => b.TextContent.Contains(L["je.saveDraft"], StringComparison.Ordinal));
        Assert.Contains("pri", draft.ClassList);
        Assert.Contains(buttons, b => b.Contains(L["tpl.use"], StringComparison.Ordinal));
        Assert.Contains(L["je.prepareOnly"], cut.Markup);
    }

    [Fact]
    public void Journal_import_needs_the_posting_permission()
    {
        Assert.False(NavModel.RouteAllowed([PermissionKeys.AcctPost], $"/c/{Company}/accounting/journal/import"));
        Assert.True(NavModel.RouteAllowed([PermissionKeys.AcctPost], $"/c/{Company}/accounting/journal/new"));
        Assert.True(NavModel.RouteAllowed([PermissionKeys.AcctPost, PermissionKeys.AcctPostEntry], $"/c/{Company}/accounting/journal/import"));
    }
}
