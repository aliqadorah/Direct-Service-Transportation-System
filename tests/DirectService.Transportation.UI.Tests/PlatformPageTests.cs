using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Transportation.Web.Client.Pages.Console;
using DirectService.Transportation.Web.Client.Pages.Tenant;
using DirectService.Transportation.Web.Client.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>Tenant screens (SCR-80 … SCR-83), the provider console (SCR-90 … SCR-94) and the shell banners, against a fake API.</summary>
public sealed class PlatformPageTests : PageTestBase
{
    private static readonly PlanResponse Pro = new("pl2", "PRO", "احترافية", "Professional", 180, "Monthly", 3, 25, 5000, 2048, true, 4, "A", 16m);
    private static readonly PlanResponse Basic = new("pl1", "BASIC", "أساسية", "Basic", 90, "Monthly", 1, 10, 1000, 1024, true, 7, "A");
    private static readonly PlanResponse Enterprise = new("pl3", "ENT", "مؤسسات", "Enterprise", 1900, "Annual", 10, 100, 30000, 10240, true, 1, "A");
    private static readonly UsageResponse Usage = new(2, 3, 18, 25, 412, 5000, 640, 2048);

    private static HttpResponseMessage Problem(int status, string code, string detail) => new((HttpStatusCode)status)
    {
        Content = new StringContent($"{{\"status\":{status},\"code\":\"{code}\",\"detail\":\"{detail}\"}}", Encoding.UTF8, "application/problem+json"),
    };

    private void Subscription(string status)
    {
        Respond("/tenant/subscription/invoices", new[] { new SubscriptionInvoiceResponse("si1", "SUB-000010", new(2026, 10, 1), new(2026, 10, 31), 155.172m, 24.828m, 180m, "Paid", null, null, DateTime.UtcNow) });
        Respond("/tenant/subscription", new SubscriptionResponse(status, Pro, "Monthly", null, DateTime.UtcNow.AddDays(20), null, null, Usage, [Basic, Pro, Enterprise]));
    }

    [Fact]
    public void Subscription_shows_usage_and_offers_downgrade_or_upgrade_by_plan()
    {
        Subscription("Active");
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000010", cut.Markup));
        Assert.Contains("412 / 5,000", Text(cut));
        Assert.Contains(L["ten.current"], cut.Markup);
        Assert.Contains(L["ten.downgrade"], cut.Markup);   // Basic: cheaper, same cycle
        Assert.Contains(L["ten.upgrade"], cut.Markup);     // Enterprise: other cycle → checkout
    }

    /// <summary>Owner decision 13 Oct 2026: tenants see only the final, tax-inclusive amount, with the note that it includes the tax.</summary>
    [Fact]
    public void Subscription_shows_only_tax_inclusive_amounts_with_the_note()
    {
        Subscription("Active");
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000010", cut.Markup));
        var text = Text(cut);
        Assert.Contains(L.F("ten.taxIncluded", "16%"), text);
        Assert.Contains("180.000", text);                    // the invoice shows the fee
        Assert.DoesNotContain("155.172", text);              // never the net…
        Assert.DoesNotContain("24.828", text);               // …or the tax
        Assert.DoesNotContain(L["ten.tax"], text);
    }

    private static SubscriptionInvoiceResponse[] Monthly(int count) => Enumerable.Range(1, count).Reverse().Select(n =>
        new SubscriptionInvoiceResponse($"si{n}", $"SUB-{n:000000}", new DateOnly(2024, 1, 1).AddMonths(n - 1), new DateOnly(2024, 1, 1).AddMonths(n).AddDays(-1),
            155.172m, 24.828m, 180m, "Paid", null, "pay_" + n, DateTime.UtcNow)).ToArray();

