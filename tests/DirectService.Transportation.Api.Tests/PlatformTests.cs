using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using DirectService.Transportation.Infrastructure.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// B12 platform: provider console (plans, tenants, account actions, health — P-03, P-11, P-12, P-15), subscription checkout, signed webhook,
/// pro-rated upgrade and downgrade rules (P-04 … P-07, BR-012), plan limits, tenant lifecycle with read-only suspension, reactivation and
/// deletion with anonymisation (5.2, BR-010, BR-011, G-18, SEC-19), support access with Owner approval and read-only, audited windows (BR-014).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PlatformTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    /// <summary>A provider staff member: a signed-up user turned into platform staff without a tenant.</summary>
    private Task<SignedInUser> StaffAsync() => StaffAsync(f);

    /// <summary>A provider staff member (also used by the staff tests).</summary>
    internal static async Task<SignedInUser> StaffAsync(ApiFactory f)
    {
        var u = await f.SignUpOwnerAsync();
        var email = u.Email;
        // Staff accounts are set up by operations (no tenant); a direct update stands in for that script here.
        await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(x => x.Email == email)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsPlatformStaff, true).SetProperty(x => x.TenantId, (long?)null)));
        // A new session reads the account as staff (session principals are cached per session).
        return u with { Client = await f.SignInAsync(u.Email, u.Password, u.AuthenticatorKey) };
    }

    [Fact]
    public async Task Staff_accounts_are_created_by_operations_with_a_set_password_link_DT131()
    {
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        using (var scope = f.Services.CreateScope())
            Assert.Contains("created", await scope.ServiceProvider.GetRequiredService<StaffAccountService>().CreateAsync(email, "Support Agent", CancellationToken.None),
                StringComparison.Ordinal);
        var user = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().SingleAsync(u => u.Email == email));
        Assert.True(user.IsPlatformStaff);
        Assert.Null(user.TenantId);

        var link = f.Emails.LinkParameters(email, "PasswordReset");
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(link["u"], link["t"], ApiHelpers.Password))).EnsureOkAsync();
        var staff = await f.SignInFirstTimeAsync(client, email, ApiHelpers.Password);   // two-step verification is set up at the first sign-in
        Assert.True(staff.Me.IsPlatformStaff);
        await (await staff.Client.GetAsync("/api/v1/platform/plans")).EnsureOkAsync();

        // A sign-up user's e-mail can never be turned into a staff account by the command.
        var owner = await f.SignUpOwnerAsync();
        using var again = f.Services.CreateScope();
        await Assert.ThrowsAsync<DirectService.Transportation.Application.Abstractions.AppException>(() => again.ServiceProvider.GetRequiredService<StaffAccountService>().CreateAsync(owner.Email, "X", CancellationToken.None));
    }

    private static PlanRequest Plan(string code, decimal price, int users = 10, bool active = true) => new(code, "باقة " + code, "Plan " + code, price, "Monthly", 3, users, 5000, 1024, active, null);

    private static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private Task<Tenant> TenantOfAsync(SignedInUser o) =>
        f.DbAsync(db => db.Set<Tenant>().IgnoreQueryFilters().Include(t => t.Plan).SingleAsync(t => db.Set<User>().IgnoreQueryFilters().Any(u => u.Email == o.Email && u.TenantId == t.Id)));

    private Task<int> SetTenantAsync(SignedInUser o, Action<Tenant> change) => f.DbAsync(async db =>
    {
        var t = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(t => db.Set<User>().IgnoreQueryFilters().Any(u => u.Email == o.Email && u.TenantId == t.Id));
        change(t);
        await db.SaveChangesAsync();
        return 0;
    });

    private async Task<int> RunLifecycleAsync()
    {
        await using var scope = f.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TenantLifecycleService>().RunAsync(CancellationToken.None);
    }

    /// <summary>The gateway's notification for an invoice: the metadata given at checkout echoed back, and the amount paid (the invoice total unless given).</summary>
    private async Task<HttpResponseMessage> WebhookAsync(SubscriptionInvoiceResponse invoice, string status, string? plan, string? signature = null, string kind = "period",
        decimal? amount = null)
    {
        var metadata = new Dictionary<string, string> { ["kind"] = kind };
        if (plan is not null)
            metadata["plan"] = (await f.DbAsync(db => db.Set<Domain.Core.Plan>().Where(p => p.Code == plan).Select(p => p.PublicId).SingleAsync())).ToString("N");
        var body = JsonSerializer.Serialize(new { reference = invoice.GatewayReference, status, metadata, amount = amount ?? invoice.Total, currency = "JOD" });
        using var anonymous = f.CreateClient();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        content.Headers.Add("X-Signature", signature ?? FakePaymentGateway.Sign(ApiFactory.WebhookSecret, body));
        return await anonymous.PostAsync("/api/v1/billing/webhook", content);
    }

    [Fact]
    public async Task Console_plans_tenants_and_health_are_for_platform_staff_only_P03_P11_P15()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/v1/platform/plans")).StatusCode);

        var code = Code("PRO");
        var plan = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(code, 50m)));
        Assert.Equal(50m, plan.Price);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(code, 60m))).StatusCode);   // code unique among active plans
        var tenants = await ReadAsync<PagedResult<ConsoleTenantResponse>>(await staff.Client.GetAsync("/api/v1/platform/tenants?status=Trial&pageSize=200"));
        Assert.Contains(tenants.Items, t => t.OwnerEmail == owner.Email && t.Usage.Companies == 1 && t.Usage.ActiveUsers == 1);
        var health = await ReadAsync<HealthResponse>(await staff.Client.GetAsync("/api/v1/platform/health"));
        Assert.True(health.Database);
        Assert.True(health.TenantsByStatus["Trial"] >= 1);

        // A plan in use cannot be deleted (BR-040); an unused one can.
        var trial = (await ReadAsync<List<PlanResponse>>(await staff.Client.GetAsync("/api/v1/platform/plans"))).Single(p => p.Code == "TRIAL");
        Assert.Equal("record.inUse", await (await staff.Client.PostAsJsonAsync($"/api/v1/platform/plans/{trial.Id}/delete", new { reason = "x" })).ProblemCodeAsync());
        await (await staff.Client.PostAsJsonAsync($"/api/v1/platform/plans/{plan.Id}/delete", new { reason = "Test plan" })).EnsureOkAsync();

        // Announcements: published by staff, shown to signed-in users while current.
        await (await staff.Client.PostAsJsonAsync("/api/v1/platform/announcements",
            new AnnouncementRequest("Maintenance", "Planned maintenance on Friday", DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddDays(2), null))).EnsureOkAsync();
        Assert.Contains(await ReadAsync<List<AnnouncementResponse>>(await owner.Client.GetAsync("/api/v1/announcements")), a => a.Title == "Maintenance");
    }

    /// <summary>
    /// Owner feedback 13 Oct 2026: the console search finds provider data only — tenants by name or a user's e-mail, subscriptions by plan,
    /// subscription invoices by number, platform staff by name or e-mail — never company records; tenants' users cannot use it.
    /// </summary>
    [Fact]
    public async Task Console_search_finds_tenants_subscriptions_invoices_and_staff_only()
    {
        var staff = await StaffAsync();
        var organisation = "Searchable " + Guid.NewGuid().ToString("N")[..6];
        var owner = await f.SignUpOwnerAsync(organisation);
        var plan = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan("S" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), 40m)));
        var checkout = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(plan.Id)));
        // A company record whose text matches: never part of the console search.
        await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"), new CustomerRequest(organisation + " customer", null, null, "0781234567", null, null, null, null, 30,
            null, null, null, null, null))).EnsureOkAsync();

        async Task<SearchResponse> Search(string q) => await ReadAsync<SearchResponse>(await staff.Client.GetAsync("/api/v1/platform/search?q=" + Uri.EscapeDataString(q)));
        var byName = await Search(organisation);
        var tenantHit = Assert.Single(byName.Hits, h => h.Type == ConsoleSearchKinds.Tenant);
        Assert.Equal(organisation, tenantHit.Title);
        Assert.All(byName.Hits, h => Assert.Contains(h.Type, new[] { ConsoleSearchKinds.Tenant, ConsoleSearchKinds.Subscription, ConsoleSearchKinds.Invoice, ConsoleSearchKinds.Staff }));
        Assert.DoesNotContain(byName.Hits, h => h.Title.Contains("customer", StringComparison.Ordinal));
        Assert.Equal(tenantHit.Id, Assert.Single((await Search(owner.Email)).Hits, h => h.Type == ConsoleSearchKinds.Tenant).Id);
        // The tenant list's own search also finds a tenant by a user's e-mail.
        Assert.Equal(organisation, Assert.Single((await ReadAsync<PagedResult<ConsoleTenantResponse>>(
            await staff.Client.GetAsync("/api/v1/platform/tenants?q=" + Uri.EscapeDataString(owner.Email)))).Items).Name);
        // Subscriptions by plan code; subscription invoices by number (pointing to their tenant); staff by e-mail.
        Assert.Contains((await Search("TRIAL")).Hits, h => h.Type == ConsoleSearchKinds.Subscription);
        var invoiceHit = Assert.Single((await Search(checkout.Invoice.Number)).Hits, h => h.Type == ConsoleSearchKinds.Invoice);
        Assert.Equal(tenantHit.Id, invoiceHit.Id);
        Assert.Contains((await Search(staff.Email)).Hits, h => h.Type == ConsoleSearchKinds.Staff && h.Detail == staff.Email);
        Assert.Empty((await Search("x")).Hits);   // two characters at least
        // Not for tenants' users.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/v1/platform/search?q=" + Uri.EscapeDataString(organisation))).StatusCode);
    }

    /// <summary>
    /// Owner decision 13 Oct 2026 (DT-183): staff cancel an invoice awaiting payment by crediting it in full with a reason — the SUB-
    /// invoice is already a numbered tax invoice, so it is never voided. Only unpaid checkouts qualify; a late payment of the old page is
    /// recorded, not applied.
    /// </summary>
    [Fact]
    public async Task Staff_cancel_an_unpaid_checkout_by_a_full_credit_only()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        var plan = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("CXL"), 232m)));
        var checkout = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(plan.Id)));
        var tenant = (await ReadAsync<PagedResult<ConsoleTenantResponse>>(await staff.Client.GetAsync("/api/v1/platform/tenants?pageSize=200"))).Items.Single(t => t.OwnerEmail == owner.Email).Id;
        string Cancel(string invoice) => $"/api/v1/platform/tenants/{tenant}/invoices/{invoice}/cancel";

        // A reason is required; tenants' users cannot reach the console.
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.Client.PostAsJsonAsync(Cancel(checkout.Invoice.Id), new ReasonRequest(" "))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsJsonAsync(Cancel(checkout.Invoice.Id), new ReasonRequest("Not wanted"))).StatusCode);

        var cancelled = await ReadAsync<SubscriptionInvoiceResponse>(await staff.Client.PostAsJsonAsync(Cancel(checkout.Invoice.Id), new ReasonRequest("Plan withdrawn")));
        Assert.Equal(("Credited", 232.000m, checkout.Invoice.Number), (cancelled.Status, cancelled.Total, cancelled.Number));
        var listed = Assert.Single(await ReadAsync<List<SubscriptionInvoiceResponse>>(await owner.Client.GetAsync("/api/v1/tenant/subscription/invoices")));
        Assert.Equal("Credited", listed.Status);   // no longer awaiting payment on the tenant page
        Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "Credit" && a.EntityType == "CORE.SubscriptionInvoice"
            && a.NewValues!.Contains("Unpaid checkout cancelled: Plan withdrawn"))));

        // Twice, or any invoice not awaiting payment: refused (credited; a renewal — due without a payment page; paid).
        Assert.Equal("subscription.notAwaitingPayment", await (await staff.Client.PostAsJsonAsync(Cancel(checkout.Invoice.Id), new ReasonRequest("Again"))).ProblemCodeAsync());
        var renewal = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(plan.Id)));
        await f.DbAsync(async db =>
        {
            var i = await db.Set<SubscriptionInvoice>().IgnoreQueryFilters().SingleAsync(x => x.Number == renewal.Invoice.Number);
            i.GatewayReference = null;
            return await db.SaveChangesAsync();
        });
        Assert.Equal("subscription.notAwaitingPayment", await (await staff.Client.PostAsJsonAsync(Cancel(renewal.Invoice.Id), new ReasonRequest("Renewal"))).ProblemCodeAsync());
        var paid = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(plan.Id)));
        await (await WebhookAsync(paid.Invoice, "succeeded", plan.Code)).EnsureOkAsync();
        Assert.Equal("subscription.notAwaitingPayment", await (await staff.Client.PostAsJsonAsync(Cancel(paid.Invoice.Id), new ReasonRequest("Paid"))).ProblemCodeAsync());
        Assert.Equal("Active", (await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"))).Status);

        // A late payment from the cancelled invoice's old page: recorded for staff to refund, the invoice stays Credited, nothing applied.
        var before = await TenantOfAsync(owner);
        await (await WebhookAsync(checkout.Invoice, "succeeded", plan.Code)).EnsureOkAsync();
        await (await WebhookAsync(checkout.Invoice, "succeeded", plan.Code)).EnsureOkAsync();   // repeated: no change
        var stored = await f.DbAsync(db => db.Set<SubscriptionInvoice>().IgnoreQueryFilters().SingleAsync(x => x.Number == checkout.Invoice.Number));
        Assert.Equal(("Credited", true), (stored.Status, stored.PaidAt is not null));
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.Action == "PaidNotApplied" && a.EntityId == stored.Id)));
        Assert.Equal(before.CurrentPeriodEnd, (await TenantOfAsync(owner)).CurrentPeriodEnd);

        // Staff see it as a payment to refund (DT-184): on the platform list and in the tenant's panel, once; tenants cannot read the list.
        var refund = Assert.Single(await ReadAsync<List<RefundDueResponse>>(await staff.Client.GetAsync("/api/v1/platform/refunds")), x => x.TenantId == tenant);
        Assert.Equal((checkout.Invoice.Id, checkout.Invoice.Number, 232.000m, RefundKinds.Credited, true), (refund.InvoiceId, refund.Number, refund.Amount, refund.Kind, refund.PaidAt is not null));
        var details = await ReadAsync<ConsoleTenantDetails>(await staff.Client.GetAsync($"/api/v1/platform/tenants/{tenant}"));
        Assert.Equal(checkout.Invoice.Id, Assert.Single(details.Refunds!).InvoiceId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/v1/platform/refunds")).StatusCode);
    }

    [Fact]
    public async Task Checkout_webhook_upgrade_downgrade_and_credit_P04_P07_BR012()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        var basic = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("BASIC"), 50m)));
        var pro = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("PRO"), 100m)));
        var tiny = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("TINY"), 10m, users: 1)));

        var sub = await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"));
        Assert.Equal("Trial", sub.Status);
        Assert.Contains(sub.Plans, p => p.Id == basic.Id);
        var checkout = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(basic.Id)));
        Assert.StartsWith("SUB-", checkout.Invoice.Number, StringComparison.Ordinal);
        // Plan fees are tax-inclusive (owner decision 13 Oct 2026): the total is the fee; net and tax derived from it, adding up exactly.
        Assert.Equal((50.000m, 43.103m, 6.897m), (checkout.Invoice.Total, checkout.Invoice.Amount, checkout.Invoice.Tax));
        Assert.Equal(16m, sub.Plans.Single(p => p.Id == basic.Id).TaxRate);
        Assert.StartsWith("https://pay.test/checkout/", checkout.PaymentUrl, StringComparison.Ordinal);
        // Owner feedback 13 Oct 2026 (third round): an unpaid checkout is Due with its gateway reference (the screen shows it as awaiting
        // payment) and changes nothing until it is paid: the tenant stays on the trial plan. Plans staff have not published are not offered.
        var listed = Assert.Single(await ReadAsync<List<SubscriptionInvoiceResponse>>(await owner.Client.GetAsync("/api/v1/tenant/subscription/invoices")));
        Assert.Equal(("Due", true), (listed.Status, listed.GatewayReference is not null));
        var hidden = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("HIDDEN"), 70m, active: false)));
        var afterCheckout = await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"));
        Assert.Equal(("Trial", "TRIAL"), (afterCheckout.Status, afterCheckout.Plan.Code));
        Assert.DoesNotContain(afterCheckout.Plans, p => p.Id == hidden.Id);

        // A new checkout replaces the unpaid one: paying the old page later changes nothing (review F2).
        var stale = checkout;
        checkout = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(basic.Id)));
        await (await WebhookAsync(stale.Invoice, "succeeded", basic.Code)).EnsureOkAsync();
        Assert.Equal("Trial", (await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"))).Status);
        // …but staff see that payment to refund, although the replaced invoice is no longer listed (DT-184).
        Assert.Contains(await ReadAsync<List<RefundDueResponse>>(await staff.Client.GetAsync("/api/v1/platform/refunds")),
            x => x.Number == stale.Invoice.Number && x.Kind == RefundKinds.Replaced && x.Amount == stale.Invoice.Total);
        // A payment of another amount is not applied; the invoice is Failed and can still be paid in full (review F3).
        await (await WebhookAsync(checkout.Invoice, "succeeded", basic.Code, amount: 1.000m)).EnsureOkAsync();
        Assert.Equal("Failed", Assert.Single(await ReadAsync<List<SubscriptionInvoiceResponse>>(await owner.Client.GetAsync("/api/v1/tenant/subscription/invoices"))).Status);
        Assert.Equal("Trial", (await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"))).Status);

        // Unsigned or tampered webhooks are refused; the signed success activates the plan (BR-011).
        Assert.Equal(HttpStatusCode.Unauthorized, (await WebhookAsync(checkout.Invoice, "succeeded", basic.Code, "00")).StatusCode);
        await (await WebhookAsync(checkout.Invoice, "succeeded", basic.Code)).EnsureOkAsync();
        await (await WebhookAsync(checkout.Invoice, "succeeded", basic.Code)).EnsureOkAsync();   // repeated: no change
        sub = await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"));
        Assert.Equal("Active", sub.Status);
        Assert.Equal(basic.Id, sub.Plan.Id);
        Assert.NotNull(sub.CurrentPeriodEnd);
        var invoices = await ReadAsync<List<SubscriptionInvoiceResponse>>(await owner.Client.GetAsync("/api/v1/tenant/subscription/invoices"));
        Assert.Equal("Paid", Assert.Single(invoices).Status);
        var html = await (await owner.Client.GetAsync($"/api/v1/tenant/subscription/invoices/{invoices[0].Id}/print?format=html")).Content.ReadAsStringAsync();
        Assert.Contains(checkout.Invoice.Number, html, StringComparison.Ordinal);
        Assert.Contains("Price includes 16% sales tax", html, StringComparison.Ordinal);

        // Upgrade now, pro-rated (at most the price difference, tax included).
        var upgrade = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(pro.Id)));
        Assert.InRange(upgrade.Invoice.Total, 0.001m, 50m);
        Assert.Equal(upgrade.Invoice.Total, upgrade.Invoice.Amount + upgrade.Invoice.Tax);
        await (await WebhookAsync(upgrade.Invoice, "succeeded", pro.Code, kind: "upgrade")).EnsureOkAsync();
        Assert.Equal(pro.Id, (await ReadAsync<SubscriptionResponse>(await owner.Client.GetAsync("/api/v1/tenant/subscription"))).Plan.Id);
        Assert.Equal("subscription.notUpgrade", await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(basic.Id))).ProblemCodeAsync());

        // Downgrade refused while usage exceeds the target (two active users > 1), listing the limit.
        await SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Viewer);
        var refused = await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/downgrade", new PlanChangeRequest(tiny.Id));
        Assert.Equal("subscription.downgradeExceeds", await refused.ProblemCodeAsync());
        await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/downgrade", new PlanChangeRequest(basic.Id))).EnsureOkAsync();
        // Not a downgrade: another billing cycle (an annual price is not comparable), or a higher limit at a lower price (review F1).
        var annual = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("ANNUAL"), 20m) with { BillingCycle = "Annual" }));
        Assert.Equal("subscription.notDowngrade", await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/downgrade", new PlanChangeRequest(annual.Id))).ProblemCodeAsync());
        var wide = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("WIDE"), 20m, users: 50)));
        Assert.Equal("subscription.notDowngrade", await (await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/downgrade", new PlanChangeRequest(wide.Id))).ProblemCodeAsync());

        // A failed payment marks its invoice Failed; staff credits part of a due invoice (D-11).
        var again = await ReadAsync<CheckoutResponse>(await owner.Client.PostAsJsonAsync("/api/v1/tenant/subscription/checkout", new CheckoutRequest(pro.Id)));
        await (await WebhookAsync(again.Invoice, "failed", pro.Code)).EnsureOkAsync();
        var tenantToken = (await ReadAsync<PagedResult<ConsoleTenantResponse>>(await staff.Client.GetAsync("/api/v1/platform/tenants?pageSize=200"))).Items.Single(t => t.OwnerEmail == owner.Email).Id;
        var credited = await ReadAsync<SubscriptionInvoiceResponse>(await staff.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{tenantToken}/credit",
            new ApplyCreditRequest(again.Invoice.Id, 1.160m, "Goodwill")));
        Assert.Equal(again.Invoice.Total - 1.160m, credited.Total);
        Assert.Equal(credited.Total, credited.Amount + credited.Tax);
        Assert.Equal(Math.Round(credited.Total / 1.16m, 3, MidpointRounding.AwayFromZero), credited.Amount);   // still tax-inclusive
        var log = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "Credit" && a.EntityType == "CORE.SubscriptionInvoice"));
        Assert.True(log);
    }

    [Fact]
    public async Task Plan_limits_block_creation_but_not_reading_P07_BR013()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        var oneUser = await ReadAsync<PlanResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/plans", Plan(Code("ONE"), 5m, users: 1)));
        var code = oneUser.Code;
        await f.DbAsync(async db =>
        {
            var plan = await db.Set<Domain.Core.Plan>().SingleAsync(p => p.Code == code);
            var t = await db.Set<Tenant>().IgnoreQueryFilters().SingleAsync(t => db.Set<User>().IgnoreQueryFilters().Any(u => u.Email == owner.Email && u.TenantId == t.Id));
            t.PlanId = plan.Id;
            await db.SaveChangesAsync();
            return 0;
        });
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => SecurityTests.InviteAndAcceptAsync(f, owner, PresetRoles.Viewer));
        Assert.Contains("plan.limitReached", ex.Message, StringComparison.Ordinal);
        await (await owner.Client.GetAsync(U(owner, "/customers"))).EnsureOkAsync();
    }

    [Fact]
    public async Task Lifecycle_suspends_reactivates_cancels_and_deletes_with_anonymisation_5_2_G18()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        var customer = await ReadAsync<CustomerResponse>(await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            new CustomerRequest("Lifecycle Co", null, null, "0781234567", "a@b.test", null, null, "Sami", 0, null, null, null, null, null)));

        // Unpaid trial end → Suspended: read-only (BR-010), reading still allowed (BR-013).
        await SetTenantAsync(owner, t => t.TrialEndsAt = DateTime.UtcNow.AddDays(-1));
        await RunLifecycleAsync();
        Assert.Equal("Suspended", (await TenantOfAsync(owner)).Status);
        Assert.Equal("tenant.readOnly", await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            new CustomerRequest("Blocked Co", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null))).ProblemCodeAsync());
        await (await owner.Client.GetAsync(U(owner, "/customers"))).EnsureOkAsync();

        // Staff extend the trial (audited) → writes allowed again.
        var tenantToken = (await ReadAsync<PagedResult<ConsoleTenantResponse>>(await staff.Client.GetAsync("/api/v1/platform/tenants?pageSize=200"))).Items.Single(t => t.OwnerEmail == owner.Email).Id;
        // Reactivating a free trial that has ended would give a free paid period: refused, the trial is extended instead (review F4).
        Assert.Equal("subscription.freePlan", await (await staff.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{tenantToken}/reactivate",
            new TenantStatusRequest("Customer asked"))).ProblemCodeAsync());
        var extended = await ReadAsync<ConsoleTenantResponse>(await staff.Client.PostAsJsonAsync($"/api/v1/platform/tenants/{tenantToken}/extend-trial", new ExtendTrialRequest(7, "Sales call")));
        Assert.Equal("Trial", extended.Status);
        await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"),
            new CustomerRequest("Allowed Co", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null))).EnsureOkAsync();
        Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "ExtendTrial")));

        // Active period ends → renewal charged → still Active with a later period end.
        await SetTenantAsync(owner, t => { t.Status = "Active"; t.CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1); });
        await RunLifecycleAsync();
        var renewed = await TenantOfAsync(owner);
        Assert.Equal("Active", renewed.Status);
        Assert.True(renewed.CurrentPeriodEnd > DateTime.UtcNow);

        // Suspended for 30 days → Cancelled (Owner only, export); retention over → Deleted, data soft-deleted and personal data anonymised.
        await SetTenantAsync(owner, t => { t.Status = "Suspended"; t.GraceEndsAt = DateTime.UtcNow.AddDays(-31); });
        await RunLifecycleAsync();
        var cancelled = await TenantOfAsync(owner);
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.NotNull(cancelled.RetentionEndsAt);
        await (await owner.Client.GetAsync(U(owner, "/customers"))).EnsureOkAsync();   // the Owner can still export
        var tenantId = cancelled.Id;
        await SetTenantAsync(owner, t => t.RetentionEndsAt = DateTime.UtcNow.AddDays(-1));
        await RunLifecycleAsync();
        Assert.Equal("Deleted", await f.DbAsync(db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync()));
        Assert.False(await f.DbAsync(db => db.Set<Customer>().IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId && !c.IsDeleted)));
        var row = await f.DbAsync(db => db.Set<Customer>().IgnoreQueryFilters().Where(c => c.TenantId == tenantId && c.Name == "Lifecycle Co").Select(c => new { c.ContactPerson, c.Email, c.DeleteReason }).SingleAsync());
        Assert.Null(row.ContactPerson);
        Assert.Null(row.Email);
        Assert.Equal("Retention period ended", row.DeleteReason);
        Assert.False(await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenantId && u.Email == owner.Email)));
        Assert.NotNull(f.Emails.Last(owner.Email, "TenantDeleted"));
        _ = customer;
    }

    [Fact]
    public async Task Support_access_needs_owner_approval_and_is_read_only_and_audited_BR014()
    {
        var staff = await StaffAsync();
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"), new CustomerRequest("Support Co", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null)))
            .EnsureOkAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.Client.GetAsync(U(owner, "/customers"))).StatusCode);   // no window

        var tenantToken = (await ReadAsync<PagedResult<ConsoleTenantResponse>>(await staff.Client.GetAsync("/api/v1/platform/tenants?pageSize=200"))).Items.Single(t => t.OwnerEmail == owner.Email).Id;
        var requested = await ReadAsync<SupportAccessResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/support-access", new SupportAccessRequest(tenantToken, "Invoice printing issue")));
        Assert.Equal("Requested", requested.Status);
        Assert.Contains((await ReadAsync<NotificationPage>(await owner.Client.GetAsync(U(owner, "/notifications")))).Items, n => n.Type == "CORE.SupportRequested");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.Client.GetAsync(U(owner, "/customers"))).StatusCode);   // not approved yet
        Assert.DoesNotContain(await ReadAsync<List<CompanyAccess>>(await staff.Client.GetAsync("/api/v1/platform/support-access/companies")), c => c.Id == owner.CompanyId);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync("/api/v1/platform/support-access/companies")).StatusCode);   // staff only

        var grants = await ReadAsync<List<SupportAccessResponse>>(await owner.Client.GetAsync("/api/v1/tenant/support-access"));
        Assert.Equal("support.window", await (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{grants[0].Id}/approve",
            new SupportApprovalRequest(DateTime.UtcNow, DateTime.UtcNow.AddDays(8), null))).ProblemCodeAsync());
        await (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{grants[0].Id}/approve", new SupportApprovalRequest(null, null, "OK for today"))).EnsureOkAsync();

        // DT-152: the window's companies are listed for the staff member with the read-only session permissions.
        var open = Assert.Single(await ReadAsync<List<CompanyAccess>>(await staff.Client.GetAsync("/api/v1/platform/support-access/companies")), c => c.Id == owner.CompanyId);
        Assert.Equal("Support", open.Role);
        Assert.DoesNotContain(PermissionKeys.MastersManage, open.Permissions);
        var list = await ReadAsync<PagedResult<CustomerResponse>>(await staff.Client.GetAsync(U(owner, "/customers")));
        Assert.Contains(list.Items, c => c.Name == "Support Co");
        // Read-only (G-09): the session holds view permissions only; the write guard also refuses (support.readOnly).
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.PostAsJsonAsync(U(owner, "/customers"),
            new CustomerRequest("By support", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null))).StatusCode);
        Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "SupportRequest" && a.NewValues!.Contains("/customers"))));
        // Views only: synchronous exports and files are refused too (review M1), the list itself stays readable.
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.GetAsync(U(owner, "/customers/export"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.GetAsync(U(owner, "/accounting/journal-entries/export?format=csv"))).StatusCode);

        // The Owner ends the window → no access.
        await (await owner.Client.PostAsync($"/api/v1/tenant/support-access/{grants[0].Id}/end", null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.Client.GetAsync(U(owner, "/customers"))).StatusCode);
        Assert.DoesNotContain(await ReadAsync<List<CompanyAccess>>(await staff.Client.GetAsync("/api/v1/platform/support-access/companies")), c => c.Id == owner.CompanyId);
    }
}
