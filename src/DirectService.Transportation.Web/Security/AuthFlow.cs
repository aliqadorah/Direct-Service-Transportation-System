using System.Security.Claims;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using DirectService.Platform.Application.Identity;

namespace DirectService.Transportation.Web.Security;

/// <summary>
/// The sign-in sequence (FR-175, FR-177, ADR-05), shared by the JSON endpoints (<c>/api/v1/auth</c>) and the server-rendered account pages
/// (SCR-01, SCR-05): password → partial sign-in cookie at a stage ("setup" or "verify") → authenticator set-up or code / recovery code →
/// server session + application cookie. One implementation, so both doors behave and are protected the same way.
/// </summary>
public sealed class AuthFlow(AuthService auth, UserManager<User> users, SessionService sessions, MeService me, RequestContext ctx)
{
    public const string StageVerify = "verify";
    public const string StageSetup = "setup";
    public const string StagePassword = "password";
    public const string StageOffer = "offer";
    /// <summary>DT-205: the forced password change of a user with 2FA comes after the code; the sign-in completes after the change.</summary>
    public const string StagePasswordAfterCode = "password-2fa";

    /// <summary>
    /// Checks the password (<paramref name="login"/> = email or username) and issues the partial sign-in for the next step; with no
    /// next step (revision 6: no 2FA required, no password change) the user is signed in at once.
    /// </summary>
    public async Task<SignInNextStep> PasswordAsync(HttpContext http, string login, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var (user, next) = await auth.CheckPasswordAsync(login, password, ct).ConfigureAwait(false);
        await http.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
        await ContinueAsync(http, user, next, ct).ConfigureAwait(false);
        return next;
    }

    /// <summary>The forced password change (stage "password"); returns the step after it.</summary>
    public async Task<SignInNextStep> ChangePasswordAsync(HttpContext http, string? newPassword, CancellationToken ct)
    {
        var (user, stage) = await MfaUserAsync(http, StagePassword, StagePasswordAfterCode).ConfigureAwait(false);
        await auth.ChangeRequiredPasswordAsync(user, newPassword, ct).ConfigureAwait(false);
        if (stage == StagePasswordAfterCode)
        {
            await CompleteAsync(http, user, ct).ConfigureAwait(false);   // the code was checked before the change
            return SignInNextStep.None;
        }
        var next = await auth.SecondStepAsync(user, ct).ConfigureAwait(false);
        await ContinueAsync(http, user, next, ct).ConfigureAwait(false);
        return next;
    }