    /// <summary>Owner feedback 13 Oct 2026 (second round): the invoices take their natural height (no box of their own to scroll) and are paged.</summary>
    [Fact]
    public void Subscription_invoices_use_their_natural_height_and_are_paged()
    {
        Subscription("Active");
        Respond("/tenant/subscription/invoices", Monthly(30));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000030", cut.Markup));
        var box = cut.FindAll("section.box").Single(b => b.QuerySelector("caption")?.TextContent == L["ten.invoices"]);
        Assert.DoesNotContain("fill", box.ClassList);
        Assert.Equal(24, box.QuerySelectorAll("tbody tr").Length);
        Assert.Contains(L.F("ui.rowsOf", "1", "24", "30"), Text(cut));
        cut.Find($"button[aria-label='{L.F("ui.page", 2)}']").Click();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000006", cut.Markup));
        Assert.DoesNotContain("SUB-000030", cut.Markup);
        Assert.Equal(6, cut.FindAll("section.box").Single(b => b.QuerySelector("caption")?.TextContent == L["ten.invoices"]).QuerySelectorAll("tbody tr").Length);
    }

    [Fact]
    public void Subscription_with_few_invoices_has_no_pager()
    {
        Subscription("Active");
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000010", cut.Markup));
        Assert.Empty(cut.FindAll("nav.tbl-foot"));
    }

    [Fact]
    public void Console_plan_price_is_labelled_tax_inclusive()
    {
        Respond("/platform/plans", new[] { Pro });
        var cut = Render<Plans>();
        cut.WaitForAssertion(() => Assert.Contains("PRO", cut.Markup));
        Assert.Contains(L["con.priceInclusive"], cut.Markup);
        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.priceInclusiveHelp", "16%"), Text(cut)));
    }

