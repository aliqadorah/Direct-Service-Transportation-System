using System.Net;
using System.Net.Http.Json;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Accounting.Domain.Cfg;
using DirectService.Platform.Domain;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>B1 identity: sign-up (US-31), email verification, mandatory 2FA (FR-177), lockout (FR-175), sessions (FR-176), reset (SEC-03/04).</summary>
[Collection(ApiCollection.Name)]
public sealed class IdentityTests(ApiFactory f)
{
    private static readonly string[] AllFilters = ["SoftDelete", "Tenant", "Company"];

    [Fact]
    public async Task Sign_up_creates_tenant_owner_company_roles_and_reference_data()
    {
        var owner = await f.SignUpOwnerAsync("Al Noor Transport");
        Assert.Single(owner.Me.Companies);
        Assert.Equal(PresetRoles.Owner, owner.Me.Companies[0].Role);
        Assert.Equal(PermissionKeys.All.Order(), owner.Me.Companies[0].Permissions.Order());
        Assert.Equal("Trial", owner.Me.TenantStatus);

        await f.DbAsync(async db =>
        {
            var user = await db.Set<User>().IgnoreQueryFilters(AllFilters).SingleAsync(u => u.Email == owner.Email);
            var tenant = await db.Set<Tenant>().IgnoreQueryFilters(AllFilters).SingleAsync(t => t.Id == user.TenantId);
            Assert.Equal("Trial", tenant.Status);
            Assert.InRange((tenant.TrialEndsAt!.Value - tenant.CreatedAt).TotalDays, 13.99, 14.01); // ASM-03
            var company = await db.Set<Company>().IgnoreQueryFilters(AllFilters).SingleAsync(c => c.TenantId == tenant.Id);
            Assert.Equal("Al Noor Transport", company.Name);

            var roles = await db.Set<Role>().IgnoreQueryFilters(AllFilters).Where(r => r.TenantId == tenant.Id)
                .Select(r => new { r.Name, r.IsPreset, Keys = r.RolePermissions.Select(p => p.Permission.PermissionKey).ToList() }).ToListAsync();
            Assert.Equal(5, roles.Count);
            foreach (var r in roles)
            {
                Assert.True(r.IsPreset);
                Assert.Equal(PresetRoles.Permissions[r.Name].Order(), r.Keys.Order());
            }

            Assert.Single(await db.Set<OwnershipType>().IgnoreQueryFilters(AllFilters).Where(o => o.CompanyId == company.Id && o.IsBuiltIn && o.FleetClass == "Own").ToListAsync());
            Assert.Equal(8, await db.Set<CompanySetting>().IgnoreQueryFilters(AllFilters).CountAsync(s => s.CompanyId == company.Id));
            var seqs = await db.Set<NumberSequence>().IgnoreQueryFilters(AllFilters).Where(s => s.CompanyId == company.Id).ToListAsync();
            Assert.Equal(12, seqs.Count);
            Assert.Equal("INV-", seqs.Single(s => s.DocumentType == "TRN.Invoice").Prefix);
            Assert.Equal("STL-", seqs.Single(s => s.DocumentType == "TRN.Settlement").Prefix);
            Assert.Equal(2, await db.Set<InvoiceLayout>().IgnoreQueryFilters(AllFilters).CountAsync(l => l.CompanyId == company.Id));
            Assert.False((await db.Set<EInvoiceSettings>().IgnoreQueryFilters(AllFilters).SingleAsync(e => e.CompanyId == company.Id)).IsEnabled);
            Assert.Equal(2, await db.Set<TermsAcceptance>().IgnoreQueryFilters(AllFilters).CountAsync(t => t.UserId == user.Id));
            Assert.NotNull(user.TwoFactorEnabledAt);
            Assert.NotNull(user.EmailConfirmedAt);
            Assert.NotEmpty(user.TwoFactorSecret!);
            return true;
        });
    }

