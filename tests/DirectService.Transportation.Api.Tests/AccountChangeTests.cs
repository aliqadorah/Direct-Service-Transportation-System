using System.Net;
using System.Net.Http.Json;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 21 Oct 2026, item 2 (DT-229): names and usernames are changed only by the Owner or an Administrator in Users &amp; roles
/// (any user they may manage, themselves too; the generic "not available"); email changes go to a pending address and take effect only when
/// the account's user, signed in, confirms the link sent to it — asked by the user with the password (+ code with 2FA), or started by an
/// Owner / Administrator. Sign-in and reset keep the old address until then; the old address gets a notice; every step is audited.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AccountChangeTests(ApiFactory f)
{
    private static string U(SignedInUser o, string path) => $"/api/v1/companies/{o.CompanyId}{path}";

    private static string Mail(string tag) => $"{tag}-{Guid.NewGuid():N}@example.test";

    private static async Task<string> RoleAsync(SignedInUser owner, string name) =>
        (await owner.Client.GetFromJsonAsync<List<RoleResponse>>(U(owner, "/roles")))!.Single(r => r.Name == name).Id;

    private static async Task<(string UserName, UserListItem User)> CreateAsync(SignedInUser owner, string role, string? email = null)
    {
        var userName = "u." + Guid.NewGuid().ToString("N")[..10];
        var r = await owner.Client.PostAsJsonAsync(U(owner, "/users"), new CreateUserRequest("Staff Member", userName, email, ApiHelpers.Password, role, false));
        await r.EnsureOkAsync();
        return (userName, (await r.Content.ReadFromJsonAsync<UserListItem>())!);
    }

    private async Task<HttpClient> SignInAsync(string login, string password = ApiHelpers.Password)
    {
        var client = f.NewClient();
        await client.UseAntiforgeryAsync();
        var r = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, password, login));
        await r.EnsureOkAsync();
        if ((await r.Content.ReadFromJsonAsync<SignInResponse>())!.Next == "twoFactorOffer") await (await client.PostAsync("/api/v1/auth/2fa/skip", null)).EnsureOkAsync();
        await client.UseAntiforgeryAsync();
        return client;
    }

    private static async Task<UserListItem> RowAsync(SignedInUser owner, string id) =>
        (await owner.Client.GetFromJsonAsync<DirectService.Shared.Contracts.PagedResult<UserListItem>>(U(owner, "/users?pageSize=200")))!.Items.Single(u => u.Id == id);

    private Task<List<string>> ActionsAsync(string email) => f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
        .Where(a => a.EntityType == "CORE.User" && db.Set<User>().IgnoreQueryFilters().Any(u => u.Id == a.EntityId && (u.Email == email || u.UserName == email)))
        .Select(a => a.Action).ToListAsync());

    // ------------------------------------------------------------ name and username

    [Fact]
    public async Task Only_the_Owner_or_an_Administrator_changes_names_and_usernames_with_the_generic_not_available()
    {
        var owner = await f.SignUpOwnerAsync();
        var (clerkName, clerk) = await CreateAsync(owner, await RoleAsync(owner, PresetRoles.Accountant));
        var (adminName, admin) = await CreateAsync(owner, await RoleAsync(owner, PresetRoles.Administrator));
        var (_, other) = await CreateAsync(owner, await RoleAsync(owner, PresetRoles.Accountant));

        // A normal user cannot change their own name or username (no users.manage, and not Owner / Administrator).
        var clerkClient = await SignInAsync(clerkName);
        var self = await clerkClient.PutAsJsonAsync(U(owner, $"/users/{clerk.Id}"), new UpdateUserRequest("Changed", clerkName + "x", clerk.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);

        // A custom role with users.manage is not enough either.
        var custom = await (await owner.Client.PostAsJsonAsync(U(owner, "/roles"), new RoleRequest("User clerk " + Guid.NewGuid().ToString("N")[..6],
            [PermissionKeys.UsersManage], null))).Content.ReadFromJsonAsync<RoleResponse>();
        var (managerName, _) = await CreateAsync(owner, custom!.Id);
        var manager = await SignInAsync(managerName);
        var refused = await manager.PutAsJsonAsync(U(owner, $"/users/{other.Id}"), new UpdateUserRequest("Changed", null, other.RowVersion));
        Assert.Equal((HttpStatusCode.Forbidden, "user.ownerOrAdministratorOnly"), (refused.StatusCode, await refused.ErrorCodeAsync()));

        // The Owner: another user (format checked, taken username refused with the generic answer, the new username signs in).
        Assert.Equal("user.userNameFormat", await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{clerk.Id}"),
            new UpdateUserRequest("Clerk", "bad name!", clerk.RowVersion))).ErrorCodeAsync());
        var taken = await owner.Client.PutAsJsonAsync(U(owner, $"/users/{clerk.Id}"), new UpdateUserRequest("Clerk", adminName.ToUpperInvariant(), clerk.RowVersion));
        Assert.Equal((HttpStatusCode.Conflict, "user.notAvailable"), (taken.StatusCode, await taken.ErrorCodeAsync()));
        Assert.Equal("validation.required", await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{clerk.Id}"),
            new UpdateUserRequest("Clerk", null, clerk.RowVersion))).ErrorCodeAsync());   // no email: the username stays required
        var renamed = "r." + Guid.NewGuid().ToString("N")[..10];
        clerk = await RowAsync(owner, clerk.Id);   // the clerk's sign-in changed the row
        await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{clerk.Id}"), new UpdateUserRequest("Clerk Renamed", renamed, clerk.RowVersion))).EnsureOkAsync();
        var row = await RowAsync(owner, clerk.Id);
        Assert.Equal(("Clerk Renamed", renamed), (row.FullName, row.UserName));
        await SignInAsync(renamed);
        Assert.Contains("ProfileChangedByAdmin", await ActionsAsync(renamed));
        var history = await f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters().Where(a => a.Action == "ProfileChangedByAdmin" && a.NewValues!.Contains(renamed)).Select(a => a.OldValues).SingleAsync());
        Assert.Contains(clerkName, history, StringComparison.Ordinal);   // old → new in the readable history

        // The Owner changes their own name; a username is never added to a user who signs in with their email (review F1/F2).
        var ownerRow = await RowAsync(owner, owner.Me.Id);
        Assert.Equal("user.userNameNotForEmailUser", await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{owner.Me.Id}"),
            new UpdateUserRequest("Owner", "owner.name", ownerRow.RowVersion))).ErrorCodeAsync());
        await (await owner.Client.PutAsJsonAsync(U(owner, $"/users/{owner.Me.Id}"), new UpdateUserRequest("Owner Renamed", null, ownerRow.RowVersion))).EnsureOkAsync();
        Assert.Equal("Owner Renamed", (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/me/"))!.FullName);

        // An Administrator: themselves and others, never the Owner.
        var adminClient = await SignInAsync(adminName);
        var adminRow = await RowAsync(owner, admin.Id);
        await (await adminClient.PutAsJsonAsync(U(owner, $"/users/{admin.Id}"), new UpdateUserRequest("Admin Renamed", adminName, adminRow.RowVersion))).EnsureOkAsync();
        var otherRow = await RowAsync(owner, other.Id);
        await (await adminClient.PutAsJsonAsync(U(owner, $"/users/{other.Id}"), new UpdateUserRequest("Other Renamed", otherRow.UserName, otherRow.RowVersion))).EnsureOkAsync();
        Assert.Equal("Other Renamed", (await RowAsync(owner, other.Id)).FullName);
        Assert.Equal("Admin Renamed", (await RowAsync(owner, admin.Id)).FullName);
        ownerRow = await RowAsync(owner, owner.Me.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminClient.PutAsJsonAsync(U(owner, $"/users/{owner.Me.Id}"), new UpdateUserRequest("Hijack", null, ownerRow.RowVersion))).StatusCode);
    }

    // ------------------------------------------------------------ email: by the user

    [Fact]
    public async Task A_user_changes_their_email_only_after_confirming_the_link_with_reauthentication_and_the_old_address_is_told()
    {
        var owner = await f.SignUpOwnerAsync();   // two-step verification is on for the Owner
        var other = await f.SignUpOwnerAsync();
        var anon = f.NewClient();
        await anon.UseAntiforgeryAsync();
        var newMail = Mail("owner-new");
        ChangeEmailRequest Ask(string mail, string? password, string? code) => new(mail, password, code);

        // With two-step verification: wrong password or wrong code → the same step-up refusal; the code is required.
        Assert.Equal("auth.stepUpFailed", await (await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(newMail, "Wrong-Pass-1", "123456"))).ErrorCodeAsync());
        Assert.Equal("auth.twoFactorStepUpCode", await (await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(newMail, owner.Password, null))).ErrorCodeAsync());
        Assert.Equal("auth.stepUpFailed", await (await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(newMail, owner.Password, "000000"))).ErrorCodeAsync());
        var taken = await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(other.Email, owner.Password, Totp.Code(owner.AuthenticatorKey)));
        Assert.Equal((HttpStatusCode.Conflict, "user.notAvailable"), (taken.StatusCode, await taken.ErrorCodeAsync()));
        Assert.Equal("user.emailUnchanged", await (await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(owner.Email, owner.Password, Totp.Code(owner.AuthenticatorKey)))).ErrorCodeAsync());

        var asked = await (await owner.Client.PostAsJsonAsync("/api/v1/me/email", Ask(newMail, owner.Password, Totp.Code(owner.AuthenticatorKey)))).Content.ReadFromJsonAsync<EmailChangeResponse>();
        Assert.Equal((owner.Email, newMail), (asked!.Email, asked.PendingEmail));
        Assert.Equal(newMail, (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/me/"))!.PendingEmail);
        var link = f.Emails.LinkParameters(newMail, "ConfirmEmailChange");
        Assert.DoesNotContain(newMail, f.Emails.Last(owner.Email, "EmailChangeAsked").TextBody, StringComparison.OrdinalIgnoreCase);   // told at once, masked (F8)

        // Until confirmed: the old address still signs in and gets reset links; the new one gets none.
        var before = f.Emails.Count;
        await (await anon.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(newMail))).EnsureOkAsync();
        Assert.Equal(before, f.Emails.Count);
        var oldSignIn = await anon.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, owner.Password));
        await oldSignIn.EnsureOkAsync();
        var reset = f.NewClient();
        await reset.UseAntiforgeryAsync();
        await (await reset.PostAsJsonAsync("/api/v1/auth/password-reset/request", new EmailRequest(owner.Email))).EnsureOkAsync();
        var oldReset = f.Emails.LinkParameters(owner.Email, "PasswordReset");   // a reset link to the old address, still valid now

        // Only the account's user, signed in, confirms it.
        var anonymous = await f.NewClient().PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]));
        Assert.True(anonymous.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);
        var otherTry = await other.Client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]));
        Assert.Equal((HttpStatusCode.Forbidden, "auth.verifySignIn"), (otherTry.StatusCode, await otherTry.ErrorCodeAsync()));
        Assert.Equal("auth.tokenInvalid", await (await owner.Client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"] + "x"))).ErrorCodeAsync());
        await (await owner.Client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]))).EnsureOkAsync();

        var me = (await owner.Client.GetFromJsonAsync<MeResponse>("/api/v1/me/"))!;   // this session stays
        Assert.Equal((newMail, (string?)null), (me.Email, me.PendingEmail));
        var notice = f.Emails.Last(owner.Email, "EmailChanged");
        Assert.Contains("contact your administrator", notice.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain(newMail, notice.TextBody, StringComparison.OrdinalIgnoreCase);   // masked
        Assert.Equal("auth.tokenInvalid", await (await owner.Client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]))).ErrorCodeAsync());
        // F3: the new security stamp ends every earlier link — the reset link sent to the old address no longer works.
        Assert.Equal("auth.tokenInvalid", await (await reset.PostAsJsonAsync("/api/v1/auth/password-reset/confirm",
            new PasswordResetConfirmRequest(oldReset["u"], oldReset["t"], "Taken-Over-Pass-9"))).ErrorCodeAsync());
        var oldClient = f.NewClient();
        await oldClient.UseAntiforgeryAsync();
        Assert.False((await oldClient.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(owner.Email, owner.Password))).IsSuccessStatusCode);   // the old address no longer signs in
        var newSignIn = f.NewClient();
        await newSignIn.UseAntiforgeryAsync();
        await (await newSignIn.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(newMail, owner.Password))).EnsureOkAsync();
        var actions = await ActionsAsync(newMail);
        Assert.Contains("EmailChangeRequested", actions);
        Assert.Contains("EmailChanged", actions);
    }

    [Fact]
    public async Task A_user_without_two_step_verification_adds_an_email_with_the_password_and_can_cancel()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, _) = await CreateAsync(owner, await RoleAsync(owner, PresetRoles.Accountant));
        var client = await SignInAsync(userName);
        Assert.Equal("auth.passwordWrong", await (await client.PostAsJsonAsync("/api/v1/me/email", new ChangeEmailRequest(Mail("x"), "Wrong-Pass-1"))).ErrorCodeAsync());

        // Ask, cancel: the first link is dead; ask again and confirm.
        var first = Mail("clerk");
        await (await client.PostAsJsonAsync("/api/v1/me/email", new ChangeEmailRequest(first, ApiHelpers.Password))).EnsureOkAsync();
        var cancelled = f.Emails.LinkParameters(first, "ConfirmEmailChange");
        await (await client.DeleteAsync("/api/v1/me/email")).EnsureOkAsync();
        Assert.Equal("auth.tokenInvalid", await (await client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(cancelled["u"], cancelled["t"]))).ErrorCodeAsync());
        var second = Mail("clerk2");
        await (await client.PostAsJsonAsync("/api/v1/me/email", new ChangeEmailRequest(second, ApiHelpers.Password))).EnsureOkAsync();
        var link = f.Emails.LinkParameters(second, "ConfirmEmailChange");
        await (await client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]))).EnsureOkAsync();
        var me = (await client.GetFromJsonAsync<MeResponse>("/api/v1/me/"))!;
        Assert.Equal(second, me.Email);
        Assert.True((await RowAsync(owner, me.Id)).EmailConfirmed);   // confirmed by its user: reset links may go to it
    }

    // ------------------------------------------------------------ email: started by an Owner / Administrator

    [Fact]
    public async Task An_email_change_started_by_the_Owner_takes_effect_only_when_the_user_confirms()
    {
        var owner = await f.SignUpOwnerAsync();
        var oldMail = Mail("clerk-old");
        var (userName, clerk) = await CreateAsync(owner, await RoleAsync(owner, PresetRoles.Accountant), oldMail);
        var newMail = Mail("clerk-new");

        Assert.Equal("user.emailOwnInProfile", await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{owner.Me.Id}/email"), new StartEmailChangeRequest(Mail("me")))).ErrorCodeAsync());
        Assert.Equal("user.notAvailable", await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{clerk.Id}/email"), new StartEmailChangeRequest(owner.Email))).ErrorCodeAsync());
        var started = await (await owner.Client.PostAsJsonAsync(U(owner, $"/users/{clerk.Id}/email"), new StartEmailChangeRequest(newMail))).Content.ReadFromJsonAsync<EmailChangeResponse>();
        Assert.Equal((oldMail, newMail), (started!.Email, started.PendingEmail));
        var row = await RowAsync(owner, clerk.Id);
        Assert.Equal((oldMail, newMail), (row.Email, row.PendingEmail));   // not changed yet
        Assert.Contains("administrator", f.Emails.Last(newMail, "ConfirmEmailChange").TextBody, StringComparison.Ordinal);
        var link = f.Emails.LinkParameters(newMail, "ConfirmEmailChange");

        // The Owner (who chose the address) cannot confirm it; only the user, signed in.
        var byOwner = await owner.Client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]));
        Assert.Equal((HttpStatusCode.Forbidden, "auth.verifySignIn"), (byOwner.StatusCode, await byOwner.ErrorCodeAsync()));
        Assert.Equal(oldMail, (await RowAsync(owner, clerk.Id)).Email);
        var client = await SignInAsync(userName);
        await (await client.PostAsJsonAsync("/api/v1/auth/confirm-email-change", new LinkTokenRequest(link["u"], link["t"]))).EnsureOkAsync();
        row = await RowAsync(owner, clerk.Id);
        Assert.Equal((newMail, (string?)null), (row.Email, row.PendingEmail));
        var actions = await ActionsAsync(newMail);
        Assert.Contains("EmailChangeStarted", actions);
        Assert.Contains("EmailChanged", actions);

        // A user who is not Owner / Administrator cannot start one.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(U(owner, $"/users/{owner.Me.Id}/email"), new StartEmailChangeRequest(Mail("y")))).StatusCode);
    }
}