    [Fact]
    public void Refused_downgrade_lists_the_exceeded_limits()
    {
        Subscription("Active");
        Respond("/tenant/subscription/downgrade", _ => Problem(409, "subscription.downgradeExceeds", "ActiveUsers 18/10"));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.downgrade"], cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.downgrade"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L.F("ten.downgradeTitle", "أساسية"), cut.Markup));
        cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.downgrade"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains("ActiveUsers 18/10", Text(cut)));
    }

    [Fact]
    public void Suspended_subscription_offers_to_pay_the_current_plan()
    {
        Subscription("Suspended");
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.payNow"], cut.Markup));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["ten.downgrade"], StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_limit_on_a_new_company_shows_the_upgrade_link()
    {
        Respond("/tenant/companies", r => r.Method == HttpMethod.Post
            ? Problem(409, "plan.limitReached", "Plan limit reached (3 companies).")
            : new[] { new CompanyResponse("c1", "شركة الاختبار", "Test company", "Active", false, DateTime.UtcNow, "A") });
        var cut = Render<Companies>();
        cut.WaitForAssertion(() => Assert.Contains("شركة الاختبار", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.newCompany"], StringComparison.Ordinal)).Click();
        cut.Find("form#company-form input").Change("شركة الشمال");
        cut.Find("form#company-form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("Plan limit reached (3 companies).", Text(cut)));
        Assert.NotNull(cut.Find("a[href='/tenant/subscription']"));
    }

    [Fact]
    public void Tenant_security_saves_with_the_row_version()
    {
        Respond("/tenant/companies", Array.Empty<CompanyResponse>());
        var tenant = new TenantResponse("Tenant", "Active", "PRO", "Professional", null, null, 10, true, true, 30, "RV1");
        Respond("/tenant/security", tenant);
        Respond("/tenant", tenant);
        var cut = Render<Companies>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.security"], cut.Markup));
        cut.FindAll("[role=tab]").First(b => b.TextContent.Contains(L["ten.security"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.passwordMin"], cut.Markup));
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "/tenant/security" && r.Body!.Contains("\"rowVersion\":\"RV1\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void Support_approval_sends_a_window_of_the_chosen_length()
    {
        var pending = new SupportAccessResponse("g1", "Tenant", "عمر المصري", "INV-02207 rejected", "Requested", null, null, null, DateTime.UtcNow, "A");
        Respond("/tenant/support-access", r => r.Method == HttpMethod.Post ? new[] { pending with { Status = "Approved", StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddHours(24) } } : new[] { pending });
        var cut = Render<SupportApprovals>();
        cut.WaitForAssertion(() => Assert.Contains("عمر المصري", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click();
        cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.endNow"], cut.Markup));
        var post = Assert.Single(Requests, r => r.Method == "POST");
        Assert.Equal("/tenant/support-access/g1/approve", post.Path);
        Assert.Contains("\"hours\":24", post.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Console_tenants_filter_by_status_and_open_a_tenant()
    {
        var t = new ConsoleTenantResponse("t1", "نقليات الجنوب", "Trial", new PlanSummary("pl0", "TRIAL", "Trial"), "Monthly", Usage, DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(4), null, null, null, null, "owner@example.test", "A");
        Respond("/platform/tenants/t1", new ConsoleTenantDetails(t, [], []));
        Respond("/platform/tenants", new PagedResult<ConsoleTenantResponse>([t], 1, 50, 1));
        var cut = Render<Tenants>();
        cut.WaitForAssertion(() => Assert.Contains("نقليات الجنوب", cut.Markup));
        cut.FindAll("[role=tab]").First(b => b.TextContent.Contains(L["st.trial"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("/platform/tenants?", StringComparison.Ordinal) && r.Path.Contains("status=Trial", StringComparison.Ordinal)));
        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.Contains(L["con.extendTrial"], cut.Markup));
        Assert.Contains("owner@example.test", Text(cut));
    }

    /// <summary>Owner feedback 13 Oct 2026: the console's search finds provider data, says so in its hint, and opens the tenant a hit belongs to.</summary>
    [Fact]
    public async Task Console_search_lists_provider_hits_with_their_screens_and_a_matching_hint()
    {
        Respond("/platform/search", new Contracts.Collaboration.SearchResponse("نقل", [
            new(ConsoleSearchKinds.Tenant, "t1", "نقليات الجنوب", "Trial"), new(ConsoleSearchKinds.Invoice, "t1", "SUB-000042", "نقليات الجنوب · 40.000 · Due"),
            new(ConsoleSearchKinds.Staff, "u7", "رنا", "rana@platform.test")]));
        var hits = await Services.GetRequiredService<AppSession>().ConsoleSearchAsync("نقل", CancellationToken.None);
        Assert.Equal(["/console/tenants?tenant=t1", "/console/tenants?tenant=t1", "/console/staff?staff=u7"], hits.Select(h => h.Href));
        Assert.Equal([L["con.hit.tenant"], L["con.hit.subscriptionInvoice"], L["con.hit.staff"]], hits.Select(h => h.Kind));
        Assert.Contains(Requests, r => r.Path.StartsWith("/platform/search?q=", StringComparison.Ordinal));

        var bar = Render<CommandBar>(p => p.Add(x => x.Open, true).Add(x => x.Placeholder, L["shell.searchConsole"]));
        Assert.Equal(L["shell.searchConsole"], bar.Find("input[type=search]").GetAttribute("placeholder"));
        Assert.NotEqual(L["shell.search"], L["shell.searchConsole"]);
    }

    [Fact]
    public void Console_tenant_list_opens_the_tenant_of_a_search_hit()
    {
        var t = new ConsoleTenantResponse("t1", "نقليات الجنوب", "Trial", new PlanSummary("pl0", "TRIAL", "Trial"), "Monthly", Usage, DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(4), null, null, null, null, "owner@example.test", "A");
        Respond("/platform/tenants/t1", new ConsoleTenantDetails(t, [], []));
        Respond("/platform/tenants", new PagedResult<ConsoleTenantResponse>([t], 1, 50, 1));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/console/tenants?tenant=t1");
        var cut = Render<Tenants>();
        cut.WaitForAssertion(() => Assert.Contains(L["con.extendTrial"], cut.Markup));
        Assert.Contains(Requests, r => r.Path == "/platform/tenants/t1");
    }

    /// <summary>Owner feedback 13 Oct 2026, item 8b: the staff screen offers per state only the allowed actions, and invites by name and e-mail.</summary>
    [Fact]
    public void Console_staff_lists_states_with_their_actions_and_invites()
    {
        var now = DateTime.UtcNow;
        Respond("/platform/staff", r => r.Method == HttpMethod.Post
            ? new StaffResponse("s9", "سارة", "sara@platform.test", StaffStates.Invited, null, now, now.AddHours(48), false, false)
            : new[]
            {
                new StaffResponse("s1", "أنا", "me@platform.test", StaffStates.Active, now, null, null, true, true),
                new StaffResponse("s2", "عمر", "omar@platform.test", StaffStates.Active, now.AddDays(-1), null, null, true, false),
                new StaffResponse("s3", "رنا", "rana@platform.test", StaffStates.Invited, null, now, now.AddHours(48), false, false),
                new StaffResponse("s4", "خالد", "khaled@platform.test", StaffStates.Disabled, null, null, null, true, false),
            });
        var cut = Render<Staff>();
        cut.WaitForAssertion(() => Assert.Contains("rana@platform.test", cut.Markup));
        string Row(string email) => cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(email, StringComparison.Ordinal)).TextContent;
        Assert.DoesNotContain(L["staff.deactivate"], Row("me@platform.test"));      // never oneself
        Assert.Contains(L["staff.you"], Row("me@platform.test"));
        Assert.Contains(L["staff.deactivate"], Row("omar@platform.test"));
        Assert.Contains(L["staff.resend"], Row("rana@platform.test"));
        Assert.Contains(L["staff.cancelInvite"], Row("rana@platform.test"));
        Assert.Contains(L["staff.reactivate"], Row("khaled@platform.test"));

        cut.FindAll("button").First(b => b.TextContent.Contains(L["staff.invite"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L["staff.inviteHelp"], cut.Markup));
        cut.FindAll(".dialog input")[0].Change("Sara");
        cut.FindAll(".dialog input")[1].Change("sara@platform.test");
        cut.Find("form#staff-invite").Submit();
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "/platform/staff/invitations" && r.Body!.Contains("sara@platform.test", StringComparison.Ordinal)));
        // The help link of the screen opens the console's help page.
        Assert.Contains("href=\"/console/help/console-staff\"", cut.Markup);
    }

    [Fact]
    public void Console_help_page_shows_the_staff_help_in_both_languages()
    {
        var cut = Render<ConsoleHelp>(p => p.Add(x => x.Topic, "console-staff"));
        Assert.Contains("/provider/setup", Text(cut));
        Assert.Contains("href=\"/console/staff\"", cut.Markup);
        Assert.Single(Web.Client.Help.HelpTopics.ConsoleTopics, t => t.Key == "console-staff" && t.Ar.Steps.Count == t.En.Steps.Count && t.Ar.Problems.Count == t.En.Problems.Count);
    }

    [Fact]
    public void Console_plans_open_the_editor_and_protect_used_plans()
    {
        Respond("/platform/plans", new[] { Pro });
        var cut = Render<Plans>();
        cut.WaitForAssertion(() => Assert.Contains("PRO", cut.Markup));
        cut.Find("tbody tr").Click();
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.planInUse", "4"), Text(cut)));
        Assert.DoesNotContain(L["ml.delete"], cut.Find(".dialog").TextContent);
    }

    [Fact]
    public void Console_health_shows_tenants_by_status()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Active"] = 21, ["Trial"] = 5 }, 412, 63, 3912, 11, 6, 320, TimeSpan.FromHours(30), DateTime.UtcNow));
        var cut = Render<Health>();
        cut.WaitForAssertion(() => Assert.Contains("3,912", cut.Markup));
        Assert.Contains(L["st.trial"], cut.Markup);
        Assert.Contains(L["con.up"], cut.Markup);
    }

    [Fact]
    public void Banners_show_current_announcements_and_hide_them_on_request()
    {
        Respond("/announcements", new[] { new AnnouncementResponse("a1", "صيانة مجدولة", "الجمعة 02:00", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(5), "A") });
        var cut = Render<TenantBanners>();
        cut.WaitForAssertion(() => Assert.Contains("صيانة مجدولة", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["banner.hide"], StringComparison.Ordinal)).Click();
        Assert.DoesNotContain("صيانة مجدولة", cut.Markup);
    }

    [Fact]
    public async Task Staff_enter_a_company_of_an_approved_support_window()
    {
        Respond("/me", new MeResponse("s1", "Support", "staff@example.test", "ar", "yyyy-MM-dd", null, true, null, null, []));
        Respond("/platform/support-access/companies", new[] { new CompanyAccess("c9", "شركة المشترك", null, "Active", "Support", true, ["trn.trips.view"]) });
        var session = Services.GetRequiredService<AppSession>();
        Assert.True(await session.EnterCompanyAsync("c9"));
        Assert.Equal("c9", Shell.Company?.Id);
        Assert.False(Shell.Can("trn.trips.edit"));
        Assert.False(await session.EnterCompanyAsync("other"));
    }
}
