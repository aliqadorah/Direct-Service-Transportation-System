using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Revision 6 item 1 (owner change request 15 Oct 2026): users created by an administrator with a username and password — sign-in with
/// username or email, no 2FA required, forced password change, no self-service reset without email, administrator reset (audited), same lockout.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class UserNameSignInTests(ApiFactory f)
{
    private const string NewPassword = "N3w-Password-1";

    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static async Task<string> RoleAsync(SignedInUser owner, string name) =>
        (await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles")))!.Single(r => r.Name == name).Id;

    private static string NewUserName() => "u." + Guid.NewGuid().ToString("N")[..10];

    private static async Task<(string UserName, UserListItem User)> CreateAsync(SignedInUser owner, bool mustChange, string? email = null, string role = PresetRoles.Accountant)
    {
        var userName = NewUserName();
        var r = await owner.Client.PostAsJsonAsync(U(owner, "/users"),
            new CreateUserRequest("Username User", userName, email, ApiHelpers.Password, await RoleAsync(owner, role), mustChange));
        await r.EnsureOkAsync();
        return (userName, (await r.Content.ReadFromJsonAsync<UserListItem>())!);
    }

    private async Task<(HttpClient Client, SignInResponse Response)> PasswordStepAsync(string login, string password)
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, password, login));
        await r.EnsureOkAsync();
        return (client, (await r.Content.ReadFromJsonAsync<SignInResponse>())!);
    }

    [Fact]
    public async Task Created_user_signs_in_with_the_username_without_2FA_and_is_audited()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateAsync(owner, mustChange: false);
        Assert.Null(user.Email);
        Assert.Equal(userName, user.UserName);

        // Case-insensitive username; no second step, the response carries the profile and the session works.
        var (client, response) = await PasswordStepAsync(userName.ToUpperInvariant(), ApiHelpers.Password);
        Assert.Equal("done", response.Next);
        Assert.NotNull(response.Me);
        Assert.Equal(userName, response.Me!.UserName);
        Assert.False(response.Me.TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);

        var audit = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .Where(a => a.EntityType == "CORE.User" && a.Action == "CreatedWithPassword" && a.NewValues!.Contains(userName)).CountAsync());
        Assert.Equal(1, audit);
    }

    [Fact]
    public async Task Created_user_with_email_signs_in_with_either_name()
    {
        var owner = await f.SignUpOwnerAsync();
        var email = $"staff-{Guid.NewGuid():N}@example.test";
        var (userName, _) = await CreateAsync(owner, mustChange: false, email: email);
        Assert.Equal("done", (await PasswordStepAsync(email, ApiHelpers.Password)).Response.Next);
        Assert.Equal("done", (await PasswordStepAsync(userName, ApiHelpers.Password)).Response.Next);
    }

    [Fact]
    public async Task Must_change_password_blocks_the_session_until_a_new_password_is_chosen()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateAsync(owner, mustChange: true);
        Assert.True(user.MustChangePassword);

        var (client, response) = await PasswordStepAsync(userName, ApiHelpers.Password);
        Assert.Equal("changePassword", response.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);   // no session yet

        // Policy and "different from the current one" apply.
        var weak = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest("short"));
        Assert.Equal("auth.passwordPolicy", await weak.ErrorCodeAsync());
        var same = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest(ApiHelpers.Password));
        Assert.Equal("auth.passwordUnchanged", await same.ErrorCodeAsync());

        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest(NewPassword));
        await changed.EnsureOkAsync();
        Assert.Equal("done", (await changed.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
        await client.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);

        // The old password no longer works; the new one signs straight in.
        var old = f.NewClient();
        await old.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).StatusCode);
        Assert.Equal("done", (await PasswordStepAsync(userName, NewPassword)).Response.Next);
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .Join(db.Set<User>().IgnoreQueryFilters(), a => a.EntityId, u => u.Id, (a, u) => new { a, u })
            .CountAsync(x => x.a.EntityType == "CORE.User" && x.a.Action == "PasswordChanged" && x.u.UserName == userName)));
    }

    [Fact]
    public async Task Change_password_endpoint_needs_the_password_stage()
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest(NewPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Username_rules_and_uniqueness_are_enforced()
    {
        var owner = await f.SignUpOwnerAsync();
        var role = await RoleAsync(owner, PresetRoles.Viewer);
        foreach (var bad in new[] { "ab", "has space", "name@domain", "عربي", new string('a', 51) })
        {
            var r = await owner.Client.PostAsJsonAsync(U(owner, "/users"), new CreateUserRequest("X", bad, null, ApiHelpers.Password, role));
            Assert.Equal("user.userNameFormat", await r.ErrorCodeAsync());
        }
        var (userName, _) = await CreateAsync(owner, mustChange: false);

        // Unique platform-wide, case-insensitively — also from another tenant.
        var other = await f.SignUpOwnerAsync();
        var dup = await other.Client.PostAsJsonAsync(U(other, "/users"),
            new CreateUserRequest("X", userName.ToUpperInvariant(), null, ApiHelpers.Password, await RoleAsync(other, PresetRoles.Viewer)));
        Assert.Equal((HttpStatusCode.Conflict, "user.notAvailable"), (dup.StatusCode, await dup.ErrorCodeAsync()));   // DT-203: generic, never which
        var usedEmail = await other.Client.PostAsJsonAsync(U(other, "/users"),
            new CreateUserRequest("X", NewUserName(), owner.Email, ApiHelpers.Password, await RoleAsync(other, PresetRoles.Viewer)));
        Assert.Equal((HttpStatusCode.Conflict, "user.notAvailable"), (usedEmail.StatusCode, await usedEmail.ErrorCodeAsync()));
        var weak = await other.Client.PostAsJsonAsync(U(other, "/users"),
            new CreateUserRequest("X", NewUserName(), null, "weak", await RoleAsync(other, PresetRoles.Viewer)));
        Assert.Equal("auth.passwordPolicy", await weak.ErrorCodeAsync());
    }

    [Fact]
    public async Task Only_users_managers_create_users_and_never_with_a_role_beyond_their_own()
    {
        var owner = await f.SignUpOwnerAsync();
        var (accountantName, _) = await CreateAsync(owner, mustChange: false);
        var (accountant, _) = await PasswordStepAsync(accountantName, ApiHelpers.Password);
        await accountant.UseAntiforgeryAsync();
        var r = await accountant.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users",
            new CreateUserRequest("X", NewUserName(), null, ApiHelpers.Password, await RoleAsync(owner, PresetRoles.Viewer)));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);

        var (adminName, _) = await CreateAsync(owner, mustChange: false, role: PresetRoles.Administrator);
        var (admin, offered) = await PasswordStepAsync(adminName, ApiHelpers.Password);
        Assert.Equal("twoFactorOffer", offered.Next);   // Administrators always see the offer (owner decision 17 Oct 2026)
        await (await admin.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();
        await admin.UseAntiforgeryAsync();
        var escalate = await admin.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users",
            new CreateUserRequest("X", NewUserName(), null, ApiHelpers.Password, await RoleAsync(owner, PresetRoles.Owner)));
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);   // DT-18
    }

    /// <summary>Owner decision 17 Oct 2026: an email entered by the administrator is verified first; until then no reset email goes to it.</summary>
    [Fact]
    public async Task An_admin_entered_email_is_verified_before_any_reset_link_goes_to_it()
    {
        var owner = await f.SignUpOwnerAsync();
        var email = $"clerk-{Guid.NewGuid():N}@example.test";
        var (userName, user) = await CreateAsync(owner, mustChange: false, email: email);
        Assert.False(user.EmailConfirmed);
        var verify = f.Emails.LinkParameters(email, "VerifyEmail");   // the verification email was sent

        // Not verified: "forgot password" sends nothing, and the administrator cannot send a link (only set a new password).
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        var before = f.Emails.Count;
        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(email))).EnsureOkAsync();
        Assert.Equal(before, f.Emails.Count);
        Assert.Equal("user.emailNotVerified", await (await owner.Client.PostAsync(U(owner, $"/users/{user.Id}/password-reset"), null)).ErrorCodeAsync());
        var (session, signedIn) = await PasswordStepAsync(userName, ApiHelpers.Password);
        Assert.Equal("done", signedIn.Next);   // sign-in does not wait for it

        // Owner decision 18 Oct 2026 (DT-203): whoever holds the link cannot verify it without being signed in as the user —
        // neither anonymously nor as another user (the Owner); the user's own session confirms it and stays signed in.
        var anonymous = await anon.PostAsJsonAsync("/api/v1/auth/verify-email", new LinkTokenRequest(verify["u"], verify["t"]));
        Assert.Equal((HttpStatusCode.Forbidden, "auth.verifySignIn"), (anonymous.StatusCode, await anonymous.ErrorCodeAsync()));
        Assert.Equal("auth.verifySignIn",
            await (await owner.Client.PostAsJsonAsync("/api/v1/auth/verify-email", new LinkTokenRequest(verify["u"], verify["t"]))).ErrorCodeAsync());
        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(email))).EnsureOkAsync();
        Assert.Equal(before, f.Emails.Count);   // still nothing sent
        await session.UseAntiforgeryAsync();
        await (await session.PostAsJsonAsync("/api/v1/auth/verify-email", new LinkTokenRequest(verify["u"], verify["t"]))).EnsureOkAsync();
        await (await session.GetAsync("/api/v1/me/")).EnsureOkAsync();   // still signed in

        // Verified: the reset email goes out.
        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(email))).EnsureOkAsync();
        Assert.True(f.Emails.LinkParameters(email, "PasswordReset").ContainsKey("t"));
    }

    [Fact]
    public async Task A_user_without_email_gets_no_reset_link_only_an_admin_reset_which_is_audited()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateAsync(owner, mustChange: false);

        // Anonymous "forgot password" with the username sends nothing (and says nothing).
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        var before = f.Emails.Count;
        Assert.Equal(HttpStatusCode.Accepted, (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(userName))).StatusCode);
        Assert.Equal(before, f.Emails.Count);

        // The administrator cannot email a link to a user without email…
        var link = await owner.Client.PostAsync(U(owner, $"/users/{user.Id}/password-reset"), null);
        Assert.Equal("user.noEmail", await link.ErrorCodeAsync());

        // …but sets a new password: sessions end, the user must change it at the next sign-in, and it is audited.
        var (session, _) = await PasswordStepAsync(userName, ApiHelpers.Password);
        var set = await owner.Client.PostAsJsonAsync(U(owner, $"/users/{user.Id}/set-password"), new SetPasswordRequest(NewPassword));
        await set.EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/v1/me/")).StatusCode);
        Assert.Equal("changePassword", (await PasswordStepAsync(userName, NewPassword)).Response.Next);
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .Join(db.Set<User>().IgnoreQueryFilters(), a => a.EntityId, u => u.Id, (a, u) => new { a, u })
            .CountAsync(x => x.a.EntityType == "CORE.User" && x.a.Action == "PasswordSetByAdmin" && x.u.UserName == userName)));
    }

    [Fact]
    public async Task Set_password_is_refused_for_users_who_sign_in_with_their_email()
    {
        var owner = await f.SignUpOwnerAsync();
        var list = await owner.Client.GetFromJsonAsync<PagedResult<UserListItem>>(U(owner, "/users"));
        var self = list!.Items.Single(u => u.Email == owner.Email);
        var r = await owner.Client.PostAsJsonAsync(U(owner, $"/users/{self.Id}/set-password"), new SetPasswordRequest(NewPassword));
        Assert.Equal("user.passwordSetNotAllowed", await r.ErrorCodeAsync());
    }

    [Fact]
    public async Task Username_users_get_the_same_lockout()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateAsync(owner, mustChange: false);
        var client = f.NewClient();
        client.DefaultRequestHeaders.Add("X-Test-Client-IP", "10.40.0.1");
        await client.UseAntiforgeryAsync();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, "Wrong-pass-1", userName));
        var locked = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName));
        Assert.Equal((HttpStatusCode.Unauthorized, "auth.signInFailed"), (locked.StatusCode, await locked.ErrorCodeAsync()));   // DT-204: ordinary answer

        // The administrator's unlock clears the address blocks too.
        await (await owner.Client.PostAsync(U(owner, $"/users/{user.Id}/unlock"), null)).EnsureOkAsync();
        var again = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName));
        Assert.Equal("done", (await again.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
    }

    /// <summary>
    /// Owner decision 18 Oct 2026 (DT-205): a user with 2FA whom an administrator gave a new password enters the code first, then chooses
    /// the new password; the change step cannot be reached with the password alone.
    /// </summary>
    [Fact]
    public async Task With_2FA_the_code_comes_before_the_forced_password_change()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateAsync(owner, mustChange: false);
        var (session, _) = await PasswordStepAsync(userName, ApiHelpers.Password);
        await session.UseAntiforgeryAsync();
        var setup = await session.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/me/2fa/setup");
        await session.UseAntiforgeryAsync();
        await (await session.PostAsJsonAsync("/api/v1/me/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null))).EnsureOkAsync();
        await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{user.Id}/set-password"), new SetPasswordRequest(NewPassword))).EnsureOkAsync();

        var (client, first) = await PasswordStepAsync(userName, NewPassword);
        Assert.Equal("twoFactor", first.Next);   // not "changePassword"
        var early = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest("An0ther-Passw0rd"));
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);   // the password alone does not reach the change

        var code = await client.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null));
        Assert.Equal("changePassword", (await code.Content.ReadFromJsonAsync<SignInResponse>())!.Next);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me/")).StatusCode);   // no session before the change
        var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password", new NewPasswordRequest("An0ther-Passw0rd"));
        var done = (await changed.Content.ReadFromJsonAsync<SignInResponse>())!;
        Assert.Equal("done", done.Next);   // signed in without a second code
        Assert.NotNull(done.Me);
        await client.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);
    }

    [Fact]
    public async Task A_username_user_can_turn_2FA_on_later_and_then_needs_it()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, _) = await CreateAsync(owner, mustChange: false);
        var (client, _) = await PasswordStepAsync(userName, ApiHelpers.Password);
        await client.UseAntiforgeryAsync();

        var setup = await client.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/me/2fa/setup");
        Assert.NotNull(setup!.QrSvg);
        await client.UseAntiforgeryAsync();   // the cookie was re-issued (new security stamp)
        var done = await client.PostAsJsonAsync("/api/v1/me/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null));
        await done.EnsureOkAsync();
        var codes = (await done.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!;
        Assert.Equal(10, codes.RecoveryCodes.Count);
        Assert.True(codes.Me.TwoFactorEnabled);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);   // this session stays signed in

        Assert.Equal("twoFactor", (await PasswordStepAsync(userName, ApiHelpers.Password)).Response.Next);
        Assert.Equal(1, await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
            .Join(db.Set<User>().IgnoreQueryFilters(), a => a.EntityId, u => u.Id, (a, u) => new { a, u })
            .CountAsync(x => x.a.EntityType == "CORE.User" && x.a.Action == "TwoFactorEnabled" && x.u.UserName == userName)));
    }

    [Fact]
    public async Task A_user_without_email_changes_the_password_in_the_profile()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, _) = await CreateAsync(owner, mustChange: false);
        var (client, _) = await PasswordStepAsync(userName, ApiHelpers.Password);
        await client.UseAntiforgeryAsync();
        var wrong = await client.PostAsJsonAsync("/api/v1/me/password", new ChangePasswordRequest("Wrong-pass-1", NewPassword));
        Assert.Equal("auth.passwordWrong", await wrong.ErrorCodeAsync());
        await (await client.PostAsJsonAsync("/api/v1/me/password", new ChangePasswordRequest(ApiHelpers.Password, NewPassword))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me/")).StatusCode);
        Assert.Equal("done", (await PasswordStepAsync(userName, NewPassword)).Response.Next);
    }
}