    /// <summary>Partial cookie at the stage of <paramref name="next"/>, or the full sign-in when nothing is left.</summary>
    private async Task ContinueAsync(HttpContext http, User user, SignInNextStep next, CancellationToken ct)
    {
        if (next == SignInNextStep.None)
        {
            await auth.CompleteWithoutSecondFactorAsync(user, skippedOffer: false, ct).ConfigureAwait(false);
            await CompleteAsync(http, user, ct).ConfigureAwait(false);
            return;
        }
        var stage = next switch
        {
            SignInNextStep.TwoFactorSetup => StageSetup, SignInNextStep.TwoFactorOffer => StageOffer, SignInNextStep.ChangePassword => StagePassword, _ => StageVerify,
        };
        await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, stage, user.SecurityStamp)).ConfigureAwait(false);
    }

    /// <summary>The profile after a sign-in that needed no second step (JSON door).</summary>
    public Task<MeResponse> ProfileAsync(CancellationToken ct) => me.GetAsync(ct);

    /// <summary>
    /// Re-issues the application cookie of the current session after the user's own security stamp changed (2FA turned on, password
    /// changed from the profile), so this session stays signed in while every other session ends.
    /// </summary>
    public async Task RefreshAppCookieAsync(HttpContext http, User user)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(user);
        var token = http.User.FindFirstValue(AppClaims.SessionToken) ?? throw new AppException(ErrorCodes.Forbidden, StatusCodes.Status403Forbidden);
        await http.SignInAsync(AuthSchemes.App, AuthSetup.AppPrincipal(user.PublicId, user.FullName, token, user.SecurityStamp)).ConfigureAwait(false);
    }

    /// <summary>The stage of the current partial sign-in, or null.</summary>
    public static async Task<string?> StageAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var result = await http.AuthenticateAsync(AuthSchemes.Mfa).ConfigureAwait(false);
        return result.Succeeded ? result.Principal?.FindFirstValue(AppClaims.Stage) : null;
    }

    /// <summary>
    /// Authenticator key and URI for the set-up stage, required or offered (creating the key changes the security stamp: the partial cookie
    /// is re-issued at the same stage).
    /// </summary>
    public async Task<TwoFactorSetupResponse> SetupAsync(HttpContext http)
    {
        var (user, stage) = await MfaUserAsync(http, StageSetup, StageOffer).ConfigureAwait(false);
        var setup = await auth.GetAuthenticatorSetupAsync(user).ConfigureAwait(false);
        await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, stage, user.SecurityStamp)).ConfigureAwait(false);
        return new TwoFactorSetupResponse(setup.SharedKey, setup.AuthenticatorUri);
    }

    /// <summary>Confirms the authenticator with a code, signs in, and returns the one-time recovery codes.</summary>
    public async Task<RecoveryCodesResponse> CompleteSetupAsync(HttpContext http, string? code, CancellationToken ct)
    {
        var (user, _) = await MfaUserAsync(http, StageSetup, StageOffer).ConfigureAwait(false);
        var codes = await auth.CompleteAuthenticatorSetupAsync(user, code ?? string.Empty, ct).ConfigureAwait(false);
        var profile = await CompleteAsync(http, user, ct).ConfigureAwait(false);
        return new RecoveryCodesResponse(codes, profile);
    }

    /// <summary>"Skip for now" on the offered set-up (revision 6): signs in without 2FA — refused when 2FA is required by then.</summary>
    public async Task<MeResponse> SkipSetupAsync(HttpContext http, CancellationToken ct)
    {
        var (user, _) = await MfaUserAsync(http, StageOffer).ConfigureAwait(false);
        await auth.CompleteWithoutSecondFactorAsync(user, skippedOffer: true, ct).ConfigureAwait(false);
        return await CompleteAsync(http, user, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Verifies the authenticator code or a recovery code and signs in — or, when an administrator asked for a new password, continues to
    /// the forced change (owner decision 18 Oct 2026, DT-205: the code first, then the new password).
    /// </summary>
    public async Task<SignInNextStep> VerifyAsync(HttpContext http, string? code, string? recoveryCode, CancellationToken ct)
    {
        var (user, _) = await MfaUserAsync(http, StageVerify).ConfigureAwait(false);
        await auth.VerifySecondFactorAsync(user, code, recoveryCode, ct).ConfigureAwait(false);
        if (user.MustChangePassword)
        {
            await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, StagePasswordAfterCode, user.SecurityStamp)).ConfigureAwait(false);
            return SignInNextStep.ChangePassword;
        }
        await CompleteAsync(http, user, ct).ConfigureAwait(false);
        return SignInNextStep.None;
    }

    /// <summary>Revokes the server session and removes both cookies.</summary>
    public async Task SignOutAsync(HttpContext http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (http.Items[typeof(SessionPrincipal)] is SessionPrincipal p)
            await sessions.RevokeAsync(p.SessionId, "Signed out", ct).ConfigureAwait(false);
        await http.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
        await http.SignOutAsync(AuthSchemes.Mfa).ConfigureAwait(false);
    }

    /// <summary>The user of the partial (password-verified) sign-in, at one of the expected stages, with an unchanged security stamp.</summary>
    private async Task<(User User, string Stage)> MfaUserAsync(HttpContext http, params string[] stages)
    {
        ArgumentNullException.ThrowIfNull(http);
        var result = await http.AuthenticateAsync(AuthSchemes.Mfa).ConfigureAwait(false);
        var p = result.Principal;
        var stage = p?.FindFirstValue(AppClaims.Stage);
        if (!result.Succeeded || p is null || stage is null || !stages.Contains(stage, StringComparer.Ordinal))
            throw new AppException(ErrorCodes.TwoFactorRequired, StatusCodes.Status401Unauthorized);
        var user = await users.FindByIdAsync(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty).ConfigureAwait(false);
        if (user is null || user.Status != "Active" || user.SecurityStamp != p.FindFirstValue(AppClaims.SecurityStamp))
            throw new AppException(ErrorCodes.TwoFactorRequired, StatusCodes.Status401Unauthorized);
        return (user, stage);
    }

    /// <summary>Creates the server-side session, issues the application cookie and returns the profile.</summary>
    private async Task<MeResponse> CompleteAsync(HttpContext http, User user, CancellationToken ct)
    {
        var (token, sessionId) = await sessions.CreateAsync(user, http.Connection.RemoteIpAddress?.ToString(),
            http.Request.Headers.UserAgent.ToString(), ct).ConfigureAwait(false);
        await http.SignOutAsync(AuthSchemes.Mfa).ConfigureAwait(false);
        await http.SignInAsync(AuthSchemes.App, AuthSetup.AppPrincipal(user.PublicId, user.FullName, token, user.SecurityStamp)).ConfigureAwait(false);
        ctx.UserId = user.Id;
        ctx.UserPublicId = user.PublicId;
        ctx.TenantId = user.TenantId;
        ctx.SessionId = sessionId;
        ctx.Language = user.LanguageCode;
        return await me.GetAsync(ct).ConfigureAwait(false);
    }
}
