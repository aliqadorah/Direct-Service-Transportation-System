using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages;
using DirectService.Transportation.Web.Client.Pages.Settings;
using DirectService.Shared.UI.Pages.Settings;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 21 Oct 2026, item 2 (DT-229): the profile keeps name and username read-only (an Owner / Administrator gets "Edit" to their
/// row in Users &amp; roles) and offers "Change email" (new address, current password, + code with 2FA; the pending address with "Cancel");
/// Users &amp; roles has the Owner / Administrator dialogs for name + username and for starting an email change.
/// </summary>
public sealed class AccountChangePageTests : PageTestBase
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private void ProfileWorld(bool twoFactor)
    {
        Respond("/me/2fa", new TwoFactorStatusResponse(twoFactor, false, false, 10, twoFactor ? Now.AddDays(-3) : null));
        Respond("/me/sessions", new[] { new SessionResponse("s1", Now.AddHours(-2), Now, "::1", "Chrome", true) });
        Respond("/me/email", new EmailChangeResponse("user@example.test", "new@example.test"));
        Shell.SetMe(Shell.Me! with { TwoFactorEnabled = twoFactor, UserName = "test.user" });
    }

    private IRenderedComponent<Profile> RenderProfile()
    {
        var cut = Render<Profile>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["profile.tfaTitle"], Text(cut)));
        return cut;
    }

    private static AngleSharp.Dom.IElement Button<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll("button, a.btn").First(b => b.TextContent.Contains(text, StringComparison.Ordinal));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Profile_sends_the_email_change_with_password_and_code_and_shows_the_pending_address(string language)
    {
        L.SetLanguage(language);
        ProfileWorld(twoFactor: true);
        var cut = RenderProfile();
        Assert.All(cut.FindAll("input[readonly]"), i => Assert.True(i.HasAttribute("readonly")));
        Assert.Contains(L["profile.nameHelp"], Text(cut));   // name and username stay read-only here

        Act(() => Button(cut, L["profile.changeEmail"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["profile.changeEmailLead"], Text(cut)));
        Act(() => cut.Find("#em-new").Change("new@example.test"));
        Act(() => cut.Find("#em-password").Change("Passw0rd-1"));
        Act(() => cut.Find("#em-code").Change("123 456"));
        Act(() => Button(cut, L["profile.sendEmailLink"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "/me/email"));
        var body = Requests.Last(r => r.Method == "POST" && r.Path == "/me/email").Body!;
        Assert.Contains("\"newEmail\":\"new@example.test\"", body, StringComparison.Ordinal);
        Assert.Contains("\"currentPassword\":\"Passw0rd-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"123456\"", body, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Contains(L.F("profile.emailPending", "new@example.test"), Text(cut)));

        Act(() => Button(cut, L["profile.emailCancel"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "DELETE" && r.Path == "/me/email"));
        cut.WaitForAssertion(() => Assert.DoesNotContain(L.F("profile.emailPending", "new@example.test"), Text(cut)));
    }

    [Fact]
    public void Without_two_step_verification_no_code_is_asked()
    {
        ProfileWorld(twoFactor: false);
        var cut = RenderProfile();
        Act(() => Button(cut, L["profile.changeEmail"]).Click());
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("#em-password")));
        Assert.Empty(cut.FindAll("#em-code"));
    }

    [Fact]
    public void Profile_offers_edit_in_users_and_roles_only_to_the_Owner_or_an_Administrator()
    {
        ProfileWorld(twoFactor: false);
        var cut = RenderProfile();
        Assert.Contains(cut.FindAll("a"), a => a.GetAttribute("href")?.EndsWith("settings/users?user=u1", StringComparison.Ordinal) == true);

        Shell.SetMe(Shell.Me! with { Companies = [Shell.Me.Companies[0] with { Role = "Accountant" }] });
        Shell.SelectCompany(Company);
        var other = RenderProfile();
        Assert.DoesNotContain(other.FindAll("a"), a => a.GetAttribute("href")?.Contains("settings/users", StringComparison.Ordinal) == true);
    }

    // ------------------------------------------------------------ Users & roles

    private IRenderedComponent<UsersRoles> RenderUsers()
    {
        var owner = new RoleResponse("r1", "Owner", true, [PermissionKeys.TenantManage, PermissionKeys.UsersManage], 1, "A");
        var clerk = new RoleResponse("r2", "Accountant", true, [PermissionKeys.TripsView], 1, "A");
        Respond("roles/permissions", Array.Empty<PermissionItem>());
        Respond("roles", new[] { owner, clerk });
        Respond("users", new PagedResult<UserListItem>([new UserListItem("u2", "كاتب", null, "Active", new Ref("r2", "Accountant"), false, false, false, null, "RV2", "clerk.one",
            false, "pending@example.test")], 1, 50, 1));
        Respond("users/u2/email", new EmailChangeResponse(null, "clerk@example.test"));
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains("clerk.one", cut.Markup));
        Act(() => cut.Find("tbody tr").Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("usr.emailPending", "pending@example.test"), Text(cut)));
        return cut;
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void The_edit_dialog_checks_and_saves_name_and_username(string language)
    {
        L.SetLanguage(language);
        var cut = RenderUsers();
        Act(() => Button(cut, L["usr.editAccount"]).Click());
        cut.WaitForAssertion(() => Assert.Equal("clerk.one", cut.Find("#edit-username").GetAttribute("value")));
        Assert.Equal("كاتب", cut.Find("#edit-name").GetAttribute("value"));
        Act(() => cut.Find("#edit-username").Change("bad name!"));
        Act(() => cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["usr.userNameFormat"], Text(cut)));
        Assert.DoesNotContain(Requests, r => r.Method == "PUT");

        Act(() => cut.Find("#edit-name").Change("كاتب الحسابات"));
        Act(() => cut.Find("#edit-username").Change("clerk.two"));
        Act(() => cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["ui.save"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT" && r.Path == "users/u2"));
        var body = Requests.Single(r => r.Method == "PUT" && r.Path == "users/u2").Body!;
        Assert.Contains("\"userName\":\"clerk.two\"", body, StringComparison.Ordinal);
        Assert.Contains("\"rowVersion\":\"RV2\"", body, StringComparison.Ordinal);
        Assert.Contains("كاتب الحسابات", System.Text.RegularExpressions.Regex.Unescape(body), StringComparison.Ordinal);
    }

    [Fact]
    public void The_email_dialog_starts_a_change_that_the_user_confirms()
    {
        var cut = RenderUsers();
        Act(() => Button(cut, L["profile.changeEmail"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["usr.changeEmailNote"], Text(cut)));
        Act(() => cut.Find("#email-new").Change("clerk@example.test"));
        Act(() => cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["profile.sendEmailLink"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == "users/u2/email"));
        Assert.Contains("\"newEmail\":\"clerk@example.test\"", Requests.Last(r => r.Path == "users/u2/email").Body, StringComparison.Ordinal);
    }

    [Fact]
    public void A_users_manager_who_is_not_Owner_or_Administrator_gets_no_edit_buttons()
    {
        Shell.SetMe(Shell.Me! with { Companies = [Shell.Me.Companies[0] with { Role = "User clerk", IsPresetRole = false }] });
        Shell.SelectCompany(Company);
        var cut = RenderUsers();
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["usr.editAccount"], StringComparison.Ordinal));
        Assert.DoesNotContain(L["profile.emailCancel"], Text(cut));
    }
}
