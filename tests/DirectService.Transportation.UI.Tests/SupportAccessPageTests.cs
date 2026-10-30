using System.Net;
using System.Text;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Pages.Console;
using DirectService.Transportation.Web.Client.Pages.Tenant;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Support access screens (owner testing 19 Oct 2026, DT-213…DT-216): the console's request dialog tells same-named subscribers apart and shows
/// the choice again before sending; the tenant's approve/refuse dialogs show the server's specific message; the console badge shows the real
/// environment.
/// </summary>
public sealed class SupportAccessPageTests : PageTestBase
{
    private static readonly DateTime Signed1 = new(2026, 10, 12, 6, 42, 0, DateTimeKind.Utc);
    private static readonly DateTime Signed2 = new(2026, 10, 12, 7, 2, 0, DateTimeKind.Utc);

    private static readonly SupportTenantOption Ali1 = new("t1", "Ali Transportation", "ali@gmail.com", "TRIAL", "Trial", Signed1, "Trial", null);
    private static readonly SupportTenantOption Ali2 = new("t2", "Ali Transportation", "aliabdelqader311@gmail.com", "TRIAL", "Trial", Signed2, "Trial", null);

    private static readonly SupportAccessResponse Pending =
        new("g1", "Ali Transportation", "Direct Service Admin", "test", "Requested", null, null, null, new DateTime(2026, 10, 19, 11, 32, 0, DateTimeKind.Utc), "A");

    private static HttpResponseMessage Problem(int status, string code, string detail) => new((HttpStatusCode)status)
    {
        Content = new StringContent($"{{\"status\":{status},\"code\":\"{code}\",\"detail\":\"{detail}\"}}", Encoding.UTF8, "application/problem+json"),
    };

    private void Console(IReadOnlyList<SupportAccessResponse>? rows = null)
    {
        Respond("/platform/support-access/companies", Array.Empty<CompanyAccess>());
        Respond("/platform/support-access/tenants", new[] { Ali1, Ali2 });
        Respond("/platform/support-access", r => r.Method == HttpMethod.Post ? Pending : (object)(rows ?? [Pending]));
    }

    [Fact]
    public void Request_dialog_tells_same_named_subscribers_apart_and_sends_only_the_one_shown()
    {
        Console();
        var cut = Render<SupportAccess>();
        cut.WaitForAssertion(() => Assert.Contains("Ali Transportation", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["con.requestSupport"], StringComparison.Ordinal)).Click());