    [Fact]
    public async Task Sign_up_with_a_used_email_is_refused()
    {
        var owner = await f.SignUpOwnerAsync();
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-up", new SignUpRequest("X", owner.Email, ApiHelpers.Password, "Other org", true, "en"));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("user.notAvailable", await r.ProblemCodeAsync());   // generic answer (DT-203)
    }

    [Fact]
    public async Task Sign_in_is_refused_until_the_email_is_verified()
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var email = $"u-{Guid.NewGuid():N}@example.test";
        await (await client.PostAsJsonAsync("/api/v1/auth/sign-up", new SignUpRequest("U", email, ApiHelpers.Password, "Org " + Guid.NewGuid().ToString("N")[..6], true, "en"))).EnsureOkAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, ApiHelpers.Password));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("auth.emailNotConfirmed", await r.ProblemCodeAsync());
    }

    [Fact]
    public async Task Password_step_alone_gives_no_access_two_factor_is_mandatory()
    {
        var owner = await f.SignUpOwnerAsync();
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest("000000", null));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("auth.twoFactorInvalid", await wrong.ProblemCodeAsync());
    }

    /// <summary>
    /// Owner decision 18 Oct 2026 (DT-204): five failures from one address block that address for the account for 15 minutes — with the
    /// ordinary "sign-in failed" answer, never "locked" — while the owner still signs in from another address; the block is audited.
    /// </summary>
    [Fact]
    public async Task Five_failed_sign_ins_from_one_address_block_only_that_address_with_the_ordinary_answer()
    {
        var owner = await f.SignUpOwnerAsync();
        var attacker = await ClientFromAsync("10.20.0.1");
        for (var i = 0; i < 5; i++)
        {
            var bad = await attacker.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, "wrong-password-1"));
            Assert.Equal((HttpStatusCode.Unauthorized, "auth.signInFailed"), (bad.StatusCode, await bad.ProblemCodeAsync()));
        }
        var blocked = await attacker.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password));
        Assert.Equal((HttpStatusCode.Unauthorized, "auth.signInFailed"), (blocked.StatusCode, await blocked.ProblemCodeAsync()));   // right password, same answer
        Assert.DoesNotContain("lock", await blocked.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var owners = await ClientFromAsync("10.20.0.2");
        var mine = await owners.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password));
        Assert.Equal("twoFactor", (await mine.Content.ReadFromJsonAsync<SignInResponse>())!.Next);   // not locked out by someone else
        await f.DbAsync(async db =>
        {
            var u = await db.Set<User>().IgnoreQueryFilters(AllFilters).SingleAsync(x => x.Email == owner.Email);
            Assert.Null(u.LockedUntil);   // the account itself is not locked (backstop: 20)
            var locks = await db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.EntityType == "CORE.User" && a.EntityId == u.Id && a.Action == "SignInLocked").ToListAsync();
            Assert.Equal("10.20.0.1", Assert.Single(locks).IpAddress);
            Assert.True(await db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.EntityId == u.Id && a.Action == "SignInFailed") >= 5);
            return true;
        });
    }

    /// <summary>DT-204 backstop: 20 failures from any addresses lock the account for everyone, with the ordinary answer; audited.</summary>
    [Fact]
    public async Task Twenty_failures_from_several_addresses_lock_the_account_for_everyone()
    {
        var owner = await f.SignUpOwnerAsync();
        for (var source = 1; source <= 4; source++)
        {
            var c = await ClientFromAsync($"10.30.0.{source}");
            for (var i = 0; i < 5; i++) await c.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, "wrong-password-1"));
        }
        var fresh = await ClientFromAsync("10.30.0.99");
        var refused = await fresh.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password));
        Assert.Equal((HttpStatusCode.Unauthorized, "auth.signInFailed"), (refused.StatusCode, await refused.ProblemCodeAsync()));
        await f.DbAsync(async db =>
        {
            var u = await db.Set<User>().IgnoreQueryFilters(AllFilters).SingleAsync(x => x.Email == owner.Email);
            Assert.InRange((u.LockedUntil!.Value - DateTime.UtcNow).TotalMinutes, 14, 15.1);
            // Three address blocks, then the 20th failure blocks the fourth address and locks the account: one audit row for that attempt.
            Assert.Equal(4, await db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.EntityId == u.Id && a.Action == "SignInLocked"));
            return true;
        });
    }

    private async Task<HttpClient> ClientFromAsync(string ip)
    {
        var c = f.NewClient();
        c.DefaultRequestHeaders.Add("X-Test-Client-IP", ip);
        await c.UseAntiforgeryAsync();
        return c;
    }

    [Fact]
    public async Task Recovery_code_signs_in_once()
    {
        var owner = await f.SignUpOwnerAsync();
        var code = owner.RecoveryCodes[0];
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var client = f.NewClient();
            await client.UseAntiforgeryAsync();
            await (await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password))).EnsureOkAsync();
            var r = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, code));
            Assert.Equal(attempt == 0 ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, r.StatusCode);
        }
        Assert.Equal(10, owner.RecoveryCodes.Count);
    }

    [Fact]
    public async Task Sign_out_ends_the_server_session()
    {
        var owner = await f.SignUpOwnerAsync();
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/v1/me")).StatusCode);
        await (await owner.Client.PostAsync("/api/v1/auth/sign-out", null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Sign_out_everywhere_keeps_only_the_current_session()
    {
        var owner = await f.SignUpOwnerAsync();
        var second = await f.SignInAsync(owner.Email, ApiHelpers.Password, owner.AuthenticatorKey);
        var sessions = await owner.Client.GetFromJsonAsync<List<SessionResponse>>("/api/v1/me/sessions");
        Assert.Equal(2, sessions!.Count);
        await (await owner.Client.PostAsync("/api/v1/me/sessions/sign-out-others", null)).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Password_reset_is_single_use_enforces_policy_and_ends_all_sessions()
    {
        var owner = await f.SignUpOwnerAsync();
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(owner.Email))).EnsureOkAsync();
        var link = f.Emails.LinkParameters(owner.Email, "PasswordReset");

        var weak = await anon.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(link["u"], link["t"], "short1"));
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(link["u"], link["t"], "NewPassw0rd-2"))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.Client.GetAsync("/api/v1/me")).StatusCode); // SEC-03

        var again = await anon.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(link["u"], link["t"], "NewPassw0rd-3"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, again.StatusCode); // SEC-04 single use
        await f.SignInAsync(owner.Email, "NewPassw0rd-2", owner.AuthenticatorKey);
    }

    [Fact]
    public async Task Unknown_email_reset_request_reveals_nothing()
    {
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        var r = await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest("nobody@example.test"));
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
        var s = await anon.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest("nobody@example.test", "whatever1"));
        Assert.Equal(HttpStatusCode.Unauthorized, s.StatusCode);
        Assert.Equal("auth.signInFailed", await s.ProblemCodeAsync());
    }

    [Fact]
    public async Task Preferences_are_saved()
    {
        var owner = await f.SignUpOwnerAsync();
        await (await owner.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("ar", owner.CompanyId, "yyyy-MM-dd"))).EnsureOkAsync();
        var me = await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal("ar", me!.LanguageCode);
        Assert.Equal("yyyy-MM-dd", me.DateFormat);
        Assert.Equal(owner.CompanyId, me.DefaultCompany);
        var bad = await owner.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("fr", null, "yyyy-MM-dd"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Matches("[؀-ۿ]", await bad.Content.ReadAsStringAsync());   // messages in Arabic at once (session cache refreshed)
        // FR-178: switching back to English changes the server's messages immediately, in the same session (found by the E2E tests).
        await (await owner.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("en", owner.CompanyId, "yyyy-MM-dd"))).EnsureOkAsync();
        var badEn = await owner.Client.PutAsJsonAsync("/api/v1/me/preferences", new PreferencesRequest("fr", null, "yyyy-MM-dd"));
        Assert.DoesNotMatch("[؀-ۿ]", await badEn.Content.ReadAsStringAsync());
    }
}
