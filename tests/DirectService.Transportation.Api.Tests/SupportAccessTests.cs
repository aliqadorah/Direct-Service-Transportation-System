using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Support access (BR-014, owner testing 19 Oct 2026, DT-213…DT-215): approval and refusal per tenant, isolation between tenants that share a
/// name (a nested IgnoreQueryFilters had listed every tenant's requests), the decision told to the staff member, the subscriber search.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SupportAccessTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<T> ReadAsync<T>(HttpResponseMessage r)
    {
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task<List<SupportTenantOption>> SearchAsync(SignedInUser staff, string q) =>
        await ReadAsync<List<SupportTenantOption>>(await staff.Client.GetAsync("/api/v1/platform/support-access/tenants?q=" + Uri.EscapeDataString(q)));

    private static async Task<SupportAccessResponse> RequestAsync(SignedInUser staff, SignedInUser owner, string reason)
    {
        var tenant = Assert.Single(await SearchAsync(staff, owner.Email)).Id;
        return await ReadAsync<SupportAccessResponse>(await staff.Client.PostAsJsonAsync("/api/v1/platform/support-access", new SupportAccessRequest(tenant, reason)));
    }

    private static async Task<List<SupportAccessResponse>> TenantListAsync(SignedInUser owner) =>
        await ReadAsync<List<SupportAccessResponse>>(await owner.Client.GetAsync("/api/v1/tenant/support-access"));

    private static async Task<SupportAccessResponse> ConsoleRowAsync(SignedInUser staff, string id) =>
        Assert.Single(await ReadAsync<List<SupportAccessResponse>>(await staff.Client.GetAsync("/api/v1/platform/support-access")), r => r.Id == id);

    [Fact]
    public async Task A_tenant_never_sees_or_decides_another_tenants_request_even_with_the_same_name()
    {
        var name = "Same Name " + Guid.NewGuid().ToString("N")[..6];
        var staff = await PlatformTests.StaffAsync(f);
        var a = await f.SignUpOwnerAsync(name);
        var b = await f.SignUpOwnerAsync(name);
        var requested = await RequestAsync(staff, a, "For A only");

        Assert.Empty(await TenantListAsync(b));
        Assert.Equal(requested.Id, Assert.Single(await TenantListAsync(a)).Id);
        // B holds A's token (e.g. from a shared link): every decision answers the same specific 404, nothing changes.
        var approve = await b.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/approve", new SupportApprovalRequest(null, null, null, 24));
        Assert.Equal(HttpStatusCode.NotFound, approve.StatusCode);
        Assert.Equal("support.requestNotFound", await approve.ProblemCodeAsync());
        var reject = await b.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/reject", new ReasonRequest("not mine"));
        Assert.Equal(HttpStatusCode.NotFound, reject.StatusCode);
        Assert.Equal("support.requestNotFound", await reject.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsync($"/api/v1/tenant/support-access/{requested.Id}/end", null)).StatusCode);
        Assert.Equal("Requested", Assert.Single(await TenantListAsync(a)).Status);
        Assert.Equal("support.notApproved", await (await staff.Client.GetAsync(U(a, "/customers"))).ProblemCodeAsync());   // 403 with the reason (DT-220)

        // A malformed token gets the same answer.
        Assert.Equal("support.requestNotFound", await (await a.Client.PostAsJsonAsync("/api/v1/tenant/support-access/not-a-token/approve",
            new SupportApprovalRequest(null, null, null, 24))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Approval_opens_a_read_only_window_shown_in_the_console_that_ends_at_its_end_time()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, "/customers"), new CustomerRequest("Window Co", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null)))
            .EnsureOkAsync();
        var requested = await RequestAsync(staff, owner, "Check invoices");

        var rows = await ReadAsync<List<SupportAccessResponse>>(await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/approve",
            new SupportApprovalRequest(null, null, null, 24)));
        Assert.Equal("Approved", Assert.Single(rows).Status);

        // The console shows the window and who approved it; the staff member is emailed.
        var row = await ConsoleRowAsync(staff, requested.Id);
        Assert.Equal("Approved", row.Status);
        Assert.Equal(owner.Me.FullName, row.DecidedBy);
        Assert.Equal(owner.Email, row.OwnerEmail);   // DT-219: "name · owner email" in the console list
        Assert.Equal(TimeSpan.FromHours(24), row.EndsAt!.Value - row.StartsAt!.Value);
        Assert.InRange(row.StartsAt.Value, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddMinutes(1));
        Assert.Contains("approved", f.Emails.Last(staff.Email, "SupportDecision").TextBody, StringComparison.Ordinal);

        // Read-only access inside the window.
        Assert.Contains((await ReadAsync<PagedResult<CustomerResponse>>(await staff.Client.GetAsync(U(owner, "/customers")))).Items, c => c.Name == "Window Co");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Client.PostAsJsonAsync(U(owner, "/customers"),
            new CustomerRequest("By support", null, null, "0781234567", null, null, null, null, 0, null, null, null, null, null))).StatusCode);

        // At the end time the access ends by itself (the window is moved into the past instead of waiting 24 hours).
        var pid = (await f.DbAsync(db => db.Set<SupportAccessGrant>().IgnoreQueryFilters().Where(g => g.TenantId == db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.Email == owner.Email).Select(u => u.TenantId!.Value).First()).Select(g => g.Id).SingleAsync()));
        await f.DbAsync(db => db.Set<SupportAccessGrant>().IgnoreQueryFilters().Where(g => g.Id == pid)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.StartsAt, DateTime.UtcNow.AddDays(-1)).SetProperty(g => g.EndsAt, DateTime.UtcNow.AddSeconds(-1))));
        Assert.Equal("support.windowExpired", await (await staff.Client.GetAsync(U(owner, "/customers"))).ProblemCodeAsync());   // 403 with the reason (DT-220)
        Assert.Equal("Expired", (await ConsoleRowAsync(staff, requested.Id)).Status);
        Assert.Equal("Expired", Assert.Single(await TenantListAsync(owner)).Status);
        // A second decision on a decided request is refused with its own message.
        Assert.Equal("support.notRequested", await (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/reject",
            new ReasonRequest("late"))).ProblemCodeAsync());
    }

    [Fact]
    public async Task Refusal_shows_refused_with_the_reason_and_tells_the_staff_member()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var owner = await f.SignUpOwnerAsync();
        var requested = await RequestAsync(staff, owner, "test");

        var rows = await ReadAsync<List<SupportAccessResponse>>(await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/reject",
            new ReasonRequest("Not needed now")));
        var mine = Assert.Single(rows);
        Assert.Equal("Rejected", mine.Status);
        Assert.Equal("Not needed now", mine.DecisionReason);

        var row = await ConsoleRowAsync(staff, requested.Id);
        Assert.Equal("Rejected", row.Status);
        Assert.Equal("Not needed now", row.DecisionReason);
        Assert.Equal(owner.Me.FullName, row.DecidedBy);
        Assert.Null(row.StartsAt);
        Assert.Contains("Not needed now", f.Emails.Last(staff.Email, "SupportDecision").TextBody, StringComparison.Ordinal);
        Assert.Equal("support.notApproved", await (await staff.Client.GetAsync(U(owner, "/customers"))).ProblemCodeAsync());   // 403 with the reason (DT-220)
        // A refusal needs a reason.
        var other = await RequestAsync(staff, owner, "again");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{other.Id}/reject", new ReasonRequest(" "))).StatusCode);
    }

    [Fact]
    public async Task Subscribers_are_found_by_name_or_any_users_email_owner_email_first()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var staff = await PlatformTests.StaffAsync(f);
        var a = await f.SignUpOwnerAsync("Twin " + tag);
        var b = await f.SignUpOwnerAsync("Twin " + tag);
        // A user of A whose email holds the other tenant's owner address as a part: an owner-email match must still come first.
        var member = await f.SignUpOwnerAsync();
        var memberEmail = "x" + b.Email;
        var aTenant = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == a.Email).Select(u => u.TenantId).SingleAsync());
        var ownTenant = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == member.Email).Select(u => u.TenantId).SingleAsync());
        // The member's own sign-up tenant is gone (deleted subscribers are never offered), so the member is only a user of A.
        await f.DbAsync(db => db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == ownTenant).ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "Deleted")));
        await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == member.Email)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.TenantId, aTenant).SetProperty(u => u.Email, memberEmail).SetProperty(u => u.NormalizedEmail, memberEmail.ToUpperInvariant())));

        // By name: both, each told apart by its owner email, plan and sign-up date.
        var byName = await SearchAsync(staff, "Twin " + tag);
        Assert.Equal(2, byName.Count);
        Assert.Equal(new[] { a.Email, b.Email }.Order(), byName.Select(t => t.OwnerEmail!).Order());
        Assert.All(byName, t => { Assert.False(string.IsNullOrEmpty(t.PlanName)); Assert.True(t.SignedUpAt > DateTime.UtcNow.AddHours(-1)); });
        Assert.NotEqual(byName[0].Id, byName[1].Id);

        // By the owner's email: B first (owner match), then A through its member's email, with that email named.
        var byEmail = await SearchAsync(staff, b.Email);
        Assert.Equal(b.Email, byEmail[0].OwnerEmail);
        Assert.Null(byEmail[0].MatchedEmail);
        var viaMember = Assert.Single(byEmail, t => t.OwnerEmail == a.Email);
        Assert.Equal(memberEmail, viaMember.MatchedEmail);

        // Too short a text finds nothing; tenants cannot search.
        Assert.Empty(await SearchAsync(staff, "T"));
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync("/api/v1/platform/support-access/tenants?q=Twin")).StatusCode);
    }

    // ------------------------------------------------------------------ a window that closes while the staff member is inside (DT-220)

    private async Task<(SignedInUser Staff, SignedInUser Owner, SupportAccessResponse Grant)> OpenWindowAsync()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var owner = await f.SignUpOwnerAsync();
        var requested = await RequestAsync(staff, owner, "Dashboard check");
        await (await owner.Client.PostAsJsonAsync($"/api/v1/tenant/support-access/{requested.Id}/approve", new SupportApprovalRequest(null, null, null, 24))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.Client.GetAsync(U(owner, "/dashboard"))).StatusCode);
        return (staff, owner, requested);
    }

    private static async Task<System.Text.Json.JsonElement> ProblemAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    }

    /// <summary>Every read of the company after the end: a 403 with a support reason, never a server error (the owner saw one on the dashboard).</summary>
    private async Task AllReadsRefusedAsync(SignedInUser staff, SignedInUser owner, string code)
    {
        var routes = f.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints.OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/v1/companies/{company}", StringComparison.Ordinal)
                && e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true)
            .Select(e => SecurityTests.Fill(e.RoutePattern.RawText!, owner.CompanyId)).Distinct().ToList();
        Assert.True(routes.Count > 100);
        foreach (var url in routes)
        {
            var r = await staff.Client.GetAsync(url);
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"GET {url} after the window closed returned {(int)r.StatusCode}");
            Assert.Equal(code, await r.ProblemCodeAsync());
        }
    }

    private Task<Guid> PublicIdAsync(string email) =>
        f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == email).Select(u => u.PublicId).SingleAsync());

    [Fact]
    public async Task A_window_ended_by_the_subscriber_refuses_with_403_ended_by_subscriber_never_500_DT220()
    {
        var (staff, owner, grant) = await OpenWindowAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);
        await (await owner.Client.PostAsync($"/api/v1/tenant/support-access/{grant.Id}/end", null)).EnsureOkAsync();

        var problem = await ProblemAsync(await staff.Client.GetAsync(U(owner, "/dashboard")));
        Assert.Equal("support.endedBySubscriber", problem.GetProperty("code").GetString());
        Assert.Equal(owner.Me.FullName, problem.GetProperty("endedBy").GetString());
        Assert.InRange(problem.GetProperty("endedAt").GetDateTime().ToUniversalTime(), before, DateTime.UtcNow.AddSeconds(5));
        Assert.Contains("ended the support session at", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        await AllReadsRefusedAsync(staff, owner, "support.endedBySubscriber");
        // The page address itself is the app (no data, no prerendering): a reload shows the app's message, never a raw problem document.
        var shellPage = await staff.Client.GetAsync($"/c/{owner.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, shellPage.StatusCode);
        Assert.Equal("text/html", shellPage.Content.Headers.ContentType?.MediaType);

        // Pushed to the staff member's open pages at once, with the tenant's company tokens; emailed; in the audit log; in the console list.
        var staffId = await PublicIdAsync(staff.Email);
        var push = Assert.Single(f.Live.SupportEnds, p => p.Staff == staffId);
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(push.Payload);
        Assert.Equal("support.endedBySubscriber", payload.GetProperty("reason").GetString());
        Assert.Contains(payload.GetProperty("companies").EnumerateArray(), c => c.GetString() == owner.CompanyId);
        Assert.Contains("ended your support session", f.Emails.Last(staff.Email, "SupportEnded").TextBody, StringComparison.Ordinal);
        Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.Action == "End" && a.EntityType == "CORE.SupportAccessGrant")));
        var row = await ConsoleRowAsync(staff, grant.Id);
        Assert.Equal("Expired", row.Status);
        Assert.Equal(owner.Me.FullName, row.EndedBy);
    }

    [Fact]
    public async Task A_window_past_its_end_refuses_with_403_window_expired_and_the_job_closes_it_DT220()
    {
        var (staff, owner, grant) = await OpenWindowAsync();
        var tenantId = await f.DbAsync(db => db.Set<User>().IgnoreQueryFilters().Where(u => u.Email == owner.Email).Select(u => u.TenantId!.Value).SingleAsync());
        var end = DateTime.UtcNow.AddSeconds(-1);
        await f.DbAsync(db => db.Set<SupportAccessGrant>().IgnoreQueryFilters().Where(g => g.TenantId == tenantId)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.StartsAt, DateTime.UtcNow.AddDays(-1)).SetProperty(g => g.EndsAt, end)));

        // Before the job runs: refused with the expiry reason already.
        var problem = await ProblemAsync(await staff.Client.GetAsync(U(owner, "/dashboard")));
        Assert.Equal("support.windowExpired", problem.GetProperty("code").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, problem.GetProperty("endedBy").ValueKind);

        // The window job closes it: Expired, "Expire" in the audit log, pushed and emailed; then every read answers the same.
        using (var scope = f.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<Application.Platform.SupportAccessService>().ExpireDueAsync(CancellationToken.None) >= 1);
        Assert.Equal("Expired", await f.DbAsync(db => db.Set<SupportAccessGrant>().IgnoreQueryFilters().Where(g => g.TenantId == tenantId).Select(g => g.Status).SingleAsync()));
        Assert.True(await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().AnyAsync(a => a.TenantId == tenantId && a.Action == "Expire")));
        var staffId = await PublicIdAsync(staff.Email);
        Assert.Contains(f.Live.SupportEnds, p => p.Staff == staffId
            && System.Text.Json.JsonSerializer.SerializeToElement(p.Payload).GetProperty("reason").GetString() == "support.windowExpired");
        Assert.Contains("support window", f.Emails.Last(staff.Email, "SupportEnded").TextBody, StringComparison.Ordinal);
        await AllReadsRefusedAsync(staff, owner, "support.windowExpired");
        var row = await ConsoleRowAsync(staff, grant.Id);
        Assert.Equal("Expired", row.Status);
        Assert.Null(row.EndedBy);
        // Nothing left to close: the job does not close it twice.
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Application.Platform.SupportAccessService>().ExpireDueAsync(CancellationToken.None);
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.TenantId == tenantId && a.Action == "Expire")));
    }

    [Fact]
    public async Task A_request_not_approved_refuses_with_403_not_approved_and_no_grant_stays_404_DT220()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var owner = await f.SignUpOwnerAsync();
        var stranger = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.Client.GetAsync(U(stranger, "/dashboard"))).StatusCode);   // no grant: nothing revealed
        await RequestAsync(staff, owner, "Pending");
        var problem = await ProblemAsync(await staff.Client.GetAsync(U(owner, "/dashboard")));
        Assert.Equal("support.notApproved", problem.GetProperty("code").GetString());
    }
}
