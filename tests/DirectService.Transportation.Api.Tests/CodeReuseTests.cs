using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Identity;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner decision 19 Oct 2026 (DT-212, the two open points of the security review): an authenticator code works once — a code seen over a
/// shoulder or on a shared screen cannot be used again inside its 30-second window — and one recovery code cannot be spent twice by two
/// requests at the same moment.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CodeReuseTests(ApiFactory f)
{
    /// <summary>A client after the password step, waiting for the code.</summary>
    private async Task<HttpClient> PasswordStepAsync(SignedInUser o)
    {
        var c = f.NewClient();
        await c.UseAntiforgeryAsync();
        var r = await (await c.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(o.Email, o.Password))).Content.ReadFromJsonAsync<SignInResponse>();
        Assert.Equal("twoFactor", r!.Next);
        return c;
    }

    [Fact]
    public async Task An_authenticator_code_works_once()
    {
        var o = await f.SignUpOwnerAsync();
        var code = Totp.Code(o.AuthenticatorKey);   // a fresh step
        var first = await PasswordStepAsync(o);
        await (await first.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(code, null))).EnsureOkAsync();

        // The same code again — still inside its window — is refused, at sign-in and for the profile's checks alike.
        var second = await PasswordStepAsync(o);
        var replay = await second.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(code, null));
        Assert.Equal((HttpStatusCode.Unauthorized, "auth.twoFactorInvalid"), (replay.StatusCode, await replay.ErrorCodeAsync()));
        Assert.Equal("auth.stepUpFailed", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/recovery-codes", new TwoFactorStepUpRequest(o.Password, code, null))).ErrorCodeAsync());

        // The app's next code works.
        await (await second.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(o.AuthenticatorKey), null))).EnsureOkAsync();
    }

    [Fact]
    public async Task An_older_code_is_refused_once_a_newer_one_was_used()
    {
        var o = await f.SignUpOwnerAsync();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var older = Totp.CodeAt(o.AuthenticatorKey, now - 1);   // still inside the ±1 window
        var newer = Totp.Code(o.AuthenticatorKey);              // now or now + 1 (later than the sign-up code)
        var a = await PasswordStepAsync(o);
        await (await a.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(newer, null))).EnsureOkAsync();
        var b = await PasswordStepAsync(o);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(older, null))).StatusCode);
    }

    [Fact]
    public async Task The_same_authenticator_code_in_two_simultaneous_requests_succeeds_once()
    {
        var o = await f.SignUpOwnerAsync();
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PasswordStepAsync(o)));
        var code = Totp.Code(o.AuthenticatorKey);
        var answers = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(code, null))));
        Assert.Equal(1, answers.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(3, answers.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task One_recovery_code_in_two_simultaneous_requests_is_spent_once()
    {
        var o = await f.SignUpOwnerAsync();
        var clients = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PasswordStepAsync(o)));
        var recovery = o.RecoveryCodes[0];
        var answers = await Task.WhenAll(clients.Select(c => c.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, recovery))));
        Assert.Equal(1, answers.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(9, (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.RecoveryCodesLeft);
        // Spent: refused afterwards too.
        var later = await PasswordStepAsync(o);
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, recovery))).StatusCode);
    }
}
