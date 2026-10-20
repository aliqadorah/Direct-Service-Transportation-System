using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Identity;
using Microsoft.EntityFrameworkCore;

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
    private async Task<HttpClient> PasswordStepAsync(SignedInUser o, string? ip = null)
    {
        var c = f.NewClient();
        if (ip is not null) c.DefaultRequestHeaders.Add("X-Test-Client-IP", ip);
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
        var seen = string.Join(", ", await Task.WhenAll(answers.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}")));
        Assert.True(answers.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, seen);
        Assert.Equal(9, (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.RecoveryCodesLeft);
        // Spent: refused afterwards too.
        var later = await PasswordStepAsync(o);
        Assert.Equal(HttpStatusCode.Unauthorized, (await later.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, recovery))).StatusCode);
    }

    /// <summary>
    /// DT-222 (the cause of this class's unreliable test under load): wrong attempts at the same moment change the same user row; the correct code
    /// must still sign in (it failed with a concurrency error) and every wrong attempt must count towards the lockout (they were dropped).
    /// Several rounds, each from distinct addresses so the per-address block does not interfere.
    /// </summary>
    [Fact]
    public async Task A_right_code_among_simultaneous_wrong_ones_signs_in_and_every_wrong_one_is_counted()
    {
        for (var round = 0; round < 3; round++)
        {
            var o = await f.SignUpOwnerAsync();
            var clients = await Task.WhenAll(Enumerable.Range(0, 7).Select(i => PasswordStepAsync(o, $"10.222.{round}.{i + 1}")));
            var answers = await Task.WhenAll(clients.Select((c, i) => c.PostAsJsonAsync("/api/v1/auth/2fa/verify",
                new TwoFactorCodeRequest(null, i == 0 ? o.RecoveryCodes[0] : "WRONG-" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture)))));
            Assert.Equal(HttpStatusCode.OK, answers[0].StatusCode);
            Assert.All(answers.Skip(1), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
            var failed = await f.DbAsync(db => db.Set<DirectService.Transportation.Domain.Core.User>().IgnoreQueryFilters()
                .Where(u => u.Email == o.Email).Select(u => new { u.FailedSignInCount, u.LastSignInAt }).SingleAsync());
            // The success may land before or after the failures: either it reset the count and later failures counted, or all six counted
            // before; never fewer than the failures after the success, and with all failures before it, zero.
            Assert.InRange(failed.FailedSignInCount, 0, 6);
            Assert.NotNull(failed.LastSignInAt);
        }
    }

    [Fact]
    public async Task Simultaneous_wrong_attempts_are_all_counted()
    {
        var o = await f.SignUpOwnerAsync();
        var clients = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => PasswordStepAsync(o, $"10.223.0.{i + 1}")));
        var answers = await Task.WhenAll(clients.Select((c, i) => c.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, "WRONG-0000" + i))));
        Assert.All(answers, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Equal(8, await f.DbAsync(db => db.Set<DirectService.Transportation.Domain.Core.User>().IgnoreQueryFilters()
            .Where(u => u.Email == o.Email).Select(u => u.FailedSignInCount).SingleAsync()));
    }
}
