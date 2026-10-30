using System.Net;
using System.Text;
using DirectService.Platform.Contracts.Tenants;
using DirectService.Transportation.Web.Client.Layout;
using DirectService.Platform.UI.Layout;
using DirectService.Shared.UI.Layout;
using DirectService.Platform.UI.Pages.Console;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DirectService.Transportation.UI.Tests;

/// <summary>
/// Provider console → System email (DT-250…DT-252): settings with a write-only secret, pending vs active, saving with a fresh code, the test
/// email with plain-words reasons, status, the expiry banner, the delivery log with Resend, and the banner on every console page.
/// </summary>
public sealed class SystemEmailPageTests : PageTestBase
{
    private const string Path = "/platform/system-email";

    private static SystemEmailSettingsView Dev() => new("Development", null, null, false, null, null, "no-reply@directservice.local", "Direct Service Transportation",
        null, null, null, null, null, null, null);

    private static SystemEmailSettingsView M365(DateOnly? expires = null, bool? lastTestOk = null, string? lastError = null) => new("Microsoft365",
        "11111111-2222-3333-4444-555555555555", "66666666-7777-8888-9999-000000000000", true, new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
        expires ?? new DateOnly(2027, 10, 1), "no-reply@directservicetech.com", "Direct Service", "support@directservicetech.com", null, null, lastTestOk, lastError, null, "AAAA");

    private static SystemEmailStatus Status(int? daysLeft = null, bool? working = true) => new(working, new DateTime(2026, 10, 22, 7, 0, 0, DateTimeKind.Utc), 1, 2, 2, daysLeft, 30,
        working == false ? "wrongSecret" : null);

    private static PagedResult<EmailLogItem> Log() => new([
        new EmailLogItem("e1", "PasswordReset", "ar", "rana@example.test", "Failed", 6, DateTime.UtcNow, null, DateTime.UtcNow, null, "missingPermission", "HTTP 403", "Microsoft365", "newLink", null, null),
        new EmailLogItem("e2", "Invitation", "en", "sami@example.test", "Failed", 6, DateTime.UtcNow, null, DateTime.UtcNow, null, "mailboxNotFound", "HTTP 404", "Microsoft365", null, "askCompanyAdmin", null),
        new EmailLogItem("e3", "VerifyEmail", "ar", "huda@example.test", "Sent", 1, DateTime.UtcNow, DateTime.UtcNow, null, null, null, null, "Microsoft365", null, null, null),
    ], 1, 25, 3);

    private IRenderedComponent<SystemEmail> Open(SystemEmailResponse data, string language = "ar")
    {
        L.SetLanguage(language);
        Respond(Path + "/log", Log());
        Respond(Path, data);
        var cut = Render<SystemEmail>();
        cut.WaitForAssertion(() => Assert.Contains(L["sem.log"], Text(cut)));
        return cut;
    }

    private static AngleSharp.Dom.IElement Control(IRenderedComponent<SystemEmail> cut, string label) =>
        cut.Find("#" + cut.FindAll("label[for]").First(l => l.TextContent.Trim().TrimEnd('*').Trim() == label).GetAttribute("for"));

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void Microsoft_365_fields_appear_with_the_mode_and_the_secret_is_write_only(string language)
    {
        var cut = Open(new SystemEmailResponse(Dev(), null, Status(working: null)), language);
        Assert.Empty(cut.FindAll("input[type=password]"));
        Assert.Contains(L["sem.notYet"], Text(cut));
        Act(() => cut.Find("input[type=radio][value=Microsoft365]").Change(true));
        var secret = Control(cut, L["sem.clientSecret"]);
        Assert.Equal("password", secret.GetAttribute("type"));
        Assert.Equal("new-password", secret.GetAttribute("autocomplete"));
        Assert.True(string.IsNullOrEmpty(secret.GetAttribute("value")));
        Assert.Contains(L["sem.secretHelp"], Text(cut));
        Assert.NotNull(Control(cut, L["sem.tenantId"]));
        Assert.NotNull(Control(cut, L["sem.mailbox"]));
    }

