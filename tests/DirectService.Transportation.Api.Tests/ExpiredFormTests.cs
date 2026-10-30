using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Platform.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 20 Oct 2026, item 1 (DT-221): an account form posted with a missing or outdated antiforgery token shows the same page
/// again with a fresh token and "This page expired. Please try again." (email/username kept, never the password) — never the framework's
/// raw text — and the persisted key ring keeps users signed in and open forms valid across a restart of the application.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed partial class ExpiredFormTests(ApiFactory f)
{
    private const string Expired = "This page expired. Please try again.";
    private const string RawText = "antiforgery";

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex Antiforgery();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>The text a reader sees (punctuation is wrapped in its own elements for right-to-left display).</summary>
    private static string Text(string html) => WebUtility.HtmlDecode(Spaces().Replace(Tag().Replace(html, string.Empty), " "));

    private static string Token(string page) => WebUtility.HtmlDecode(Antiforgery().Match(page).Groups[1].Value);

    private static FormUrlEncodedContent Form(string handler, string? token, params (string Name, string Value)[] fields)
    {
        var body = new List<KeyValuePair<string, string>> { new("_handler", handler) };
        if (token is not null) body.Add(new("__RequestVerificationToken", token));
        body.AddRange(fields.Select(x => new KeyValuePair<string, string>(x.Name, x.Value)));
        return new FormUrlEncodedContent(body);
    }

    /// <summary>Every account page with a form: (address, form name, fields of that form).</summary>
    public static TheoryData<string, string> Pages() => new()
    {
        { "/sign-in?lang=en", "sign-in" },
        { "/sign-up?lang=en", "sign-up" },
        { "/forgot-password?lang=en", "forgot" },
        { "/reset-password?lang=en&u=x&t=y", "reset" },
        { "/verify-email?lang=en&u=x&t=y", "resend" },
        { "/two-factor?lang=en", "two-factor" },
        { "/two-factor/setup?lang=en", "two-factor-setup" },
        { "/change-password?lang=en", "change-password" },
        { "/accept-invitation?lang=en&t=x", "accept-new" },
        { "/accept-staff-invitation?lang=en&t=x", "accept-staff" },
        { "/provider/setup?lang=en", "setup-code" },
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public async Task A_form_without_a_valid_token_shows_the_page_again_with_the_message_and_a_fresh_token(string url, string handler)
    {
        var browser = f.NewClient();
        await browser.GetAsync(url);   // the antiforgery cookie, as a browser that opened the page has it
        foreach (var token in new string?[] { null, "CfDJ8-outdated-token" })
        {
            var r = await browser.PostAsync(url, Form(handler, token, ("Input.Password", "secret-Passw0rd")));
            var page = await r.Content.ReadAsStringAsync();
            Assert.NotEqual(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, url + " " + r.StatusCode);   // provider setup closes (404) once staff exist
            Assert.DoesNotContain(RawText, page.Replace("__RequestVerificationToken", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
            if (r.StatusCode == HttpStatusCode.NotFound) continue;
            Assert.Contains(Expired, Text(page), StringComparison.Ordinal);
            Assert.DoesNotContain("secret-Passw0rd", page, StringComparison.Ordinal);
            if (page.Contains("<form", StringComparison.Ordinal)) Assert.NotEmpty(Token(page));   // a fresh token to try again with
        }
    }

    [Fact]
    public async Task The_page_shown_again_keeps_the_email_but_not_the_password_and_the_next_try_works()
    {
        var browser = f.NewClient();
        await browser.GetAsync("/sign-in?lang=en");
        var email = $"kept-{Guid.NewGuid():N}@example.test";
        var r = await browser.PostAsync("/sign-in?lang=en", Form("sign-in", "outdated", ("Input.Email", email), ("Input.Password", "typed-Passw0rd")));
        var page = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains($"value=\"{email}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("typed-Passw0rd", page, StringComparison.Ordinal);

        // Arabic page: the same message in Arabic.
        var ar = await (await browser.PostAsync("/sign-in?lang=ar", Form("sign-in", null, ("Input.Email", email)))).Content.ReadAsStringAsync();
        Assert.Contains("انتهت صلاحية هذه الصفحة. يُرجى المحاولة مرة أخرى.", Text(ar), StringComparison.Ordinal);

        // The fresh token of the page shown again is accepted: the next try is a normal sign-in attempt (wrong password message).
        var again = await browser.PostAsync("/sign-in?lang=en", Form("sign-in", Token(page), ("Input.Email", email), ("Input.Password", "wrong-Passw0rd")));
        var answer = WebUtility.HtmlDecode(await again.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Expired, Text(answer), StringComparison.Ordinal);
        Assert.Contains(ErrorCatalog.Format(ErrorCodes.SignInFailed, "en"), Text(answer), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cross_site_post_keeps_nothing_and_does_nothing()
    {
        var browser = f.NewClient();
        var email = $"xsite-{Guid.NewGuid():N}@example.test";
        using var req = new HttpRequestMessage(HttpMethod.Post, "/forgot-password?lang=en") { Content = Form("forgot", null, ("Input.Email", email)) };
        req.Headers.Add("Sec-Fetch-Site", "cross-site");
        var before = f.Emails.Count;
        var r = await browser.SendAsync(req);
        var page = await r.Content.ReadAsStringAsync();
        Assert.Contains(Expired, Text(page), StringComparison.Ordinal);
        Assert.DoesNotContain(email, page, StringComparison.Ordinal);
        Assert.Equal(before, f.Emails.Count);   // the handler never ran
    }

    [Fact]
    public async Task Signing_in_in_another_tab_makes_the_open_form_expire_politely()
    {
        var userName = await AccountantAsync();
        var browser = f.NewClient();
        var form = await browser.GetStringAsync("/forgot-password?lang=en");   // tab 1: anonymous form
        await browser.UseAntiforgeryAsync();                                    // tab 2: signs in (same cookies)
        await (await browser.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).EnsureOkAsync();

        var email = $"tab-{Guid.NewGuid():N}@example.test";
        var r = await browser.PostAsync("/forgot-password?lang=en", Form("forgot", Token(form), ("Input.Email", email)));
        var page = await r.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains(Expired, Text(page), StringComparison.Ordinal);
        Assert.Contains($"value=\"{email}\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restart_of_the_application_keeps_users_signed_in_and_open_forms_valid()
    {
        var userName = await AccountantAsync();
        var jar = new System.Net.CookieContainer();
        var before = Client(f, jar);
        await before.UseAntiforgeryAsync();
        await (await before.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(null, ApiHelpers.Password, userName))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.OK, (await before.GetAsync("/api/v1/me/")).StatusCode);
        var anonymousJar = new System.Net.CookieContainer();
        var form = await Client(f, anonymousJar).GetStringAsync("/forgot-password?lang=en");

        // A new host process (same configuration, same key folder): the key ring is read back from disk.
        using var restarted = f.WithWebHostBuilder(_ => { });
        var after = Client(restarted, jar);
        Assert.Equal(HttpStatusCode.OK, (await after.GetAsync("/api/v1/me/")).StatusCode);
        var posted = await Client(restarted, anonymousJar).PostAsync("/forgot-password?lang=en",
            Form("forgot", Token(form), ("Input.Email", $"nobody-{Guid.NewGuid():N}@example.test")));
        Assert.DoesNotContain(Expired, Text(await posted.Content.ReadAsStringAsync()), StringComparison.Ordinal);

        // Control: a host with another key ring (keys not persisted) would have signed the user out.
        var otherKeys = Path.Combine(Path.GetTempPath(), "dst-api-tests-otherkeys-" + Guid.NewGuid().ToString("N"));
        using var newKeys = f.WithWebHostBuilder(b => b.UseSetting("DataProtection:KeysPath", otherKeys));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client(newKeys, jar).GetAsync("/api/v1/me/")).StatusCode);
        try { Directory.Delete(otherKeys, recursive: true); } catch (IOException) { }
    }

    /// <summary>A user without 2FA (accountant created by a new owner): the password alone gives a full session.</summary>
    private async Task<string> AccountantAsync()
    {
        var owner = await f.SignUpOwnerAsync();
        var roles = (await owner.Client.GetFromJsonAsync<List<RoleResponse>>($"/api/v1/companies/{owner.CompanyId}/roles"))!;
        var userName = "xf." + Guid.NewGuid().ToString("N")[..10];
        await (await owner.Client.PostAsJsonAsync($"/api/v1/companies/{owner.CompanyId}/users",
            new CreateUserRequest("Form User", userName, null, ApiHelpers.Password, roles.Single(x => x.Name == PresetRoles.Accountant).Id, false))).EnsureOkAsync();
        return userName;
    }

    private static HttpClient Client(WebApplicationFactory<Program> host, System.Net.CookieContainer jar)
    {
        var c = host.CreateDefaultClient(new Uri("https://localhost"), new CookieContainerHandler(jar));
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        return c;
    }
}
