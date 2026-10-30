using System.Security.Claims;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Platform.Contracts.Identity;
using DirectService.Platform.Domain;
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

        // DT-229: the link sent to a new email address (also the account page /confirm-email-change); only the account's user, signed in.
        auth.MapPost("/confirm-email-change", async (LinkTokenRequest r, HttpContext http, EmailChangeService changes, AuthFlow flow, CancellationToken ct) =>
        {
            var user = await changes.ConfirmAsync(r.User, r.Token, ct).ConfigureAwait(false);
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);   // new security stamp: this session stays signed in
            return TypedResults.NoContent();
        });
        auth.MapPost("/verify-email", async (LinkTokenRequest r, AccountService accounts, CancellationToken ct) =>
        {
            await accounts.VerifyEmailAsync(r.User, r.Token, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });

        auth.MapPost("/sign-in", async (SignInRequest r, HttpContext http, AuthFlow flow, CancellationToken ct) =>
        {
            var next = await flow.PasswordAsync(http, r.Login ?? r.Email ?? string.Empty, r.Password, ct).ConfigureAwait(false);
            return TypedResults.Ok(await ResponseAsync(next, flow, ct).ConfigureAwait(false));
        });

        // Revision 6: the forced password change after the password step (user created or reset by an administrator).
        auth.MapPost("/change-password", async (NewPasswordRequest r, HttpContext http, AuthFlow flow, CancellationToken ct) =>
        {
            var next = await flow.ChangePasswordAsync(http, r.NewPassword, ct).ConfigureAwait(false);
            return TypedResults.Ok(await ResponseAsync(next, flow, ct).ConfigureAwait(false));
        });

        auth.MapGet("/2fa/setup", async (HttpContext http, AuthFlow flow) => TypedResults.Ok(await flow.SetupAsync(http).ConfigureAwait(false)));

        auth.MapPost("/2fa/setup", async (TwoFactorCodeRequest r, HttpContext http, AuthFlow flow, CancellationToken ct) =>
            TypedResults.Ok(await flow.CompleteSetupAsync(http, r.Code, ct).ConfigureAwait(false)));

        auth.MapPost("/2fa/skip", async (HttpContext http, AuthFlow flow, CancellationToken ct) =>
            TypedResults.Ok(await flow.SkipSetupAsync(http, ct).ConfigureAwait(false)));

        auth.MapPost("/2fa/verify", async (TwoFactorCodeRequest r, HttpContext http, AuthFlow flow, CancellationToken ct) =>
            TypedResults.Ok(await ResponseAsync(await flow.VerifyAsync(http, r.Code, r.RecoveryCode, ct).ConfigureAwait(false), flow, ct).ConfigureAwait(false)));   // DT-205: "done" or "changePassword"

        auth.MapPost("/sign-out", async (HttpContext http, AuthFlow flow, CancellationToken ct) =>
        {
            await flow.SignOutAsync(http, ct).ConfigureAwait(false);
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

    private static async Task<SignInResponse> ResponseAsync(SignInNextStep next, AuthFlow flow, CancellationToken ct) => next switch
    {
        SignInNextStep.TwoFactorSetup => new SignInResponse("twoFactorSetup"),
        SignInNextStep.TwoFactorOffer => new SignInResponse("twoFactorOffer"),
        SignInNextStep.ChangePassword => new SignInResponse("changePassword"),
        SignInNextStep.None => new SignInResponse("done", await flow.ProfileAsync(ct).ConfigureAwait(false)),
        _ => new SignInResponse("twoFactor"),
    };
}
