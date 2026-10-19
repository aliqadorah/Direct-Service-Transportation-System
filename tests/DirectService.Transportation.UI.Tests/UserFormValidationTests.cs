using System.Net;
using System.Text;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Client.Pages.Settings;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 18 Oct 2026: "Add user" showed "Please correct the highlighted fields" with no field highlighted (a 7-character
/// password). Now (DT-208) the organisation's password rules are shown under the field and checked before sending, every server field
/// error lands on its field with its own message, and a refusal without a field is spelled out in the summary. The error screen (DT-209)
/// says "connection lost" only when the server gave no answer.
/// </summary>
public sealed class UserFormValidationTests() : PageTestBase(PermissionKeys.UsersManage, PermissionKeys.SettingsManage, PermissionKeys.TripsView)
{
    private static readonly RoleResponse Viewer = new("r3", "Viewer", true, [PermissionKeys.TripsView], 0, "A");

    private void World(object? users = null)
    {
        Respond("roles/permissions", Array.Empty<PermissionItem>());
        Respond("roles", new[] { Viewer });
        Respond("users/password-policy", new PasswordPolicyResponse(8, true, true));
        Respond("users", users ?? new PagedResult<UserListItem>([], 1, 50, 0));
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    private IRenderedComponent<UsersRoles> OpenCreate()
    {
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains(L["usr.addUser"], StringComparison.Ordinal)));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["usr.addUser"], StringComparison.Ordinal)).Click();
        cut.FindAll(".dialog [role=tab]").Single(t => t.TextContent.Contains(L["usr.modeCreate"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(L["usr.rulesTitle"], Text(cut)));
        return cut;
    }

    private static void Fill(IRenderedComponent<UsersRoles> cut, string password, string userName = "Viewer0")
    {
        cut.Find("#new-name").Change("Viewer - مطلع");
        cut.Find("#new-username").Change(userName);
        cut.Find("#new-password").Input(password);
        cut.FindAll(".dialog select").Single().Change("0");
    }

    private void Create(IRenderedComponent<UsersRoles> cut) =>
        cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["usr.create"], StringComparison.Ordinal)).Click();

    [Fact]
    public void Password_rules_are_shown_before_typing_and_ticked_while_typing()
    {
        World();
        var cut = OpenCreate();
        Assert.Contains(L.F("usr.ruleLength", 8), Text(cut));
        Assert.Contains(L["usr.ruleLetter"], Text(cut));
        Assert.Contains(L["usr.ruleDigit"], Text(cut));
        Assert.Empty(cut.FindAll(".pwrules li.met"));
        cut.Find("#new-password").Input("abc1");
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".pwrules li.met").Count));   // letter and digit, not the length
    }

    [Fact]
    public void A_7_character_password_is_marked_on_its_field_with_the_missing_rule_and_nothing_is_sent()
    {
        World();
        var cut = OpenCreate();
        Fill(cut, "Abcdef1");
        Create(cut);
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#new-password").GetAttribute("aria-invalid")));
        var message = L.F("usr.passwordNeeds", L.F("usr.ruleLength", 8));
        Assert.Contains(message, Text(cut));
        Assert.Null(cut.Find("#new-username").GetAttribute("aria-invalid"));
        Assert.DoesNotContain(Requests, r => r.Method == "POST" && r.Path == "users");
    }

    [Fact]
    public void Empty_required_fields_and_a_bad_username_are_marked_on_their_fields()
    {
        World();
        var cut = OpenCreate();
        cut.Find("#new-username").Change("v");
        Create(cut);
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#new-name").GetAttribute("aria-invalid")));
        Assert.Equal("true", cut.Find("#new-username").GetAttribute("aria-invalid"));
        Assert.Equal("true", cut.Find("#new-password").GetAttribute("aria-invalid"));
        Assert.Contains(L["usr.userNameFormat"], Text(cut));
        Assert.Contains(L.F("ui.requiredField", L["acct.password"]), Text(cut));
        Assert.DoesNotContain(Requests, r => r.Method == "POST" && r.Path == "users");
    }

    [Fact]
    public void Server_field_errors_land_on_their_fields_and_a_refusal_without_field_is_spelled_out()
    {
        World();
        var answers = new Queue<HttpResponseMessage>(
        [
            Problem(HttpStatusCode.BadRequest, """{"status":400,"code":"validation.failed","detail":"Please correct the highlighted fields.","errors":[{"field":"password","code":"auth.passwordPolicy","message":"The password does not meet your organisation's policy: at least 10 characters, including at least one letter and one digit."}]}"""),
            Problem(HttpStatusCode.Conflict, """{"status":409,"code":"user.notAvailable","detail":"This username or email address is not available. Choose another one."}"""),
        ]);
        Respond("users", r => r.Method == HttpMethod.Post ? answers.Dequeue() : new PagedResult<UserListItem>([], 1, 50, 0));
        var cut = OpenCreate();
        Fill(cut, "Passw0rd-1");
        Create(cut);
        cut.WaitForAssertion(() => Assert.True(cut.Find("#new-password").GetAttribute("aria-invalid") == "true", Text(cut) + " | requests: " + string.Join(", ", Requests.Select(r => r.Method + " " + r.Path))));
        Assert.Contains("at least 10 characters", Text(cut));
        Assert.DoesNotContain("Please correct the highlighted fields.", Text(cut));

        cut.Find("#new-password").Input("Passw0rd-12");
        Create(cut);
        cut.WaitForAssertion(() => Assert.Contains("This username or email address is not available.", Text(cut)));
    }

    [Fact]
    public void Invite_marks_an_empty_or_malformed_email_on_its_field()
    {
        World();
        var cut = Render<UsersRoles>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("button"), b => b.TextContent.Contains(L["usr.addUser"], StringComparison.Ordinal)));
        cut.FindAll("button").First(b => b.TextContent.Contains(L["usr.addUser"], StringComparison.Ordinal)).Click();
        cut.Find("#inv-email").Change("not-an-email");
        cut.FindAll(".dialog button").Single(b => b.TextContent.Contains(L["usr.send"], StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#inv-email").GetAttribute("aria-invalid")));
        Assert.Contains(L["usr.emailFormat"], Text(cut));
        Assert.DoesNotContain(Requests, r => r.Method == "POST" && r.Path == "invitations");
    }
}
