using System.Net;
using System.Text;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Web.Client.Help;
using DirectService.Shared.UI.Help;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Transportation.Web.Client.Localization;
using DirectService.Shared.UI.Localization;
using DirectService.Transportation.Web.Client.Pages.Console;
using DirectService.Transportation.Web.Client.Pages.Tenant;
using DirectService.Transportation.Web.Client.Services;
using DirectService.Shared.UI.Services;
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
        Act(() => cut.Find($"button[aria-label='{L.F("ui.page", 2)}']").Click());
        cut.WaitForAssertion(() => Assert.Contains("SUB-000006", cut.Markup));
        Assert.DoesNotContain("SUB-000030", cut.Markup);
        Assert.Equal(6, cut.FindAll("section.box").Single(b => b.QuerySelector("caption")?.TextContent == L["ten.invoices"]).QuerySelectorAll("tbody tr").Length);
    }

    private static readonly PlanResponse Trial = new("pl0", "TRIAL", "تجريبية", "Trial", 0, "Monthly", 2, 5, 2000, 1024, false, 3, "A", 16m);

    private int TaxNotes(IRenderedComponent<Subscription> cut) =>
        cut.FindAll("p.help").Count(p => p.TextContent.Contains(L.F("ten.taxIncluded", "16%"), StringComparison.Ordinal));

    /// <summary>Owner feedback 13 Oct 2026 (second round): the tax note once per section showing prices, never repeated section after section.</summary>
    [Theory]
    [InlineData("trial, no plans on offer, one invoice", 1)]
    [InlineData("paid plan listed in the plans table, one invoice", 2)]
    [InlineData("paid plan no longer offered, no invoices", 1)]
    public void Subscription_tax_note_appears_once_per_section_with_prices(string situation, int notes)
    {
        var invoice = new SubscriptionInvoiceResponse("si2", "SUB-000002", new(2026, 10, 13), new(2026, 11, 12), 200m, 32m, 232m, "Paid", null, "pay_2", DateTime.UtcNow);
        var (current, plans, invoices) = situation switch
        {
            "trial, no plans on offer, one invoice" => (Trial, Array.Empty<PlanResponse>(), new[] { invoice }),
            "paid plan listed in the plans table, one invoice" => (Pro, new[] { Basic, Pro }, new[] { invoice }),
            _ => (Pro, new[] { Basic }, Array.Empty<SubscriptionInvoiceResponse>()),
        };
        Respond("/tenant/subscription/invoices", invoices);
        Respond("/tenant/subscription", new SubscriptionResponse(current == Trial ? "Trial" : "Active", current, "Monthly", DateTime.UtcNow.AddDays(10), null, null, null, Usage, plans));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.invoices"], cut.Markup));
        cut.WaitForAssertion(() => Assert.Equal(notes, TaxNotes(cut)));
        if (plans.Length == 0) Assert.DoesNotContain(L["ten.plansHelp"], Text(cut));
    }

    /// <summary>
    /// Owner feedback 13 Oct 2026 (third round): on the trial, invoice SUB-000002 of 232.000 came from choosing a plan whose payment page was
    /// left unpaid. It is shown as awaiting payment with what it means — not as due beside a 0.000 trial plan.
    /// </summary>
    [Fact]
    public void Unpaid_checkout_is_awaiting_payment_and_explained_not_due()
    {
        Respond("/tenant/subscription/invoices", new[]
        {
            new SubscriptionInvoiceResponse("si2", "SUB-000002", new(2026, 10, 13), new(2026, 11, 12), 200m, 32m, 232m, "Due", null, "chk_0186", DateTime.UtcNow),
        });
        Respond("/tenant/subscription", new SubscriptionResponse("Trial", Trial, "Monthly", DateTime.UtcNow.AddDays(13), null, null, null, Usage, []));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));
        var text = Text(cut);
        Assert.Contains(L["st.awaitingPayment"], text);
        Assert.DoesNotContain(L["st.due"], text);
        Assert.Contains(L.F("ten.awaitingTitle", "SUB-000002"), text);
        Assert.Contains(L.F("ten.awaitingText", "232.000", "2026-10-13 – 2026-11-12", "تجريبية"), text);
    }

    [Fact]
    public void Renewal_invoice_without_a_payment_page_stays_due_without_the_note()
    {
        Respond("/tenant/subscription/invoices", new[]
        {
            new SubscriptionInvoiceResponse("si3", "SUB-000003", new(2026, 11, 1), new(2026, 11, 30), 155.172m, 24.828m, 180m, "Due", null, null, DateTime.UtcNow),
        });
        Respond("/tenant/subscription", new SubscriptionResponse("Active", Pro, "Monthly", null, DateTime.UtcNow.AddDays(1), null, null, Usage, [Basic, Pro]));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000003", cut.Markup));
        Assert.Contains(L["st.due"], Text(cut));
        Assert.DoesNotContain(L.F("ten.awaitingTitle", "SUB-000003"), Text(cut));
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
        Act(() => cut.Find("tbody tr").Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.priceInclusiveHelp", "16%"), Text(cut)));
    }

    [Fact]
    public void Refused_downgrade_lists_the_exceeded_limits()
    {
        Subscription("Active");
        Respond("/tenant/subscription/downgrade", _ => Problem(409, "subscription.downgradeExceeds", "ActiveUsers 18/10"));
        var cut = Render<Subscription>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.downgrade"], cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.downgrade"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("ten.downgradeTitle", "أساسية"), cut.Markup));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.downgrade"], StringComparison.Ordinal)).Click());
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
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.newCompany"], StringComparison.Ordinal)).Click());
        Act(() => cut.Find("form#company-form input").Change("شركة الشمال"));
        Act(() => cut.Find("form#company-form").Submit());
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
        Act(() => cut.FindAll("[role=tab]").First(b => b.TextContent.Contains(L["ten.security"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["ten.passwordMin"], cut.Markup));
        Act(() => cut.Find("form").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "/tenant/security" && r.Body!.Contains("\"rowVersion\":\"RV1\"", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Owner finding 27 Oct 2026 (DT-270): tenant-facing help pages and the sign-in texts never say that two-step verification is always
    /// required, and never mention platform staff (the provider console's own help topics may).
    /// </summary>
    private static readonly string[] SignInKeys = ["ten.securityHelp", "ten.require2faHelp", "acct.mandatory", "acct.setupNote", "acct.inviteDone", "acct.checkEmailText"];

    private static readonly string[] OutdatedWording =
        ["platform staff", "موظفو المنصة", "موظفي المنصة", "always required", "إلزامي دائماً", "Two-step verification cannot be turned off.", "لا يمكن تعطيل التحقق بخطوتين."];

    [Fact]
    public void Tenant_help_and_sign_in_texts_have_no_outdated_two_step_or_staff_wording()
    {
        var texts = HelpTopics.All.SelectMany(t => new[] { t.Ar, t.En }).SelectMany(h => new[] { h.Title, h.Summary, h.Who }.Concat(h.Steps).Concat(h.Problems)).ToList();
        texts.AddRange(SignInKeys.SelectMany(k => new[] { UiText.All[k].Ar, UiText.All[k].En }));
        foreach (var bad in OutdatedWording)
            Assert.DoesNotContain(texts, t => t.Contains(bad, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Revision 6 item 2: "Require two-step verification for all users" — off by default, saved with the security settings.</summary>
    [Fact]
    public void Tenant_security_turns_on_require_2fa_for_all_users()
    {
        Respond("/tenant/companies", Array.Empty<CompanyResponse>());
        var tenant = new TenantResponse("Tenant", "Active", "PRO", "Professional", null, null, 10, true, true, 30, "RV1");
        Respond("/tenant/security", tenant);
        Respond("/tenant", tenant);
        var cut = Render<Companies>();
        cut.WaitForAssertion(() => Assert.Contains(L["ten.security"], cut.Markup));
        Act(() => cut.FindAll("[role=tab]").First(b => b.TextContent.Contains(L["ten.security"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["ten.require2fa"], cut.Markup));
        Assert.Contains(L["ten.require2faHelp"], cut.Markup);
        // Owner finding 27 Oct 2026 (DT-270): two-step verification is optional unless required; no word about platform staff here.
        Assert.Contains(L["ten.securityHelp"], cut.Markup);
        Assert.Equal(("تنطبق هذه الإعدادات على كل مستخدمي المنشأة.", "These settings apply to every user of the organisation."), UiText.All["ten.securityHelp"]);
        Assert.Equal("When on, every user without two-step verification must set it up at their next sign-in.", UiText.All["ten.require2faHelp"].En);
        Assert.DoesNotContain("المنصة", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("إلزامي دائماً", cut.Markup, StringComparison.Ordinal);
        AngleSharp.Dom.IElement toggle() => cut.FindAll("[role=switch]").Single(s => s.ParentElement!.TextContent.Contains(L["ten.require2fa"], StringComparison.Ordinal)
            || (s.GetAttribute("aria-labelledby") is { } id && cut.Find("#" + id).TextContent.Contains(L["ten.require2fa"], StringComparison.Ordinal)));
        Assert.Equal("false", toggle().GetAttribute("aria-checked"));
        Act(() => toggle().Click());
        Act(() => cut.Find("form").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "/tenant/security" && r.Body!.Contains("\"requireTwoFactor\":true", StringComparison.Ordinal)));
    }

    [Fact]
    public void Support_approval_sends_a_window_of_the_chosen_length()
    {
        var pending = new SupportAccessResponse("g1", "Tenant", "عمر المصري", "INV-02207 rejected", "Requested", null, null, null, DateTime.UtcNow, "A");
        Respond("/tenant/support-access", r => r.Method == HttpMethod.Post ? new[] { pending with { Status = "Approved", StartsAt = DateTime.UtcNow, EndsAt = DateTime.UtcNow.AddHours(24) } } : new[] { pending });
        var cut = Render<SupportApprovals>();
        cut.WaitForAssertion(() => Assert.Contains("عمر المصري", cut.Markup));
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ten.approve"], StringComparison.Ordinal)).Click());
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
        Act(() => cut.FindAll("[role=tab]").First(b => b.TextContent.Contains(L["st.trial"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.StartsWith("/platform/tenants?", StringComparison.Ordinal) && r.Path.Contains("status=Trial", StringComparison.Ordinal)));
        Act(() => cut.Find("tbody tr").Click());
        cut.WaitForAssertion(() => Assert.Contains(L["con.extendTrial"], cut.Markup));
        Assert.Contains("owner@example.test", Text(cut));
    }

    /// <summary>Owner feedback 13 Oct 2026: the console's search finds provider data, says so in its hint, and opens the tenant a hit belongs to.</summary>
    [Fact]
    public async Task Console_search_lists_provider_hits_with_their_screens_and_a_matching_hint()
    {
        Respond("/platform/search", new SearchResponse("نقل", [
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

    /// <summary>
    /// Owner decision 13 Oct 2026 (DT-183): in the tenant panel only an invoice awaiting payment offers "Cancel invoice" (a full credit with a
    /// reason); a renewal due offers the usual credit, paid and credited invoices nothing.
    /// </summary>
    [Fact]
    public void Console_cancels_only_an_invoice_awaiting_payment_with_a_reason()
    {
        var t = new ConsoleTenantResponse("t1", "نقليات الجنوب", "Trial", new PlanSummary("pl0", "TRIAL", "Trial"), "Monthly", Usage, DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(4), null, null, null, null, "owner@example.test", "A");
        SubscriptionInvoiceResponse Invoice(string id, string number, string status, string? reference) =>
            new(id, number, new(2026, 10, 13), new(2026, 11, 12), 200m, 32m, 232m, status, null, reference, DateTime.UtcNow);
        var awaiting = Invoice("si2", "SUB-000002", "Due", "chk_0186");
        Respond("/platform/tenants/t1/invoices/si2/cancel", Invoice("si2", "SUB-000002", "Credited", "chk_0186"));
        Respond("/platform/tenants/t1", new ConsoleTenantDetails(t,
            [awaiting, Invoice("si3", "SUB-000003", "Due", null), Invoice("si4", "SUB-000004", "Paid", "pay_4"), Invoice("si5", "SUB-000005", "Credited", "chk_5")], []));
        Respond("/platform/tenants", new PagedResult<ConsoleTenantResponse>([t], 1, 50, 1));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/console/tenants?tenant=t1");
        var cut = Render<Tenants>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));

        string? Action(string number) => cut.FindAll(".panel-side tbody tr").Single(r => r.TextContent.Contains(number, StringComparison.Ordinal))
            .QuerySelector("button")?.GetAttribute("aria-label");
        Assert.Equal(L.F("con.cancelInvoiceTitle", "SUB-000002"), Action("SUB-000002"));
        Assert.Contains(L["st.awaitingPayment"], cut.Find(".panel-side").TextContent);
        Assert.Equal(L.F("con.creditTitle", "SUB-000003"), Action("SUB-000003"));   // a renewal due: the usual credit
        Assert.Null(Action("SUB-000004"));
        Assert.Null(Action("SUB-000005"));

        Act(() => cut.Find($"button[aria-label='{L.F("con.cancelInvoiceTitle", "SUB-000002")}']").Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.cancelInvoiceText", "SUB-000002", "232.000"), Text(cut)));
        var confirm = cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ui.confirm"], StringComparison.Ordinal));
        Assert.True(confirm.HasAttribute("disabled"));                              // no reason yet
        Act(() => cut.Find(".dialog textarea").Input("Plan withdrawn"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["ui.confirm"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "/platform/tenants/t1/invoices/si2/cancel"));
        Assert.Contains("\"reason\":\"Plan withdrawn\"", Requests.Single(r => r.Path.EndsWith("/cancel", StringComparison.Ordinal)).Body, StringComparison.Ordinal);
        Assert.DoesNotContain(Requests, r => r.Path == "/platform/tenants/t1/credit");
    }

    private static readonly RefundDueResponse LatePayment = new("si2", "SUB-000002", "t1", "نقليات الجنوب", 232m, new DateTime(2026, 10, 14, 9, 0, 0, DateTimeKind.Utc), RefundKinds.Credited);

    /// <summary>Owner request 14 Oct 2026 (DT-184): a late payment of a credited checkout is marked on its row, listed in the panel with a replaced one.</summary>
    [Fact]
    public void Console_panel_marks_payments_to_refund()
    {
        var t = new ConsoleTenantResponse("t1", "نقليات الجنوب", "Trial", new PlanSummary("pl0", "TRIAL", "Trial"), "Monthly", Usage, DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(4), null, null, null, null, "owner@example.test", "A");
        var credited = new SubscriptionInvoiceResponse("si2", "SUB-000002", new(2026, 10, 13), new(2026, 11, 12), 200m, 32m, 232m, "Credited", DateTime.UtcNow, "chk_0186", DateTime.UtcNow);
        var other = new SubscriptionInvoiceResponse("si5", "SUB-000005", new(2026, 10, 13), new(2026, 11, 12), 200m, 32m, 232m, "Credited", null, "chk_5", DateTime.UtcNow);
        Respond("/platform/tenants/t1", new ConsoleTenantDetails(t, [credited, other], [],
            [LatePayment, LatePayment with { InvoiceId = "si1", Number = "SUB-000001", Kind = RefundKinds.Replaced }]));
        Respond("/platform/tenants", new PagedResult<ConsoleTenantResponse>([t], 1, 50, 1));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/console/tenants?tenant=t1");
        var cut = Render<Tenants>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));

        string Row(string number) => cut.FindAll(".panel-side tbody tr").Single(r => r.TextContent.Contains(number, StringComparison.Ordinal)).TextContent;
        Assert.Contains(L["con.refundDue"], Row("SUB-000002"));
        Assert.DoesNotContain(L["con.refundDue"], Row("SUB-000005"));   // credited without a payment: nothing to refund
        var banner = Text(cut);
        Assert.Contains(L["con.refunds"], banner);
        Assert.Contains(L.F("con.refundLine", "SUB-000002", "232.000", DirectService.Shared.UI.Ui.Fmt.Date(LatePayment.PaidAt!.Value.ToLocalTime(), Shell.DateFormat), L["con.refund.credited"]), banner);
        Assert.Equal(2, cut.FindAll(".panel-side .banner button").Count(b => b.TextContent.Contains(L["con.markRefunded"], StringComparison.Ordinal)));
        Assert.Empty(cut.FindAll(".panel-side details.refunded"));        // nothing refunded yet
        Assert.Contains(L["con.refund.replaced"], banner);                // the replaced invoice is not a row, but it is listed
    }

    [Fact]
    public void Console_health_lists_payments_to_refund_with_a_link_to_the_tenant()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Trial"] = 3 }, 5, 3, 9, 0, 0, 300, TimeSpan.FromHours(2), DateTime.UtcNow));
        Respond("/platform/refunds", new RefundsResponse([LatePayment], []));
        var cut = Render<Health>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));
        Assert.Contains(L["con.refund.credited"], Text(cut));
        Assert.Equal("/console/tenants?tenant=t1", cut.Find("table a").GetAttribute("href"));
        Assert.Contains(L["con.refundsHelp"], Text(cut));
    }

    [Fact]
    public void Console_health_keeps_its_figures_when_the_refund_list_fails()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Trial"] = 3 }, 5, 3, 9, 0, 0, 300, TimeSpan.FromHours(2), DateTime.UtcNow));
        var cut = Render<Health>();   // /platform/refunds answers 404
        cut.WaitForAssertion(() => Assert.Contains(L["con.services"], cut.Markup));
        Assert.Contains(L["ui.loadFailed"], Text(cut));
    }

    [Fact]
    public void Console_health_without_payments_to_refund_says_so()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Trial"] = 3 }, 5, 3, 9, 0, 0, 300, TimeSpan.FromHours(2), DateTime.UtcNow));
        Respond("/platform/refunds", new RefundsResponse([], []));
        var cut = Render<Health>();
        cut.WaitForAssertion(() => Assert.Contains(L["con.noRefunds"], cut.Markup));
    }

    private static readonly RefundedResponse Marked = new(LatePayment, new DateOnly(2026, 10, 14), "TRX-778", "E2E Support", new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc));

    /// <summary>Owner request 14 Oct 2026 (DT-185): "Mark as refunded" asks the date and reference; the payment then moves to the collapsed "Refunded".</summary>
    [Fact]
    public void Console_health_marks_a_payment_refunded_and_shows_it_under_refunded()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Trial"] = 3 }, 5, 3, 9, 0, 0, 300, TimeSpan.FromHours(2), DateTime.UtcNow));
        var marked = false;
        Respond("/platform/refunds/si2/refunded", _ => { marked = true; return Marked; });
        Respond("/platform/refunds", _ => marked ? new RefundsResponse([], [Marked]) : new RefundsResponse([LatePayment], []));
        var cut = Render<Health>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));

        Act(() => cut.Find($"button[aria-label='{L.F("con.markRefundedOf", "SUB-000002")}']").Click());
        cut.WaitForAssertion(() => Assert.Contains(L["con.refundReference"], cut.Markup));
        var confirm = cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["con.markRefunded"], StringComparison.Ordinal));
        Assert.True(confirm.HasAttribute("disabled"));                          // the date is filled in (today); the reference is not
        Act(() => cut.Find(".dialog input[maxlength='100']").Input("TRX-778"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["con.markRefunded"], StringComparison.Ordinal)).Click());

        cut.WaitForAssertion(() => Assert.Contains(L.F("con.refundedN", 1), cut.Markup));
        var body = Requests.Single(r => r.Path == "/platform/refunds/si2/refunded").Body!;
        Assert.Contains("\"reference\":\"TRX-778\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"refundedOn\":\"{DateTime.Now:yyyy-MM-dd}\"", body, StringComparison.Ordinal);
        Assert.Contains(L["con.noRefunds"], cut.Markup);                     // left the list
        var refunded = cut.Find("details.refunded");
        Assert.False(refunded.HasAttribute("open"));                           // collapsed
        Assert.Contains("TRX-778", refunded.TextContent);
        Assert.Contains("E2E Support", refunded.TextContent);
    }

    [Fact]
    public void Mark_refunded_shows_a_refused_date_on_its_field()
    {
        Respond("/platform/health", new HealthResponse(true, new Dictionary<string, int> { ["Trial"] = 3 }, 5, 3, 9, 0, 0, 300, TimeSpan.FromHours(2), DateTime.UtcNow));
        Respond("/platform/refunds", new RefundsResponse([LatePayment], []));
        Respond("/platform/refunds/si2/refunded", _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"status\":400,\"code\":\"validation.failed\",\"detail\":\"Please correct the highlighted fields.\",\"errors\":[{\"field\":\"refundedOn\",\"code\":\"refund.date\",\"message\":\"Enter the refund date, between the payment date (2026-10-14) and today.\"}]}",
                Encoding.UTF8, "application/problem+json"),
        });
        var cut = Render<Health>();
        cut.WaitForAssertion(() => Assert.Contains("SUB-000002", cut.Markup));
        Act(() => cut.Find($"button[aria-label='{L.F("con.markRefundedOf", "SUB-000002")}']").Click());
        cut.WaitForAssertion(() => Assert.Contains(L["con.refundReference"], cut.Markup));
        Act(() => cut.Find(".dialog input[maxlength='100']").Input("TRX-1"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["con.markRefunded"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("between the payment date", Text(cut)));
        Assert.Contains(cut.FindAll(".dialog [aria-invalid=true]"), e => e.TagName == "INPUT");
    }

    [Fact]
    public void Console_panel_shows_refunded_payments_collapsed()
    {
        var t = new ConsoleTenantResponse("t1", "نقليات الجنوب", "Trial", new PlanSummary("pl0", "TRIAL", "Trial"), "Monthly", Usage, DateTime.UtcNow.AddDays(-10),
            DateTime.UtcNow.AddDays(4), null, null, null, null, "owner@example.test", "A");
        Respond("/platform/tenants/t1", new ConsoleTenantDetails(t, [], [], [], [Marked]));
        Respond("/platform/tenants", new PagedResult<ConsoleTenantResponse>([t], 1, 50, 1));
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("/console/tenants?tenant=t1");
        var cut = Render<Tenants>();
        cut.WaitForAssertion(() => Assert.Contains(L.F("con.refundedN", 1), cut.Markup));
        Assert.Empty(cut.FindAll(".panel-side .banner.warn"));                // nothing left to refund
        Assert.Contains("TRX-778", cut.Find(".panel-side details.refunded").TextContent);
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

        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["staff.invite"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["staff.inviteHelp"], cut.Markup));
        Act(() => cut.FindAll(".dialog input")[0].Change("Sara"));
        Act(() => cut.FindAll(".dialog input")[1].Change("sara@platform.test"));
        Act(() => cut.Find("form#staff-invite").Submit());
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
        Assert.Single(DirectService.Shared.UI.Help.HelpTopics.ConsoleTopics, t => t.Key == "console-staff" && t.Ar.Steps.Count == t.En.Steps.Count && t.Ar.Problems.Count == t.En.Problems.Count);
    }

    [Fact]
    public void Console_plans_open_the_editor_and_protect_used_plans()
    {
        Respond("/platform/plans", new[] { Pro });
        var cut = Render<Plans>();
        cut.WaitForAssertion(() => Assert.Contains("PRO", cut.Markup));
        Act(() => cut.Find("tbody tr").Click());
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
        Act(() => cut.FindAll("button").First(b => b.TextContent.Contains(L["banner.hide"], StringComparison.Ordinal)).Click());
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