        var send = () => cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["con.sendRequest"], StringComparison.Ordinal));
        Assert.True(send().HasAttribute("disabled"));

        Act(() => cut.Find("#support-tenant").Input("Ali"));
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("[role=option]").Count));
        var options = cut.FindAll("[role=option] span").Select(o => o.TextContent).ToList();
        // Each entry: name · owner email · plan · signed-up date — the two "Ali Transportation" read differently.
        Assert.Equal($"Ali Transportation · ali@gmail.com · Trial · {Signed1.ToLocalTime():yyyy-MM-dd}", options[0]);
        Assert.Contains("aliabdelqader311@gmail.com", options[1], StringComparison.Ordinal);
        Assert.NotEqual(options[0], options[1]);

        // Typed text alone never makes a request: a subscriber must be picked.
        Act(() => cut.Find("textarea").Input("Invoice printing"));
        Assert.True(send().HasAttribute("disabled"));

        Act(() => cut.FindAll("[role=option]")[1].PointerDown());
        cut.WaitForAssertion(() => Assert.Contains("aliabdelqader311@gmail.com", cut.Find("[data-testid=chosen-tenant]").TextContent));
        Assert.DoesNotContain("ali@gmail.com", cut.Find("[data-testid=chosen-tenant]").TextContent.Replace("aliabdelqader311@gmail.com", "", StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains(L["con.chosenSubscriber"], cut.Markup);
        Assert.False(send().HasAttribute("disabled"));

        Act(() => send().Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST"));
        var post = Assert.Single(Requests, r => r.Method == "POST");
        Assert.Equal("/platform/support-access", post.Path);
        Assert.Contains("\"tenantId\":\"t2\"", post.Body, StringComparison.Ordinal);
        Assert.Contains("Invoice printing", post.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_matches_through_another_users_email_are_named()
    {
        Console();
        Respond("/platform/support-access/tenants", new[] { Ali1 with { MatchedEmail = "driver@ali.test" } });
        var cut = Render<SupportAccess>();
        cut.WaitForAssertion(() => Assert.Contains("Ali Transportation", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["con.requestSupport"], StringComparison.Ordinal)).Click());
        Act(() => cut.Find("#support-tenant").Input("driver@"));
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.matchedBy", "driver@ali.test"), cut.Find("[role=option] small").TextContent));
        Assert.Contains("q=driver%40", Requests.Last(r => r.Path.StartsWith("/platform/support-access/tenants", StringComparison.Ordinal)).Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Console_shows_the_approved_window_and_a_refusal_with_its_reason()
    {
        var starts = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        Console([
            Pending with { Id = "g1", Status = "Approved", StartsAt = starts, EndsAt = starts.AddDays(1), DecidedBy = "Ali Ali", OwnerEmail = "aliabdelqader311@gmail.com" },
            Pending with { Id = "g2", Status = "Rejected", DecidedBy = "Ali Ali", DecisionReason = "Not needed now" },
        ]);
        var cut = Render<SupportAccess>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));
        var approved = cut.FindAll("tbody tr")[0].TextContent;
        Assert.Contains(Fmt(starts), approved, StringComparison.Ordinal);
        Assert.Contains(Fmt(starts.AddDays(1)), approved, StringComparison.Ordinal);
        Assert.Contains("Ali Ali", approved, StringComparison.Ordinal);
        Assert.Contains("Ali Transportation · aliabdelqader311@gmail.com", approved, StringComparison.Ordinal);   // DT-219
        var refused = cut.FindAll("tbody tr")[1].TextContent;
        Assert.Contains(L["st.refused"], refused, StringComparison.Ordinal);
        Assert.Contains("Not needed now", refused, StringComparison.Ordinal);
    }

    private static string Fmt(DateTime utc) => Web.Client.Ui.Fmt.DateTime(utc.ToLocalTime(), "yyyy-MM-dd");

    [Fact]
    public void Approve_for_one_day_sends_24_hours()
    {
        Respond("/tenant/support-access", r => r.Method == HttpMethod.Post
            ? new[] { Pending with { Status = "Approved", StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddDays(1) } } : new[] { Pending });
        var cut = Render<SupportApprovals>();
        cut.WaitForAssertion(() => Assert.Contains("Direct Service Admin", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
        Assert.Contains(L["ten.day"], cut.Find(".dialog select").TextContent, StringComparison.Ordinal);
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["ten.endNow"], cut.Markup));
        var post = Assert.Single(Requests, r => r.Method == "POST");
        Assert.Equal("/tenant/support-access/g1/approve", post.Path);
        Assert.Contains("\"hours\":24", post.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_approval_shows_the_specific_message_and_reads_the_list_again()
    {
        var reads = 0;
        Respond("/tenant/support-access", r =>
        {
            if (r.Method == HttpMethod.Post) return Problem(404, "support.requestNotFound", "This access request was not found in your subscription");
            return ++reads == 1 ? new[] { Pending } : Array.Empty<SupportAccessResponse>();
        });
        var cut = Render<SupportApprovals>();
        cut.WaitForAssertion(() => Assert.Contains("Direct Service Admin", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("This access request was not found in your subscription", cut.Find(".dialog").TextContent, StringComparison.Ordinal));
        Assert.Equal(2, reads);
        Assert.DoesNotContain("غير موجود", cut.Find(".dialog").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuse_sends_the_reason_and_shows_a_failure_in_the_dialog()
    {
        var fail = true;
        Respond("/tenant/support-access", r => r.Method == HttpMethod.Post
            ? fail ? Problem(422, "support.notRequested", "This access request is no longer waiting for a decision") : (object)
                new[] { Pending with { Status = "Rejected", DecisionReason = "Not needed now", DecidedBy = "Ali Ali" } }
            : new[] { Pending });
        var cut = Render<SupportApprovals>();
        cut.WaitForAssertion(() => Assert.Contains("Direct Service Admin", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.reject"], StringComparison.Ordinal)).Click());
        Act(() => cut.Find(".dialog textarea").Input("Not needed now"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.reject"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("no longer waiting for a decision", cut.Find(".dialog").TextContent, StringComparison.Ordinal));

        fail = false;
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.reject"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("Not needed now", cut.Find("tbody").TextContent, StringComparison.Ordinal));
        Assert.Contains(L["st.refused"], cut.Find("tbody").TextContent, StringComparison.Ordinal);
        var post = Requests.Last(r => r.Method == "POST");
        Assert.Equal("/tenant/support-access/g1/reject", post.Path);
        Assert.Contains("Not needed now", post.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development", "shell.development")]
    [InlineData("Production", "shell.production")]
    [InlineData("Staging", "shell.staging")]
    public void Console_badge_shows_the_real_environment(string environment, string key)
    {
        var cut = Render<TopBar>(p => p.Add(x => x.Provider, true).Add(x => x.Environment, environment));
        Assert.Contains(L[key], cut.Find(".top").TextContent, StringComparison.Ordinal);
        if (environment != "Production") Assert.DoesNotContain(L["shell.production"], cut.Find(".top").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Console_badge_is_hidden_until_the_environment_is_known()
    {
        var cut = Render<TopBar>(p => p.Add(x => x.Provider, true));
        Assert.DoesNotContain(L["shell.production"], cut.Markup, StringComparison.Ordinal);
    }
}
