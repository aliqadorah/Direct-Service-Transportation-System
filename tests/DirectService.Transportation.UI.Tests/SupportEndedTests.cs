using System.Net;
using System.Text;
using System.Text.Json;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Shared.Contracts;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Pages;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// A staff member inside a subscriber's company when the support window closes (owner testing 20 Oct 2026, DT-220): the live message or the
/// server's 403 replace the page with the reason and the time, "Back to provider console", no support banner and no company data — never
/// "something went wrong on the server".
/// </summary>
public sealed class SupportEndedTests : PageTestBase
{
    private const string Subscriber = "c9";
    private static readonly DateTime EndedAt = new(2026, 10, 20, 8, 10, 26, DateTimeKind.Utc);

    public SupportEndedTests()
    {
        var staff = new MeResponse("s1", "Direct Service Admin", "staff@example.test", "ar", "yyyy-MM-dd", null, true, null, null, []);
        Respond("/me", staff);
        Respond("/announcements", Array.Empty<object>());
        Respond("/platform/support-access/companies", new[] { new CompanyAccess(Subscriber, "شركة المشترك", null, "Active", "Support", true, ["trn.trips.view", "acc.acct.view"]) });
        RespondFor(Subscriber, "setup-checklist", new SetupChecklistResponse([], 0, 0));
        RespondFor(Subscriber, "notifications", new NotificationPage([], 0));
        RespondFor(Subscriber, "dashboard", new DashboardResponse("mtd", new DashboardFigures(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), 4, null, null, null, null,
            null, null, 662.244m), [], [new DashboardCashCard("CashBox", "cb1", "الصندوق الرئيسي", 212.469m)], [], null, DateTime.UtcNow));
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private static void Page(RenderTreeBuilder b)
    {
        b.OpenComponent(0, typeof(Dashboard));
        b.AddAttribute(1, "Company", Subscriber);
        b.CloseComponent();
    }

    private IRenderedComponent<CompanyLayout> Layout()
    {
        Nav.NavigateTo($"/c/{Subscriber}");
        var cut = Render<CompanyLayout>(p => p.Add(x => x.Body, (RenderFragment)Page));
        cut.WaitForAssertion(() => Assert.Contains("662.244", cut.Markup));
        Assert.Contains(L["banner.support"], cut.Markup);
        return cut;
    }

    private static string Local(DateTime utc) => utc.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private static JsonElement Payload(string reason, string? by, params string[] companies) =>
        JsonSerializer.SerializeToElement(new { reason, endedAt = EndedAt, endedBy = by, companies });

    private void AssertEnded(IRenderedComponent<CompanyLayout> cut, string message)
    {
        cut.WaitForAssertion(() => Assert.Contains(message, cut.Find("[data-testid=support-ended]").TextContent, StringComparison.Ordinal));
        Assert.Equal("/console/support-access", cut.Find("[data-testid=support-ended] a").GetAttribute("href"));
        Assert.Contains(L["support.backToConsole"], cut.Find("[data-testid=support-ended] a").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(L["banner.support"], cut.Markup);       // the support banner is gone
        Assert.DoesNotContain("662.244", cut.Markup);                 // no company data stays on screen
        Assert.DoesNotContain("الصندوق الرئيسي", cut.Markup);
        Assert.DoesNotContain("شركة المشترك", cut.Markup);
        Assert.DoesNotContain(L["ui.serverErrorTitle"], cut.Markup);
        Assert.Null(Shell.Company);
        Assert.Empty(cut.FindAll("aside.side nav a"));                // no menu of the company left
        Assert.Empty(cut.FindAll("[aria-label=\"" + L["shell.switchCompany"] + "\"]"));
    }

    [Fact]
    public void The_live_end_by_the_subscriber_replaces_the_page_with_the_reason_and_the_time()
    {
        var cut = Layout();
        Services.GetRequiredService<LiveUpdates>().RaiseSupportEnded(Payload("support.endedBySubscriber", "Ali Ali", Subscriber, "c10"));
        AssertEnded(cut, L.F("support.endedBySubscriber", Local(EndedAt)));
    }

    [Fact]
    public void A_403_window_expired_on_the_next_request_shows_the_window_end_never_a_server_error()
    {
        var cut = Layout();
        RespondFor(Subscriber, "dashboard", new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent($"{{\"status\":403,\"code\":\"support.windowExpired\",\"detail\":\"The support window ended at 08:10 UTC.\",\"traceId\":\"0HN:0000000D\",\"endedAt\":\"{EndedAt:O}\"}}",
                Encoding.UTF8, "application/problem+json"),
        });
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["dash.today"], StringComparison.Ordinal)).Click());   // the dashboard reloads
        AssertEnded(cut, L.F("support.windowEnded", Local(EndedAt)));
    }

    [Fact]
    public void Opening_the_company_after_the_end_shows_the_reason_not_not_found()
    {
        Respond("/platform/support-access/companies", Array.Empty<CompanyAccess>());   // the window is no longer open
        RespondFor(Subscriber, "setup-checklist", new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent($"{{\"status\":403,\"code\":\"support.endedBySubscriber\",\"traceId\":\"0HN:0000000E\",\"endedAt\":\"{EndedAt:O}\",\"endedBy\":\"Ali Ali\"}}",
                Encoding.UTF8, "application/problem+json"),
        });
        Nav.NavigateTo($"/c/{Subscriber}");
        var cut = Render<CompanyLayout>(p => p.Add(x => x.Body, (RenderFragment)Page));
        AssertEnded(cut, L.F("support.endedBySubscriber", Local(EndedAt)));
        Assert.DoesNotContain(L["page.notFound"], cut.Markup);
    }

    [Fact]
    public void A_live_end_of_another_subscribers_window_leaves_the_page_as_it_is()
    {
        var cut = Layout();
        Services.GetRequiredService<LiveUpdates>().RaiseSupportEnded(Payload("support.windowExpired", null, "c77"));
        cut.WaitForAssertion(() => Assert.Contains("662.244", cut.Markup));
        Assert.Empty(cut.FindAll("[data-testid=support-ended]"));
        Assert.Contains(L["banner.support"], cut.Markup);
    }

    [Fact]
    public void The_message_is_translated()
    {
        L.SetLanguage("en");
        var cut = Render<SupportEndedView>(p => p.Add(x => x.End, new ShellState.SupportEnd("support.endedBySubscriber", EndedAt, "Ali Ali")));
        Assert.Contains($"The subscriber ended the support session at {Local(EndedAt)}.", cut.Find("p").TextContent, StringComparison.Ordinal);
        Assert.Contains("Back to provider console", cut.Markup, StringComparison.Ordinal);
        L.SetLanguage("ar");
        cut.Render();
        Assert.Contains($"أنهى المشترك جلسة الدعم في {Local(EndedAt)}.", cut.Find("p").TextContent, StringComparison.Ordinal);
        Assert.Contains("العودة إلى لوحة مزوّد الخدمة", cut.Markup, StringComparison.Ordinal);
    }
}
