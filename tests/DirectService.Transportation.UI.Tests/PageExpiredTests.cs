using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 20 Oct 2026, item 1 (DT-221): a change refused for its antiforgery token is sent again once with a fresh token; refused
/// again (the page is out of date, e.g. signed in or out in another tab), the shell says so in the user's language with a "Reload" button —
/// never a raw error.
/// </summary>
public sealed class PageExpiredTests : PageTestBase
{
    private int _posts;
    private int _refusals;

    public PageExpiredTests()
    {
        Respond("/me", Shell.Me);
        Respond("/announcements", Array.Empty<object>());
        Respond("setup-checklist", new SetupChecklistResponse([], 0, 0));
        Respond("notifications", new NotificationPage([], 0));
        Respond("notes", _ =>
        {
            _posts++;
            return _posts <= _refusals ? Refused() : new { ok = true };
        });
    }

    private static HttpResponseMessage Refused() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent("{\"status\":400,\"code\":\"auth.antiforgery\",\"detail\":\"The page has expired. Reload it and try again.\"}",
            Encoding.UTF8, "application/problem+json"),
    };

    private IRenderedComponent<CompanyLayout> Layout()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/c/{Company}");
        var cut = Render<CompanyLayout>(p => p.Add(x => x.Body, (RenderFragment)(b => b.AddContent(0, "page body"))));
        cut.WaitForAssertion(() => Assert.Contains("page body", cut.Markup));
        return cut;
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task Refused_again_after_a_fresh_token_shows_the_reload_message(string language)
    {
        L.SetLanguage(language);
        _refusals = 2;
        var cut = Layout();
        var api = Services.GetRequiredService<Api>();
        await Assert.ThrowsAsync<ApiException>(() => cut.InvokeAsync(() => api.PostAsync(Api.C(Company, "notes"), new { text = "x" })));
        Assert.Equal(2, _posts);   // sent once more with a fresh token, then given up
        cut.WaitForAssertion(() => Assert.Contains(L["shell.pageExpired"], Text(cut)));
        Assert.DoesNotContain("antiforgery", cut.Markup, StringComparison.OrdinalIgnoreCase);

        Act(() => cut.FindAll("button").Single(b => b.TextContent.Contains(L["shell.reload"], StringComparison.Ordinal)).Click());
        var nav = (Bunit.TestDoubles.BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        Assert.True(nav.History.First().Options.ForceLoad);   // a full reload: new token, current sign-in
        Assert.EndsWith($"/c/{Company}", nav.History.First().Uri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_refusal_is_retried_silently()
    {
        _refusals = 1;
        var cut = Layout();
        var api = Services.GetRequiredService<Api>();
        await cut.InvokeAsync(() => api.PostAsync(Api.C(Company, "notes"), new { text = "x" }));
        Assert.Equal(2, _posts);
        Assert.DoesNotContain(L["shell.pageExpired"], Text(cut));
    }
}
