using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Application.Platform;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Contracts.Security;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DirectService.Platform.Application.Staff;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 item 2 (owner change request 15 Oct 2026): 2FA offered at the first sign-in with "Skip for now", the tenant setting
/// "Require 2FA for all users" (Owner only, default off), and platform staff always with 2FA.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TwoFactorPolicyTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    /// <summary>Signs up a new Owner and stops at the first password step.</summary>
    private async Task<(HttpClient Client, string Email, SignInResponse Response)> SignUpToPasswordStepAsync()
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        await (await client.PostAsJsonAsync("/api/v1/auth/sign-up", new SignUpRequest("Owner", email, ApiHelpers.Password, "Org " + Guid.NewGuid().ToString("N")[..8], true, "en"))).EnsureOkAsync();
        var link = f.Emails.LinkParameters(email, "VerifyEmail");
        (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new LinkTokenRequest(link["u"], link["t"]))).EnsureSuccessStatusCode();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, ApiHelpers.Password));
        await r.EnsureOkAsync();
        return (client, email, (await r.Content.ReadFromJsonAsync<SignInResponse>())!);
    }

    private async Task<SignInResponse> SignInAsync(string login, string password)
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, password, login));
        await r.EnsureOkAsync();
        return (await r.Content.ReadFromJsonAsync<SignInResponse>())!;
    }

    private static async Task RequireTwoFactorAsync(SignedInUser owner, bool on)
    {
        var t = await owner.Client.GetFromJsonAsync<TenantResponse>("/api/v1/tenant/");
        var r = await owner.Client.PutAsJsonAsync("/api/v1/tenant/security",
            new TenantSecurityRequest(t!.PasswordMinLength, t.PasswordRequireLetter, t.PasswordRequireDigit, t.SessionIdleTimeoutMinutes, t.RowVersion, on));
        await r.EnsureOkAsync();
        Assert.Equal(on, (await r.Content.ReadFromJsonAsync<TenantResponse>())!.RequireTwoFactor);
    }

    private static async Task<string> CreateUserAsync(SignedInUser owner, string role = PresetRoles.Accountant)
    {
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var userName = "u." + Guid.NewGuid().ToString("N")[..10];
        await (await owner.Client.PostAsJsonAsync(U(owner, "/users"),
            new CreateUserRequest("User", userName, null, ApiHelpers.Password, roles!.Single(r => r.Name == role).Id, false))).EnsureOkAsync();
        return userName;
    }

    [Fact]
    public async Task Sign_up_offers_2FA_and_skip_for_now_signs_in_and_is_audited()
    {
        var (client, email, response) = await SignUpToPasswordStepAsync();
        Assert.Equal("twoFactorOffer", response.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);   // not signed in yet

        var skipped = await client.PostAsync("/api/v1/auth/2fa/skip", null);
        await skipped.EnsureOkAsync();
        Assert.False((await skipped.Content.ReadFromJsonAsync<MeResponse>())!.TwoFactorEnabled);
        await client.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);

        // The Owner sees the offer again at every sign-in until 2FA is set up (owner decision 17 Oct 2026).
        Assert.Equal("twoFactorOffer", (await SignInAsync(email, ApiHelpers.Password)).Next);
        await f.DbAsync(async db =>
        {
            var u = await db.Set<User>().IgnoreQueryFilters().SingleAsync(x => x.Email == email);
            Assert.Null(u.TwoFactorEnabledAt);
            Assert.NotNull(u.LastSignInAt);
            Assert.Equal(1, await db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.EntityType == "CORE.User" && a.EntityId == u.Id && a.Action == "TwoFactorSkipped"));
            Assert.True(await db.Set<AuditLog>().IgnoreQueryFilters().CountAsync(a => a.EntityType == "CORE.User" && a.EntityId == u.Id && a.Action == "SignIn") >= 1);
            return true;
        });
    }

    [Fact]
    public async Task The_offered_set_up_can_be_completed_instead()
    {
        var (client, email, response) = await SignUpToPasswordStepAsync();
        Assert.Equal("twoFactorOffer", response.Next);
        var setup = await client.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/auth/2fa/setup");
        var done = await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null));
        await done.EnsureOkAsync();
        Assert.True((await done.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.Me.TwoFactorEnabled);
        Assert.Equal("twoFactor", (await SignInAsync(email, ApiHelpers.Password)).Next);
    }

    [Fact]
    public async Task Skip_is_refused_without_the_offer_stage()
    {
        var anonymous = f.NewClient();
        await anonymous.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/auth/2fa/skip", null)).StatusCode);

        // An enrolled user at the code step cannot skip the code.
        var owner = await f.SignUpOwnerAsync();
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, ApiHelpers.Password));
        Assert.Equal("twoFactor", (await r.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/2fa/skip", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);
    }

    [Fact]
    public async Task Require_2FA_for_all_users_forces_set_up_at_the_next_sign_in()
    {
        var owner = await f.SignUpOwnerAsync();
        var userName = await CreateUserAsync(owner);
        Assert.Equal("done", (await SignInAsync(userName, ApiHelpers.Password)).Next);   // default OFF

        await RequireTwoFactorAsync(owner, on: true);
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName));
        Assert.Equal("twoFactorSetup", (await r.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/2fa/skip", null)).StatusCode);   // no skip when required
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);
        var setup = await client.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/auth/2fa/setup");
        await (await client.PostAsJsonAsync("/api/v1/auth/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null))).EnsureOkAsync();

        await RequireTwoFactorAsync(owner, on: false);
        Assert.Equal("twoFactor", (await SignInAsync(userName, ApiHelpers.Password)).Next);   // once on, the user keeps it
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .CountAsync(a => a.EntityType == "CORE.Tenant" && a.Action == "SettingChange" && a.NewValues!.Contains("RequireTwoFactor") && a.NewValues.Contains("true")
                && db.Set<User>().IgnoreQueryFilters().Any(u => u.Email == owner.Email && u.TenantId == a.TenantId))));
    }

    [Fact]
    public async Task Turning_the_requirement_on_ends_the_sessions_of_users_without_2FA()
    {
        var owner = await f.SignUpOwnerAsync();   // enrolled (helper completes the set-up)
        var userName = await CreateUserAsync(owner);
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);

        await RequireTwoFactorAsync(owner, on: true);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.Client.GetAsync("/api/v1/me/")).StatusCode);   // the Owner (with 2FA) keeps the session
    }

    [Fact]
    public async Task Turning_the_requirement_on_after_the_offer_refuses_the_skip()
    {
        var (client, email, response) = await SignUpToPasswordStepAsync();
        Assert.Equal("twoFactorOffer", response.Next);
        await f.DbAsync(db => db.Set<Tenant>().IgnoreQueryFilters()
            .Where(t => db.Set<User>().IgnoreQueryFilters().Any(u => u.Email == email && u.TenantId == t.Id))
            .ExecuteUpdateAsync(t => t.SetProperty(x => x.RequireTwoFactor, true)));
        var skip = await client.PostAsync("/api/v1/auth/2fa/skip", null);
        Assert.Equal(HttpStatusCode.Unauthorized, skip.StatusCode);
        Assert.Equal("auth.twoFactorRequired", await skip.ProblemCodeAsync());
    }

    [Fact]
    public async Task Only_the_owner_changes_the_requirement()
    {
        var owner = await f.SignUpOwnerAsync();
        var admin = await CreateUserAsync(owner, PresetRoles.Administrator);
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, admin))).EnsureOkAsync();
        await (await client.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();
        await client.UseAntiforgeryAsync();
        var r = await client.PutAsJsonAsync("/api/v1/tenant/security", new TenantSecurityRequest(8, true, true, 30, null, true));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    /// <summary>Owner decision 17 Oct 2026: Owner/Administrator users see the offer at every sign-in (even created with a username); others once.</summary>
    [Fact]
    public async Task Owners_and_administrators_see_the_offer_at_every_sign_in_others_once()
    {
        var owner = await f.SignUpOwnerAsync();
        var admin = await CreateUserAsync(owner, PresetRoles.Administrator);
        var clerk = await CreateUserAsync(owner, PresetRoles.Accountant);
        for (var i = 0; i < 2; i++)
        {
            var client = f.NewClient();
            await client.UseAntiforgeryAsync();
            var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, admin));
            Assert.Equal("twoFactorOffer", (await r.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
            await (await client.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();   // skip allowed every time
        }
        Assert.Equal("done", (await SignInAsync(clerk, ApiHelpers.Password)).Next);   // a username user without the role: not asked

        // An invited email user without the role: offered at the first sign-in only.
        var roles = await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles"));
        var email = $"viewer-{Guid.NewGuid():N}@example.test";
        await (await owner.Client.PostAsJsonAsync(U(owner, "/invitations"), new InviteRequest(email, roles!.Single(x => x.Name == PresetRoles.Viewer).Id))).EnsureOkAsync();
        var invited = f.NewClient();
        await invited.UseAntiforgeryAsync();
        await (await invited.PostAsJsonAsync("/api/v1/auth/invitations/accept", new InvitationAcceptRequest(f.Emails.LinkParameters(email, "Invitation")["t"], "Viewer", ApiHelpers.Password))).EnsureOkAsync();
        var first = await invited.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, ApiHelpers.Password));
        Assert.Equal("twoFactorOffer", (await first.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
        await (await invited.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();
        Assert.Equal("done", (await SignInAsync(email, ApiHelpers.Password)).Next);
    }

    [Fact]
    public async Task Platform_staff_always_need_2FA_and_cannot_skip()
    {
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        using (var scope = f.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<StaffAccountService>().CreateAsync(email, "Support Agent", CancellationToken.None);
        var link = f.Emails.LinkParameters(email, "PasswordReset");
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        await (await client.PostAsJsonAsync("/api/v1/auth/password-reset/confirm", new PasswordResetConfirmRequest(link["u"], link["t"], ApiHelpers.Password))).EnsureOkAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(email, ApiHelpers.Password));
        Assert.Equal("twoFactorSetup", (await r.Content.ReadFromJsonAsync<SignInResponse>())!.Next);   // never the optional offer
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/2fa/skip", null)).StatusCode);
        Assert.True(DirectService.Platform.Application.Identity.AuthService.RequiresTwoFactor(new User { IsPlatformStaff = true, UserName = "staff" }, tenantRequires: false));
    }
}
