using System.Security.Claims;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Domain.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

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

    /// <summary>Checks the password and issues the partial sign-in; returns the next step.</summary>
    public async Task<SignInNextStep> PasswordAsync(HttpContext http, string email, string password, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        var (user, next) = await auth.CheckPasswordAsync(email, password, ct).ConfigureAwait(false);
        var stage = next == SignInNextStep.TwoFactorSetup ? StageSetup : StageVerify;
        await http.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
        await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, stage, user.SecurityStamp)).ConfigureAwait(false);
        return next;
    }

    /// <summary>The stage of the current partial sign-in, or null.</summary>
    public static async Task<string?> StageAsync(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var result = await http.AuthenticateAsync(AuthSchemes.Mfa).ConfigureAwait(false);
        return result.Succeeded ? result.Principal?.FindFirstValue(AppClaims.Stage) : null;
    }

    /// <summary>Authenticator key and URI for the set-up stage (creating the key changes the security stamp: the partial cookie is re-issued).</summary>
    public async Task<TwoFactorSetupResponse> SetupAsync(HttpContext http)
    {
        var user = await MfaUserAsync(http, StageSetup).ConfigureAwait(false);
        var setup = await auth.GetAuthenticatorSetupAsync(user).ConfigureAwait(false);
        await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, StageSetup, user.SecurityStamp)).ConfigureAwait(false);
        return new TwoFactorSetupResponse(setup.SharedKey, setup.AuthenticatorUri);
    }

    /// <summary>Confirms the authenticator with a code, signs in, and returns the one-time recovery codes.</summary>
    public async Task<RecoveryCodesResponse> CompleteSetupAsync(HttpContext http, string? code, CancellationToken ct)
    {
        var user = await MfaUserAsync(http, StageSetup).ConfigureAwait(false);
        var codes = await auth.CompleteAuthenticatorSetupAsync(user, code ?? string.Empty, ct).ConfigureAwait(false);
        var profile = await CompleteAsync(http, user, ct).ConfigureAwait(false);
        return new RecoveryCodesResponse(codes, profile);
    }

    /// <summary>Verifies the authenticator code or a recovery code and signs in.</summary>
    public async Task<MeResponse> VerifyAsync(HttpContext http, string? code, string? recoveryCode, CancellationToken ct)
    {
        var user = await MfaUserAsync(http, StageVerify).ConfigureAwait(false);
        await auth.VerifySecondFactorAsync(user, code, recoveryCode, ct).ConfigureAwait(false);
        return await CompleteAsync(http, user, ct).ConfigureAwait(false);
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

    /// <summary>The user of the partial (password-verified) sign-in, at the expected stage, with an unchanged security stamp.</summary>
    private async Task<User> MfaUserAsync(HttpContext http, string stage)
    {
        ArgumentNullException.ThrowIfNull(http);
        var result = await http.AuthenticateAsync(AuthSchemes.Mfa).ConfigureAwait(false);
        var p = result.Principal;
        if (!result.Succeeded || p is null || p.FindFirstValue(AppClaims.Stage) != stage)
            throw new AppException(ErrorCodes.TwoFactorRequired, StatusCodes.Status401Unauthorized);
        var user = await users.FindByIdAsync(p.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty).ConfigureAwait(false);
        if (user is null || user.Status != "Active" || user.SecurityStamp != p.FindFirstValue(AppClaims.SecurityStamp))
            throw new AppException(ErrorCodes.TwoFactorRequired, StatusCodes.Status401Unauthorized);
        return user;
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
