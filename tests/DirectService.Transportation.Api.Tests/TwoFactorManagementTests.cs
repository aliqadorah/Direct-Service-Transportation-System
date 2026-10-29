using System.Net;
using System.Net.Http.Json;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Api.Tests;

/// <summary>
/// Owner testing 19 Oct 2026 (DT-210): two-step verification managed from the profile — turn on, turn off, change the authenticator app,
/// new recovery codes. The three changes need the current password AND a current code (or a recovery code); turning off is refused while
/// the organisation requires 2FA and always for platform staff; every change is audited and announced by email to a verified address.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TwoFactorManagementTests(ApiFactory f)
{
    private static TwoFactorStepUpRequest Proof(SignedInUser o, string? password = null, string? code = null) =>
        new(password ?? o.Password, code ?? Totp.Code(o.AuthenticatorKey), null);

    private Task<int> AuditAsync(string email, string action) => f.DbAsync(db => db.Set<AuditLog>().IgnoreQueryFilters()
        .Join(db.Set<User>().IgnoreQueryFilters(), a => a.EntityId, u => u.Id, (a, u) => new { a, u })
        .CountAsync(x => x.a.EntityType == "CORE.User" && x.a.Action == action && x.u.Email == email));

    [Fact]
    public async Task Status_shows_on_off_required_and_codes_left()
    {
        var o = await f.SignUpOwnerAsync();
        var s = (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!;
        Assert.Equal((true, false, false, 10), (s.Enabled, s.Required, s.RequiredForStaff, s.RecoveryCodesLeft));
        Assert.NotNull(s.EnabledAt);
    }

    [Fact]
    public async Task Turning_off_needs_password_and_code_then_ends_other_sessions_audits_and_emails()
    {
        var o = await f.SignUpOwnerAsync();
        var other = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);

        Assert.Equal("currentPassword", await FieldAsync(await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", new TwoFactorStepUpRequest(null, Totp.Code(o.AuthenticatorKey), null))));
        // A wrong password and a wrong code get the same answer, without a field: nothing tells which one was wrong (security review).
        var wrongPassword = await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o, password: "Wrong-passw0rd"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "auth.stepUpFailed"), (wrongPassword.StatusCode, await wrongPassword.ErrorCodeAsync()));
        var noCode = await ProblemAsync(await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", new TwoFactorStepUpRequest(o.Password, null, null)));
        Assert.Equal(("code", "auth.twoFactorStepUpCode"), noCode);
        var wrongCode = await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o, code: "000000"));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "auth.stepUpFailed"), (wrongCode.StatusCode, await wrongCode.ErrorCodeAsync()));   // a refusal (422), never 401 — that would end the session
        Assert.Equal(HttpStatusCode.OK, (await o.Client.GetAsync("/api/v1/me/")).StatusCode);   // the failed attempts did not end the session

        var before = f.Emails.Count;
        var me = await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o))).Content.ReadFromJsonAsync<MeResponse>();
        Assert.False(me!.TwoFactorEnabled);
        await o.Client.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await o.Client.GetAsync("/api/v1/me/")).StatusCode);   // this session stays signed in
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.GetAsync("/api/v1/me/")).StatusCode);   // every other session ended
        Assert.Equal(1, await AuditAsync(o.Email, "TwoFactorDisabled"));
        Assert.Contains("turned off", f.Emails.Last(o.Email, "TwoFactorChanged").TextBody, StringComparison.Ordinal);
        Assert.True(f.Emails.Count > before);

        // Off: the old key and recovery codes are gone; sign-in no longer asks for a code (the Owner sees the offer).
        var s = (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!;
        Assert.Equal((false, 0), (s.Enabled, s.RecoveryCodesLeft));
        var fresh = f.NewClient();
        await fresh.UseAntiforgeryAsync();
        var signIn = await (await fresh.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(o.Email, o.Password))).Content.ReadFromJsonAsync<SignInResponse>();
        Assert.Equal("twoFactorOffer", signIn!.Next);

        // Turning it on again from the profile is audited and announced too.
        var setup = await o.Client.GetFromJsonAsync<TwoFactorSetupResponse>("/api/v1/me/2fa/setup");
        await o.Client.UseAntiforgeryAsync();
        await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/setup", new TwoFactorCodeRequest(Totp.Code(setup!.SharedKey), null))).EnsureOkAsync();
        Assert.Contains("turned on", f.Emails.Last(o.Email, "TwoFactorChanged").TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Turning_off_is_refused_when_the_organisation_requires_2fa()
    {
        var o = await f.SignUpOwnerAsync();
        var tenant = (await o.Client.GetFromJsonAsync<TenantResponse>("/api/v1/tenant"))!;
        await (await o.Client.PutAsJsonAsync("/api/v1/tenant/security",
            new TenantSecurityRequest(tenant.PasswordMinLength, tenant.PasswordRequireLetter, tenant.PasswordRequireDigit, tenant.SessionIdleTimeoutMinutes, tenant.RowVersion, true))).EnsureOkAsync();
        var s = (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!;
        Assert.Equal((true, false), (s.Required, s.RequiredForStaff));
        var refused = await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o));
        Assert.Equal("auth.twoFactorRequiredByPolicy", await refused.ErrorCodeAsync());
        Assert.True((await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.Enabled);
        Assert.Equal(0, await AuditAsync(o.Email, "TwoFactorDisabled"));
        // Changing the app and new recovery codes stay allowed.
        await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/recovery-codes", Proof(o))).EnsureOkAsync();
    }

    [Fact]
    public async Task Platform_staff_can_never_turn_it_off()
    {
        var staff = await PlatformTests.StaffAsync(f);
        var s = (await staff.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!;
        Assert.Equal((true, true, true), (s.Enabled, s.Required, s.RequiredForStaff));
        Assert.Equal("auth.twoFactorRequiredForStaff", await (await staff.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(staff))).ErrorCodeAsync());
        Assert.True((await staff.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.Enabled);
    }

    [Fact]
    public async Task Changing_the_app_keeps_the_old_key_until_the_new_one_is_confirmed()
    {
        var o = await f.SignUpOwnerAsync();
        Assert.Equal("auth.stepUpFailed", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change", Proof(o, password: "Wrong-passw0rd"))).ErrorCodeAsync());
        // Nothing pending yet: confirming is refused.
        Assert.Equal("auth.twoFactorChangeExpired", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest("123456", null))).ErrorCodeAsync());

        var setup = (await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change", Proof(o))).Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!;
        Assert.NotEqual(o.AuthenticatorKey, setup.SharedKey);
        Assert.NotNull(setup.QrSvg);
        // Not confirmed yet: the old app still signs in.
        var still = f.NewClient();
        await still.UseAntiforgeryAsync();
        await (await still.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(o.Email, o.Password))).EnsureOkAsync();
        await (await still.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(o.AuthenticatorKey), null))).EnsureOkAsync();

        var wrong = await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest(Totp.Code(o.AuthenticatorKey), null));
        Assert.Equal("auth.twoFactorInvalid", await wrong.ErrorCodeAsync());   // a code of the OLD app does not confirm the new one
        await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null))).EnsureOkAsync();
        Assert.Equal(1, await AuditAsync(o.Email, "TwoFactorAppChanged"));
        Assert.Contains("authenticator app was changed", f.Emails.Last(o.Email, "TwoFactorChanged").TextBody, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, (await still.GetAsync("/api/v1/me/")).StatusCode);   // other sessions ended
        await o.Client.UseAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.OK, (await o.Client.GetAsync("/api/v1/me/")).StatusCode);

        // Now only the new app works.
        var next = f.NewClient();
        await next.UseAntiforgeryAsync();
        await (await next.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(o.Email, o.Password))).EnsureOkAsync();
        var oldApp = await next.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(o.AuthenticatorKey), null));
        Assert.Equal((HttpStatusCode.Unauthorized, "auth.twoFactorInvalid"), (oldApp.StatusCode, await oldApp.ErrorCodeAsync()));
        await (await next.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null))).EnsureOkAsync();
    }

    /// <summary>
    /// Security review 19 Oct 2026: a pending new key can only be confirmed by the session that started the change (a stolen cookie of
    /// another session cannot guess it), and five wrong codes drop it; wrong codes count towards the lockout.
    /// </summary>
    [Fact]
    public async Task A_pending_app_change_belongs_to_its_session_and_allows_five_wrong_codes()
    {
        var o = await f.SignUpOwnerAsync();
        var other = await f.SignInAsync(o.Email, o.Password, o.AuthenticatorKey);
        var setup = (await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change", Proof(o))).Content.ReadFromJsonAsync<TwoFactorSetupResponse>())!;
        Assert.Equal("auth.twoFactorChangeExpired",
            await (await other.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null))).ErrorCodeAsync());

        for (var i = 0; i < 5; i++)
            Assert.Equal("auth.twoFactorInvalid", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest("000000", null))).ErrorCodeAsync());
        // Dropped after five wrong codes: even the right code needs a new start.
        Assert.Equal("auth.twoFactorChangeExpired",
            await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/change/confirm", new TwoFactorCodeRequest(Totp.Code(setup.SharedKey), null))).ErrorCodeAsync());
        Assert.Equal(0, await AuditAsync(o.Email, "TwoFactorAppChanged"));
        Assert.True(await AuditAsync(o.Email, "SignInFailed") >= 5);
    }

    [Fact]
    public async Task New_recovery_codes_replace_the_old_ones_and_a_recovery_code_can_prove_the_change()
    {
        var o = await f.SignUpOwnerAsync();
        var oldCode = o.RecoveryCodes[0];
        // A recovery code instead of the app code (lost phone) — it is used up.
        var r = await o.Client.PostAsJsonAsync("/api/v1/me/2fa/recovery-codes", new TwoFactorStepUpRequest(o.Password, null, oldCode));
        var fresh = (await r.Content.ReadFromJsonAsync<RecoveryCodesResponse>())!.RecoveryCodes;
        Assert.Equal(10, fresh.Count);
        Assert.Equal(1, await AuditAsync(o.Email, "RecoveryCodesRenewed"));
        Assert.Contains("New recovery codes", f.Emails.Last(o.Email, "TwoFactorChanged").TextBody, StringComparison.Ordinal);

        // The old codes no longer work; the new ones do.
        Assert.Equal("auth.stepUpFailed", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/recovery-codes", new TwoFactorStepUpRequest(o.Password, null, o.RecoveryCodes[1]))).ErrorCodeAsync());
        var signIn = f.NewClient();
        await signIn.UseAntiforgeryAsync();
        await (await signIn.PostAsJsonAsync("/api/v1/auth/sign-in", new SignInRequest(o.Email, o.Password))).EnsureOkAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await signIn.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, o.RecoveryCodes[2]))).StatusCode);
        await (await signIn.PostAsJsonAsync("/api/v1/auth/2fa/verify", new TwoFactorCodeRequest(null, fresh[0]))).EnsureOkAsync();
        Assert.Equal(9, (await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.RecoveryCodesLeft);
    }

    [Fact]
    public async Task Failed_step_up_attempts_count_towards_the_lockout()
    {
        var o = await f.SignUpOwnerAsync();
        for (var i = 0; i < 5; i++) await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o, password: "Wrong-passw0rd"));
        // Blocked for this address now: even the right proof gets the ordinary answer, and 2FA stays on.
        Assert.Equal("auth.stepUpFailed", await (await o.Client.PostAsJsonAsync("/api/v1/me/2fa/disable", Proof(o))).ErrorCodeAsync());
        Assert.True((await o.Client.GetFromJsonAsync<TwoFactorStatusResponse>("/api/v1/me/2fa"))!.Enabled);
        Assert.True(await AuditAsync(o.Email, "SignInFailed") >= 5);
        Assert.Equal(1, await AuditAsync(o.Email, "SignInLocked"));
    }

    /// <summary>The field and code of a 400 answer, read once.</summary>
    private static async Task<(string? Field, string? Code)> ProblemAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array && errors.GetArrayLength() > 0)
            return (errors[0].GetProperty("field").GetString(), errors[0].GetProperty("code").GetString());
        return (body.TryGetProperty("field", out var field) ? field.GetString() : null, body.GetProperty("code").GetString());
    }

    private static async Task<string?> FieldAsync(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        if (body.TryGetProperty("errors", out var errors) && errors.ValueKind == System.Text.Json.JsonValueKind.Array && errors.GetArrayLength() > 0)
            return errors[0].GetProperty("field").GetString();
        return body.TryGetProperty("field", out var field) ? field.GetString() : null;
    }
}
