using System.Security.Claims;
using DirectService.Transportation.Application.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DirectService.Transportation.Web.Security;

/// <summary>Authentication schemes (ADR-05).</summary>
public static class AuthSchemes
{
    /// <summary>Full application session (after password + 2FA).</summary>
    public const string App = "dst";

    /// <summary>Short-lived partial sign-in between the password step and the second factor.</summary>
    public const string Mfa = "dst-mfa";
}

public static class AppClaims
{
    public const string SessionToken = "dst:sid";
    public const string SecurityStamp = "dst:stamp";
    public const string Stage = "dst:stage";
}

internal static class AuthSetup
{
    public static IServiceCollection AddAppAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(AuthSchemes.App)
            .AddCookie(AuthSchemes.App, o =>
            {
                o.Cookie.Name = "__Host-dst";
                Harden(o);
                o.ExpireTimeSpan = TimeSpan.FromHours(12); // absolute lifetime is enforced by the session row (Q-04)
                o.SlidingExpiration = false;
                o.Events.OnValidatePrincipal = ValidateSessionAsync;
            })
            .AddCookie(AuthSchemes.Mfa, o =>
            {
                o.Cookie.Name = "__Host-dst-mfa";
                Harden(o);
                o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
                o.SlidingExpiration = false;
            });
        return services;
    }

    /// <summary>HTTP-only, Secure, SameSite=Strict cookies; API calls get 401/403 instead of redirects (SEC-03).</summary>
    private static void Harden(CookieAuthenticationOptions o)
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
    }

    /// <summary>Every request: the session row must still be valid (revocation, idle timeout, expiry, security stamp).</summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var token = context.Principal?.FindFirstValue(AppClaims.SessionToken);
        var stamp = context.Principal?.FindFirstValue(AppClaims.SecurityStamp);
        if (token is null || stamp is null)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }
        var sessions = context.HttpContext.RequestServices.GetRequiredService<SessionService>();
        var principal = await sessions.ValidateAsync(token, stamp, context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (principal is null)
        {
            await RejectAsync(context).ConfigureAwait(false);
            return;
        }
        context.HttpContext.Items[typeof(SessionPrincipal)] = principal;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(AuthSchemes.App).ConfigureAwait(false);
    }

    public static ClaimsPrincipal AppPrincipal(Guid userPublicId, string fullName, string sessionToken, string securityStamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userPublicId.ToString("N")),
            new Claim(ClaimTypes.Name, fullName),
            new Claim(AppClaims.SessionToken, sessionToken),
            new Claim(AppClaims.SecurityStamp, securityStamp),
        ], AuthSchemes.App));

    public static ClaimsPrincipal MfaPrincipal(Guid userPublicId, string stage, string securityStamp) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userPublicId.ToString("N")),
            new Claim(AppClaims.Stage, stage),
            new Claim(AppClaims.SecurityStamp, securityStamp),
        ], AuthSchemes.Mfa));
}
