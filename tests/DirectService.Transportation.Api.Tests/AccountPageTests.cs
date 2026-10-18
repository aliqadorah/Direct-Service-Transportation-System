using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// The server-rendered sign-in screens (SCR-01, SCR-03, SCR-05) after the owner decisions of 18 Oct 2026: the email-verification page of an
/// administrator-entered email asks to sign in first (DT-203), the sign-in page answers a blocked address with the ordinary message
/// (DT-204), and the two-step page leads a user with 2FA to the forced password change after the code (DT-205). Rendered and posted over
/// HTTP like a browser (form handler + antiforgery token).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class AccountPageTests(ApiFactory f)
{
    private const string NewPassword = "N3w-Password-1";

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The text a reader sees (punctuation is wrapped in its own elements for right-to-left display).</summary>
    private static string Text(string html) => Spaces().Replace(Tag().Replace(html, string.Empty), " ");

    private static async Task<(HttpResponseMessage Response, string Page)> PostFormAsync(HttpClient c, string url, string handler, params (string Name, string Value)[] fields)
    {
        var page = await c.GetStringAsync(url);
        var body = new List<KeyValuePair<string, string>> { new("_handler", handler), new("__RequestVerificationToken", WebUtility.HtmlDecode(Antiforgery().Match(page).Groups[1].Value)) };
        body.AddRange(fields.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)));
        var r = await c.PostAsync(url, new FormUrlEncodedContent(body));
        var html = WebUtility.HtmlDecode(await r.Content.ReadAsStringAsync());
        return (r, html);
    }

    private static async Task<(string UserName, UserListItem User)> CreateUserAsync(SignedInUser owner, string? email = null)
    {
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles"))!;
        var userName = "p." + Guid.NewGuid().ToString("N")[..10];
        var r = await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users",
            new CreateUserRequest("Page User", userName, email, ApiHelpers.Password, roles.Single(x => x.Name == PresetRoles.Accountant).Id, false));
        await r.EnsureOkAsync();
        return (userName, (await r.Content.ReadFromJsonAsync<UserListItem>())!);
    }

    [Fact]
    public async Task Verify_page_of_an_admin_entered_email_asks_to_sign_in_and_confirms_once_signed_in()
    {
        var owner = await f.SignUpOwnerAsync();
        var email = $"page-{Guid.NewGuid():N}@example.test";
        var (userName, _) = await CreateUserAsync(owner, email);
        var link = f.Emails.LinkParameters(email, "VerifyEmail");
        var url = $"/verify-email?u={Uri.EscapeDataString(link["u"])}&t={Uri.EscapeDataString(link["t"])}";

        var browser = f.NewClient();
        var (_, asked) = await PostFormAsync(browser, url, "verify");
        Assert.Contains("To confirm it, sign in with that account's username", Text(asked), StringComparison.Ordinal);
        Assert.Contains("/sign-in?", asked, StringComparison.Ordinal);
        Assert.Contains("returnUrl=" + Uri.EscapeDataString(url), asked, StringComparison.Ordinal);   // sign-in comes back to this link
        Assert.DoesNotContain("Your email address is confirmed.", Text(asked), StringComparison.Ordinal);

        // The page's own link: sign in on the sign-in page, which returns to the email link; then confirm.
        var (back, _) = await PostFormAsync(browser, "/sign-in?lang=en&returnUrl=" + Uri.EscapeDataString(url), "sign-in", ("Input.Email", userName), ("Input.Password", ApiHelpers.Password));
        Assert.Equal("/verify-email", back.RequestMessage!.RequestUri!.AbsolutePath);
        var (doneResponse, done) = await PostFormAsync(browser, url, "verify");
        Assert.True(doneResponse.IsSuccessStatusCode, ((int)doneResponse.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("Your email address is confirmed.", Text(done), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_page_answers_a_blocked_address_with_the_ordinary_message()
    {
        var owner = await f.SignUpOwnerAsync();
        var browser = f.NewClient();
        browser.DefaultRequestHeaders.Add("X-Test-Client-IP", "10.50.0.1");
        for (var i = 0; i < 5; i++)
            await PostFormAsync(browser, "/sign-in?lang=en", "sign-in", ("Input.Email", owner.Email), ("Input.Password", "wrong-password-1"));
        var (r, page) = await PostFormAsync(browser, "/sign-in?lang=en", "sign-in", ("Input.Email", owner.Email), ("Input.Password", ApiHelpers.Password));
        Assert.EndsWith("/sign-in", r.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);   // still on the sign-in page
        Assert.Contains(ErrorCatalog.Format(ErrorCodes.SignInFailed, "en"), Text(page), StringComparison.Ordinal);
        Assert.DoesNotContain("locked", Text(page), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Two_step_page_leads_to_the_forced_password_change_after_the_code()
    {
        var owner = await f.SignUpOwnerAsync();
        var (userName, user) = await CreateUserAsync(owner);
        var session = f.NewClient();
        await session.UseAntiforgeryAsync();
        await (await session.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).EnsureOkAsync();
        await session.UseAntiforgeryAsync();
        var setup = await session.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/me/2fa/setup");
        await session.UseAntiforgeryAsync();
        await (await session.PostAsJsonAsync("/api/v1/me/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null))).EnsureOkAsync();
        await (await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users/{user.Id}/set-password", new SetPasswordRequest(NewPassword))).EnsureOkAsync();

        var browser = f.NewClient();
        var (signedIn, _) = await PostFormAsync(browser, "/sign-in?lang=en", "sign-in", ("Input.Email", userName), ("Input.Password", NewPassword));
        Assert.Equal("/two-factor", signedIn.RequestMessage!.RequestUri!.AbsolutePath);   // the code first
        var (coded, _) = await PostFormAsync(browser, signedIn.RequestMessage.RequestUri.PathAndQuery, "two-factor", ("Input.Code", Totp.Code(setup.SharedKey)));
        Assert.Equal("/change-password", coded.RequestMessage!.RequestUri!.AbsolutePath);   // then the new password
        var (changed, _) = await PostFormAsync(browser, coded.RequestMessage.RequestUri.PathAndQuery, "change-password",
            ("Input.NewPassword", "An0ther-Passw0rd"), ("Input.ConfirmPassword", "An0ther-Passw0rd"));
        Assert.DoesNotContain("/change-password", changed.RequestMessage!.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain("/two-factor", changed.RequestMessage.RequestUri.AbsolutePath, StringComparison.Ordinal);   // no second code
        await browser.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/v1/me/")).StatusCode);
    }
}
