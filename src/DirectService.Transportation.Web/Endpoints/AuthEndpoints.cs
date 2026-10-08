using System.Security.Claims;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Web.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Anonymous account endpoints (rate-limited, SEC-14): sign-up, email verification, sign-in with mandatory
/// two-factor authentication (set-up at first sign-in, then code or recovery code), sign-out, password
/// reset, invitations. After a successful sign-in the client must fetch a new antiforgery token.
/// </summary>
internal static class AuthEndpoints
{
    private const string StageVerify = "verify";
    private const string StageSetup = "setup";

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Authentication").AllowAnonymous().RequireRateLimiting(RateLimits.Auth);

        auth.MapGet("/antiforgery", (HttpContext http, IAntiforgery af) =>
        {
            var t = af.GetAndStoreTokens(http);
            return TypedResults.Ok(new AntiforgeryResponse(t.HeaderName!, t.RequestToken!));
        });

        auth.MapPost("/sign-up", async (SignUpRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.SignUpAsync(r, ct).ConfigureAwait(false);
            return TypedResults.Accepted((string?)null);
        });

        auth.MapPost("/resend-verification", async (EmailRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.ResendVerificationAsync(r.Email, ct).ConfigureAwait(false);
            return TypedResults.Accepted((string?)null);
        });

        auth.MapPost("/verify-email", async (LinkTokenRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.VerifyEmailAsync(r.User, r.Token, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });

        auth.MapPost("/sign-in", async (SignInRequest r, HttpContext http, AuthService authService, CancellationToken ct) =>
        {
            var (user, next) = await authService.CheckPasswordAsync(r.Email, r.Password, ct).ConfigureAwait(false);
            var stage = next == SignInNextStep.TwoFactorSetup ? StageSetup : StageVerify;
            await http.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
            await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, stage, user.SecurityStamp)).ConfigureAwait(false);
            return TypedResults.Ok(new SignInResponse(next == SignInNextStep.TwoFactorSetup ? "twoFactorSetup" : "twoFactor"));
        });

        auth.MapGet("/2fa/setup", async (HttpContext http, AuthService authService, UserManager<User> users) =>
        {
            var user = await MfaUserAsync(http, users, StageSetup).ConfigureAwait(false);
            var setup = await authService.GetAuthenticatorSetupAsync(user).ConfigureAwait(false);
            // Creating the key changes the security stamp: re-issue the partial sign-in with the new stamp.
            await http.SignInAsync(AuthSchemes.Mfa, AuthSetup.MfaPrincipal(user.PublicId, StageSetup, user.SecurityStamp)).ConfigureAwait(false);
            return TypedResults.Ok(new TwoFactorSetupResponse(setup.SharedKey, setup.AuthenticatorUri));
        });

        auth.MapPost("/2fa/setup", async (TwoFactorCodeRequest r, HttpContext http, AuthService authService, UserManager<User> users,
            SessionService sessions, MeService me, RequestContext ctx, CancellationToken ct) =>
        {
            var user = await MfaUserAsync(http, users, StageSetup).ConfigureAwait(false);
            var codes = await authService.CompleteAuthenticatorSetupAsync(user, r.Code ?? string.Empty, ct).ConfigureAwait(false);
            var profile = await CompleteSignInAsync(http, user, sessions, me, ctx, ct).ConfigureAwait(false);
            return TypedResults.Ok(new RecoveryCodesResponse(codes, profile));
        });

        auth.MapPost("/2fa/verify", async (TwoFactorCodeRequest r, HttpContext http, AuthService authService, UserManager<User> users,
            SessionService sessions, MeService me, RequestContext ctx, CancellationToken ct) =>
        {
            var user = await MfaUserAsync(http, users, StageVerify).ConfigureAwait(false);
            await authService.VerifySecondFactorAsync(user, r.Code, r.RecoveryCode, ct).ConfigureAwait(false);
            return TypedResults.Ok(await CompleteSignInAsync(http, user, sessions, me, ctx, ct).ConfigureAwait(false));
        });

        auth.MapPost("/sign-out", async (HttpContext http, SessionService sessions, CancellationToken ct) =>
        {
            if (http.Items[typeof(SessionPrincipal)] is SessionPrincipal p)
                await sessions.RevokeAsync(p.SessionId, "Signed out", ct).ConfigureAwait(false);
            await http.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
            await http.SignOutAsync(AuthSchemes.Mfa).ConfigureAwait(false);
            return TypedResults.NoContent();
        });

        auth.MapPost("/password-reset/request", async (EmailRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.RequestPasswordResetAsync(r.Email, ct).ConfigureAwait(false);
            return TypedResults.Accepted((string?)null);
        });

        auth.MapPost("/password-reset/confirm", async (PasswordResetConfirmRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.ResetPasswordAsync(r.User, r.Token, r.NewPassword, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });

        auth.MapGet("/invitations/{token}", async (string token, InvitationService invitations, CancellationToken ct) =>
            TypedResults.Ok(await invitations.InfoAsync(token, ct).ConfigureAwait(false)));

        auth.MapPost("/invitations/accept", async (InvitationAcceptRequest r, InvitationService invitations, CancellationToken ct) =>
        {
            await invitations.AcceptNewAsync(r, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
    }

    /// <summary>The user of the partial (password-verified) sign-in, at the expected stage, with an unchanged security stamp.</summary>
    private static async Task<User> MfaUserAsync(HttpContext http, UserManager<User> users, string stage)
    {
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
    private static async Task<MeResponse> CompleteSignInAsync(HttpContext http, User user, SessionService sessions, MeService me,
        RequestContext ctx, CancellationToken ct)
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