    [Fact]
    public void A_saved_secret_is_shown_only_as_saved_on_and_expires_on()
    {
        var cut = Open(new SystemEmailResponse(M365(), null, Status()));
        var secret = Control(cut, L["sem.clientSecret"]);
        Assert.True(string.IsNullOrEmpty(secret.GetAttribute("value")));
        Assert.Equal(L["sem.secretKeep"], secret.GetAttribute("placeholder"));
        Assert.Contains("2027-10-01", Text(cut));
        Assert.Contains(L["sem.working"], Text(cut));
    }

    [Fact]
    public void Saving_asks_for_a_fresh_code_and_sends_it_with_the_settings()
    {
        var saved = new SystemEmailResponse(Dev(), M365(), Status());
        Respond(Path, req => req.Method == HttpMethod.Put ? saved : new SystemEmailResponse(Dev(), null, Status()));
        var cut = Open(new SystemEmailResponse(Dev(), null, Status()));
        Respond(Path, req => req.Method == HttpMethod.Put ? saved : new SystemEmailResponse(Dev(), null, Status()));
        Act(() => cut.Find("input[type=radio][value=Microsoft365]").Change(true));
        Act(() => Control(cut, L["sem.tenantId"]).Change("11111111-2222-3333-4444-555555555555"));
        Act(() => Control(cut, L["sem.clientId"]).Change("66666666-7777-8888-9999-000000000000"));
        Act(() => Control(cut, L["sem.clientSecret"]).Change("typed-secret"));
        Act(() => Control(cut, L["sem.mailbox"]).Change("no-reply@directservicetech.com"));
        Act(() => cut.Find("#sem-form").Submit());
        cut.WaitForAssertion(() => Assert.Contains(L["sem.codeText"], Text(cut)));
        Assert.DoesNotContain(Requests, r => r.Method == "PUT");

        // Without a code: asked again, nothing sent.
        Act(() => cut.Find("#sem-code").Submit());
        Assert.Contains(L["sem.codeRequired"], Text(cut));
        Act(() => cut.Find("#sem-code-input").Input("123456"));
        Act(() => cut.Find("#sem-code").Submit());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "PUT"));
        var body = Requests.Single(r => r.Method == "PUT").Body!;
        Assert.Contains("\"code\":\"123456\"", body, StringComparison.Ordinal);
        Assert.Contains("\"clientSecret\":\"typed-secret\"", body, StringComparison.Ordinal);
        cut.WaitForAssertion(() => Assert.Contains(L.F("sem.pendingBanner", L["sem.mode.Microsoft365"]), Text(cut)));
        // The secret typed is not kept in the form after saving.
        Assert.True(string.IsNullOrEmpty(Control(cut, L["sem.clientSecret"]).GetAttribute("value")));
    }

    [Fact]
    public void A_wrong_code_stays_in_the_dialog()
    {
        var cut = Open(new SystemEmailResponse(Dev(), null, Status()));
        Respond(Path, req => req.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"status\":400,\"code\":\"validation.failed\",\"detail\":\"x\",\"errors\":[{\"field\":\"code\",\"code\":\"auth.twoFactorInvalid\",\"message\":\"The verification code is incorrect.\"}]}",
                    Encoding.UTF8, "application/problem+json"),
            }
            : new SystemEmailResponse(Dev(), null, Status()));
        Act(() => cut.Find("#sem-form").Submit());
        Act(() => cut.Find("#sem-code-input").Input("000000"));
        Act(() => cut.Find("#sem-code").Submit());
        cut.WaitForAssertion(() => Assert.Contains("The verification code is incorrect.", cut.Find("#sem-code-err").TextContent));
        Assert.Equal("true", cut.Find("#sem-code-input").GetAttribute("aria-invalid"));
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public void A_test_email_says_why_it_failed_in_plain_words_and_when_new_settings_became_active(string language)
    {
        var cut = Open(new SystemEmailResponse(Dev(), M365(), Status()), language);
        Assert.Contains(L["sem.testHelpPending"], Text(cut));
        Respond(Path + "/test", new SystemEmailTestResponse(false, "wrongSecret", "AADSTS7000215: Invalid client secret provided.", false, [], new SystemEmailResponse(Dev(), M365(), Status())));
        Act(() => cut.Find("#sem-test").Input("ops@example.test"));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["sem.sendTest"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(L["sem.err.wrongSecret"], Text(cut)));
        Assert.Contains("AADSTS7000215", cut.Find("details .tech").TextContent);
        Assert.Contains("\"address\":\"ops@example.test\"", Requests.Last(r => r.Path == Path + "/test").Body);

        Respond(Path + "/test", new SystemEmailTestResponse(true, null, null, true, ["tenantWideGrant"], new SystemEmailResponse(M365(), null, Status())));
        Act(() => cut.FindAll("button").Single(b => b.TextContent.Trim() == L["sem.sendTest"]).Click());
        cut.WaitForAssertion(() => Assert.Contains(L.F("sem.testOkActivated", "ops@example.test"), Text(cut)));
        Assert.Contains(L["sem.warn.tenantWideGrant"], Text(cut));
        Assert.DoesNotContain(L.F("sem.pendingBanner", L["sem.mode.Microsoft365"]), Text(cut));
    }

    [Fact]
    public void The_secret_expiry_and_failing_state_are_shown()
    {
        var cut = Open(new SystemEmailResponse(M365(new DateOnly(2026, 11, 10)), null, Status(daysLeft: 19, working: false)));
        Assert.Contains(L.F("sem.secretExpires", "2026-11-10", 19), Text(cut));
        Assert.Contains(L["sem.failing"], Text(cut));
        Assert.Contains(L["sem.err.wrongSecret"], Text(cut));
    }

    [Fact]
    public void The_delivery_log_offers_resend_or_says_what_to_do_and_shows_no_content()
    {
        var cut = Open(new SystemEmailResponse(M365(), null, Status()));
        var rows = cut.FindAll("table.grid tbody tr");
        Assert.Equal(3, rows.Count);
        Assert.Contains(L["sem.tpl.PasswordReset"], rows[0].TextContent);
        Assert.Contains(L["sem.err.missingPermission"], rows[0].TextContent);
        Assert.Contains(L["sem.note.askCompanyAdmin"], rows[1].TextContent);
        Assert.Empty(rows[2].QuerySelectorAll("button"));
        Respond(Path + "/log/e1/resend", new EmailResendResponse("newLink"));
        Act(() => cut.FindAll("table.grid tbody tr")[0].QuerySelector("button")!.Click());
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Method == "POST" && r.Path == Path + "/log/e1/resend"));
        cut.WaitForAssertion(() => Assert.Contains(Services.GetRequiredService<DirectService.Shared.UI.Ui.ToastService>().Items, t => t.Text == L["sem.resentNewLink"]));

        // Filtering by status asks the server again.
        Act(() => cut.Find("select[aria-label]").Change("Failed"));
        cut.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path.Contains("log?page=1&pageSize=25&status=Failed", StringComparison.Ordinal)));
    }

    [Fact]
    public void Every_console_page_warns_when_email_needs_attention()
    {
        Respond(Path + "/alert", new SystemEmailAlertResponse(3, 12, false));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/console/tenants");
        var cut = Render<SystemEmailAlert>();
        cut.WaitForAssertion(() => Assert.Contains(L.F("sem.alertFailed", 3), Text(cut)));
        Assert.Contains(L.F("sem.alertSecret", 12), Text(cut));
        Assert.Equal("/console/system-email", cut.Find(".banner a").GetAttribute("href"));

        Respond(Path + "/alert", new SystemEmailAlertResponse(0, null, false));
        var quiet = Render<SystemEmailAlert>();
        quiet.WaitForAssertion(() => Assert.Contains(Requests, r => r.Path == Path + "/alert"));
        Assert.DoesNotContain("banner", quiet.Markup);
    }
}
