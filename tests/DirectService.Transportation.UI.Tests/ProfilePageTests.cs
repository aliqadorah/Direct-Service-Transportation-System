using System.Net;
using System.Text;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Web.Client.Pages;
using DirectService.Platform.UI.Pages;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Owner testing 19 Oct 2026: the profile manages two-step verification (DT-210 — turn on; turn off, change the authenticator app, new
/// recovery codes with the current password and a code; "Turn off" disabled when required) and lists only valid sessions with the
/// sign-in time, "This computer (local)" for the loopback address and "this session" on the current one (DT-211).
/// </summary>
public sealed class ProfilePageTests : PageTestBase
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private void World(TwoFactorStatusResponse status, IReadOnlyList<SessionResponse>? sessions = null)
    {
        Respond("/me/2fa", status);
        Respond("/me/sessions", sessions ?? [new SessionResponse("s1", Now.AddHours(-2), Now, "::1", "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0", true)]);
        Shell.SetMe(Shell.Me! with { TwoFactorEnabled = status.Enabled });
    }

    private static TwoFactorStatusResponse On(bool required = false, bool staff = false, int left = 10) => new(true, required, staff, left, Now.AddDays(-3));

    private IRenderedComponent<Profile> RenderProfile()
    {
        var cut = Render<Profile>(p => p.Add(x => x.Company, Company));
        cut.WaitForAssertion(() => Assert.Contains(L["profile.tfaTitle"], Text(cut)));
        return cut;
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<Profile> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal));

    private static HttpResponseMessage Problem(string json) =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") };

    [Fact]
    public void When_off_only_turn_on_is_offered()
    {
        World(new TwoFactorStatusResponse(false, false, false, 0, null));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Assert.Contains(L["profile.enable2fa"], Text(cut)));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["profile.tfaTurnOff"], StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Contains(L["profile.tfaChangeApp"], StringComparison.Ordinal));
    }

    [Fact]
    public void When_on_it_offers_turn_off_change_app_and_new_codes_with_the_codes_left()
    {
        World(On(left: 2));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Assert.Contains(L["profile.tfaChangeApp"], Text(cut)));
        Assert.Contains(L["profile.tfaNewCodes"], Text(cut));
        Assert.False(Button(cut, L["profile.tfaTurnOff"]).HasAttribute("disabled"));
        Assert.Contains(L.F("profile.tfaFewCodes", 2), Text(cut));   // few recovery codes left: a reminder
    }

    [Theory]
    [InlineData(false, "profile.tfaRequiredByOrg")]
    [InlineData(true, "profile.tfaStaffOnly")]
    public void Turn_off_is_disabled_with_an_explanation_when_required(bool staff, string reason)
    {
        World(On(required: true, staff: staff));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Assert.True(Button(cut, L["profile.tfaTurnOff"]).HasAttribute("disabled")));
        Assert.Contains(L[reason], Text(cut));
        Assert.Equal(L[reason], Button(cut, L["profile.tfaTurnOff"]).GetAttribute("title"));
    }

    [Fact]
    public void Turn_off_asks_for_password_and_code_on_their_fields_and_shows_the_server_answer_on_the_code()
    {
        World(On());
        Respond("/me/2fa/disable", _ => Problem("""{"status":400,"code":"validation.failed","detail":"x","errors":[{"field":"code","code":"auth.twoFactorInvalid","message":"The verification code is incorrect."}]}"""));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Button(cut, L["profile.tfaTurnOff"]));
        Act(() => Button(cut, L["profile.tfaTurnOff"]).Click());
        Assert.Contains(L["profile.tfaOffWarn"], Text(cut));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["profile.tfaTurnOff"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Equal("true", cut.Find("#tfa-password").GetAttribute("aria-invalid")));
        Assert.Equal("true", cut.Find("#tfa-code").GetAttribute("aria-invalid"));
        Assert.DoesNotContain(Requests, r => r.Path == "/me/2fa/disable");

        Act(() => cut.Find("#tfa-password").Change("Passw0rd-1"));
        Act(() => cut.Find("#tfa-code").Change("123 456"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["profile.tfaTurnOff"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("The verification code is incorrect.", Text(cut)));
        var body = Requests.Single(r => r.Path == "/me/2fa/disable").Body!;
        Assert.Contains("\"currentPassword\":\"Passw0rd-1\"", body, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"123456\"", body, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#tfa-code").GetAttribute("aria-invalid"));
        Assert.Null(cut.Find("#tfa-password").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void A_recovery_code_can_be_used_instead_of_the_app_code()
    {
        World(On());
        Respond("/me/2fa/recovery-codes", new RecoveryCodesResponse(["11111-22222", "33333-44444"], Shell.Me!));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Button(cut, L["profile.tfaNewCodes"]));
        Act(() => Button(cut, L["profile.tfaNewCodes"]).Click());
        Assert.Contains(L["profile.tfaCodesWarn"], Text(cut));
        Act(() => cut.FindAll(".dialog button.linkbtn").Single().Click());
        Act(() => cut.Find("#tfa-password").Change("Passw0rd-1"));
        Act(() => cut.Find("#tfa-recovery").Change(" ABCDE-12345 "));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["profile.tfaNewCodes"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains("33333-44444", Text(cut)));
        Assert.Contains(L["profile.tfaCodesDone"], Text(cut));
        var body = Requests.Single(r => r.Path == "/me/2fa/recovery-codes").Body!;
        Assert.Contains("\"recoveryCode\":\"ABCDE-12345\"", body, StringComparison.Ordinal);
        Assert.Contains("\"code\":null", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Changing_the_app_shows_the_new_key_then_confirms_with_its_code()
    {
        World(On());
        Respond("/me/2fa/change/confirm", Shell.Me!);
        Respond("/me/2fa/change", new TwoFactorSetupResponse("NEWKEY2345ABCDEF", "otpauth://totp/x", "<svg></svg>"));
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Button(cut, L["profile.tfaChangeApp"]));
        Act(() => Button(cut, L["profile.tfaChangeApp"]).Click());
        Act(() => cut.Find("#tfa-password").Change("Passw0rd-1"));
        Act(() => cut.Find("#tfa-code").Change("123456"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["profile.tfaContinue"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.True(Text(cut).Contains("NEWKEY2345ABCDEF", StringComparison.Ordinal), Text(cut)[..Math.Min(3000, Text(cut).Length)] + " | " + string.Join(", ", Requests.Select(r => r.Method + " " + r.Path))));
        Assert.Contains(L["profile.tfaChangeLead"], Text(cut));
        Act(() => cut.Find("#tfa-new-code").Change("654321"));
        Act(() => cut.FindAll(".dialog button").Last(b => b.TextContent.Contains(L["profile.tfaConfirmApp"], StringComparison.Ordinal)).Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == "/me/2fa/change/confirm" && r.Body!.Contains("\"code\":\"654321\"", StringComparison.Ordinal)));
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("#tfa-new-code")));   // done: the dialog closed (the toast is shown by the shell)
    }

    [Fact]
    public void Sessions_show_the_local_computer_the_sign_in_time_and_this_session()
    {
        var signedIn = new DateTime(2026, 10, 19, 6, 30, 0, DateTimeKind.Utc);
        World(On(), [
            new SessionResponse("s1", signedIn, Now, "::1", "Mozilla/5.0 (Windows NT 10.0) Chrome/130.0", true),
            new SessionResponse("s2", signedIn.AddHours(-1), Now.AddMinutes(-5), "10.1.2.3", "Mozilla/5.0 (iPhone) Safari/604.1", false),
        ]);
        var cut = RenderProfile();
        cut.WaitForAssertion(() => Assert.Contains(L["profile.localComputer"], Text(cut)));
        Assert.Contains("10.1.2.3", Text(cut));
        Assert.DoesNotContain("::1", Text(cut));
        Assert.Contains(L["profile.signedIn"], Text(cut));
        Assert.Contains(L["profile.thisSession"], Text(cut));
        Assert.Contains(L["profile.sessionsNote"], Text(cut));
        Assert.Contains(L["profile.signOutOthers"], Text(cut));
    }
}
