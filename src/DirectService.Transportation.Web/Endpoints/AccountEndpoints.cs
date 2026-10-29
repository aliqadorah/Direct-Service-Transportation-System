using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Web.Pipeline;
using DirectService.Transportation.Web.Security;
using Microsoft.AspNetCore.Identity;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>Query parameters of list endpoints (API-04).</summary>
public sealed record ListQuery(int? Page, int? PageSize, string? Search)
{
    public PageRequest ToPage() => new(Page ?? 1, PageSize ?? 50);
}

internal static class AccountEndpoints
{
    /// <summary>The signed-in user's own profile, preferences, sessions (FR-176, FR-178) and invitation acceptance.</summary>
    public static void MapMeEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").WithTags("Me").RequireAuthorization();
        // Platform staff see the host's real environment on the provider console's badge (owner testing 19 Oct 2026, DT-216); tenants never.
        me.MapGet("/", async (MeService s, IHostEnvironment env, CancellationToken ct) =>
        {
            var m = await s.GetAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(m.IsPlatformStaff ? m with { Environment = env.EnvironmentName } : m);
        });
        me.MapPut("/preferences", async (PreferencesRequest r, MeService s, CancellationToken ct) =>
        {
            await s.UpdatePreferencesAsync(r, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        me.MapGet("/sessions", async (MeService s, CancellationToken ct) => TypedResults.Ok(await s.SessionsAsync(ct).ConfigureAwait(false)));
        me.MapDelete("/sessions/{session}", async (string session, MeService s, CancellationToken ct) =>
        {
            await s.RevokeSessionAsync(session, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        me.MapPost("/sessions/sign-out-others", async (MeService s, CancellationToken ct) =>
            TypedResults.Ok(new { revoked = await s.SignOutOthersAsync(ct).ConfigureAwait(false) }));
        // Revision 6: own password change (users without email have no reset link) and turning 2FA on later. Each changes the security
        // stamp: this session's cookie is re-issued, every other session ends.
        me.MapPost("/password", async (ChangePasswordRequest r, HttpContext http, AuthService auth, AuthFlow flow, UserManager<User> users, IRequestContext ctx, SessionService sessions, CancellationToken ct) =>
        {
            var user = await SelfAsync(users, ctx).ConfigureAwait(false);
            await auth.ChangePasswordAsync(user, r.CurrentPassword, r.NewPassword, ct).ConfigureAwait(false);
            await sessions.RevokeAllAsync(user.Id, "Password changed", ctx.SessionId, ct).ConfigureAwait(false);   // DT-211: at once, not after the 30-s cache
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
        // DT-229: own email change — re-authentication (password, + code with 2FA), link to the new address, rate-limited like sign-in.
        me.MapPost("/email", async (ChangeEmailRequest r, EmailChangeService s, CancellationToken ct) =>
            TypedResults.Ok(await s.RequestOwnAsync(r, ct).ConfigureAwait(false))).RequireRateLimiting(RateLimits.Auth);
        me.MapDelete("/email", async (EmailChangeService s, CancellationToken ct) => TypedResults.Ok(await s.CancelOwnAsync(ct).ConfigureAwait(false)));
        me.MapGet("/2fa/setup", async (HttpContext http, AuthService auth, AuthFlow flow, UserManager<User> users, IRequestContext ctx, IQrCodeRenderer qr) =>
        {
            var user = await SelfAsync(users, ctx).ConfigureAwait(false);
            var setup = await auth.GetAuthenticatorSetupAsync(user).ConfigureAwait(false);
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);
            return TypedResults.Ok(new TwoFactorSetupResponse(setup.SharedKey, setup.AuthenticatorUri, qr.Svg(setup.AuthenticatorUri)));
        });
        me.MapPost("/2fa/setup", async (TwoFactorCodeRequest r, HttpContext http, AuthService auth, AuthFlow flow, UserManager<User> users, IRequestContext ctx, MeService s, TwoFactorSettingsService tfa, SessionService sessions, CancellationToken ct) =>
        {
            var user = await SelfAsync(users, ctx).ConfigureAwait(false);
            var codes = await auth.CompleteAuthenticatorSetupAsync(user, r.Code ?? string.Empty, ct).ConfigureAwait(false);
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);
            await sessions.RevokeAllAsync(user.Id, "Two-step verification turned on", ctx.SessionId, ct).ConfigureAwait(false);   // DT-211: at once
            await tfa.NotifyEnabledAsync(user, ct).ConfigureAwait(false);   // DT-210
            return TypedResults.Ok(new RecoveryCodesResponse(codes, await s.GetAsync(ct).ConfigureAwait(false)));
        });
        // DT-210 (owner testing 19 Oct 2026): manage two-step verification — status, turn off, change the app, new recovery codes. Each change
        // needs the current password and a current code or a recovery code; turning off and changing the app end every other session.
        me.MapGet("/2fa", async (TwoFactorSettingsService tfa, UserManager<User> users, IRequestContext ctx, CancellationToken ct) =>
            TypedResults.Ok(await tfa.StatusAsync(await SelfAsync(users, ctx).ConfigureAwait(false), ct).ConfigureAwait(false)));
        me.MapPost("/2fa/disable", async (TwoFactorStepUpRequest r, HttpContext http, TwoFactorSettingsService tfa, AuthFlow flow, UserManager<User> users, IRequestContext ctx, MeService s, CancellationToken ct) =>
        {
            var user = await SelfAsync(users, ctx).ConfigureAwait(false);
            await tfa.DisableAsync(user, r, ct).ConfigureAwait(false);
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);
            return TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false));
        });
        me.MapPost("/2fa/change", async (TwoFactorStepUpRequest r, TwoFactorSettingsService tfa, UserManager<User> users, IRequestContext ctx, IQrCodeRenderer qr, CancellationToken ct) =>
        {
            var setup = await tfa.StartChangeAsync(await SelfAsync(users, ctx).ConfigureAwait(false), r, ct).ConfigureAwait(false);
            return TypedResults.Ok(new TwoFactorSetupResponse(setup.SharedKey, setup.AuthenticatorUri, qr.Svg(setup.AuthenticatorUri)));
        });
        me.MapPost("/2fa/change/confirm", async (TwoFactorCodeRequest r, HttpContext http, TwoFactorSettingsService tfa, AuthFlow flow, UserManager<User> users, IRequestContext ctx, MeService s, CancellationToken ct) =>
        {
            var user = await SelfAsync(users, ctx).ConfigureAwait(false);
            await tfa.ConfirmChangeAsync(user, r.Code, ct).ConfigureAwait(false);
            await flow.RefreshAppCookieAsync(http, user).ConfigureAwait(false);
            return TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false));
        });
        me.MapPost("/2fa/recovery-codes", async (TwoFactorStepUpRequest r, TwoFactorSettingsService tfa, UserManager<User> users, IRequestContext ctx, MeService s, CancellationToken ct) =>
        {
            var codes = await tfa.NewRecoveryCodesAsync(await SelfAsync(users, ctx).ConfigureAwait(false), r, ct).ConfigureAwait(false);
            return TypedResults.Ok(new RecoveryCodesResponse(codes, await s.GetAsync(ct).ConfigureAwait(false)));
        });
        me.MapPost("/invitations/accept", async (InvitationAcceptRequest r, InvitationService s, CancellationToken ct) =>
        {
            await s.AcceptExistingAsync(r.Token, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
    }

    private static async Task<User> SelfAsync(UserManager<User> users, IRequestContext ctx) =>
        await users.FindByIdAsync((ctx.UserPublicId ?? throw Fail.Forbidden()).ToString("N")).ConfigureAwait(false) ?? throw Fail.Forbidden();

    /// <summary>Tenant administration (core.tenant.manage): security settings and companies (5.1, US-34, D-08/D-09).</summary>
    public static void MapTenantEndpoints(this RouteGroupBuilder api)
    {
        var tenant = api.MapGroup("/tenant").WithTags("Tenant").AddEndpointFilter<WriteGuardFilter>();
        tenant.MapGet("/", async (TenantService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        tenant.MapPut("/security", async (TenantSecurityRequest r, TenantService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateSecurityAsync(r, ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        tenant.MapGet("/companies", async (TenantService s, CancellationToken ct) => TypedResults.Ok(await s.CompaniesAsync(ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        tenant.MapPost("/companies", async (CreateCompanyRequest r, TenantService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CreateCompanyAsync(r, ct).ConfigureAwait(false)))
            .RequireTenantPermission(PermissionKeys.TenantManage);
        tenant.MapPost("/companies/{companyId}/archive", async (string companyId, ReasonRequest r, TenantService s, CancellationToken ct) =>
        {
            await s.ArchiveCompanyAsync(companyId, r.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireTenantPermission(PermissionKeys.TenantManage);
        tenant.MapPost("/companies/{companyId}/restore", async (string companyId, ReasonRequest r, TenantService s, CancellationToken ct) =>
        {
            await s.RestoreCompanyAsync(companyId, r.Reason, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireTenantPermission(PermissionKeys.TenantManage);
    }

    /// <summary>Users, roles and invitations of the current company (core.users.manage; FR-172 … FR-174).</summary>
    public static void MapUserAdminEndpoints(this RouteGroupBuilder company)
    {
        var users = company.MapGroup("/users").WithTags("Users").RequirePermission(PermissionKeys.UsersManage);
        users.MapGet("/", async ([AsParameters] ListQuery q, UserAdminService s, CancellationToken ct) =>
            TypedResults.Ok(await s.ListAsync(q.Search, q.ToPage(), ct).ConfigureAwait(false)));
        users.MapPost("/", async (CreateUserRequest r, UserAdminService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CreateWithPasswordAsync(r, ct).ConfigureAwait(false)));
        users.MapGet("/password-policy", async (UserAdminService s, CancellationToken ct) =>
            TypedResults.Ok(await s.PasswordPolicyAsync(ct).ConfigureAwait(false)));   // DT-208: rules shown under the password field
        // DT-229: name / username (Owner or Administrator only) and an email change that the user confirms.
        users.MapPut("/{user}", async (string user, UpdateUserRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.UpdateUserAsync(user, r, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/email", async (string user, StartEmailChangeRequest r, EmailChangeService s, CancellationToken ct) =>
            TypedResults.Ok(await s.StartForUserAsync(user, r, ct).ConfigureAwait(false))).RequireRateLimiting(RateLimits.Auth);
        users.MapDelete("/{user}/email", async (string user, EmailChangeService s, CancellationToken ct) =>
            TypedResults.Ok(await s.CancelForUserAsync(user, ct).ConfigureAwait(false)));
        users.MapPost("/{user}/set-password", async (string user, SetPasswordRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.SetPasswordAsync(user, r, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPut("/{user}/role", async (string user, AssignRoleRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.AssignRoleAsync(user, r.Role, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/remove", async (string user, ReasonRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.RemoveFromCompanyAsync(user, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/disable", async (string user, ReasonRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.DisableAsync(user, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/enable", async (string user, UserAdminService s, CancellationToken ct) =>
        { await s.EnableAsync(user, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/archive", async (string user, ReasonRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.ArchiveAsync(user, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/unlock", async (string user, UserAdminService s, CancellationToken ct) =>
        { await s.UnlockAsync(user, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/password-reset", async (string user, UserAdminService s, CancellationToken ct) =>
        { await s.SendPasswordResetAsync(user, ct).ConfigureAwait(false); return TypedResults.Accepted((string?)null); });
        users.MapPost("/{user}/reset-2fa", async (string user, ReasonRequest r, UserAdminService s, CancellationToken ct) =>
        { await s.ResetTwoFactorAsync(user, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        users.MapPost("/{user}/sign-out", async (string user, UserAdminService s, CancellationToken ct) =>
        { await s.SignOutAsync(user, ct).ConfigureAwait(false); return TypedResults.NoContent(); });

        var roles = company.MapGroup("/roles").WithTags("Roles").RequirePermission(PermissionKeys.UsersManage);
        roles.MapGet("/", async (RoleService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(ct).ConfigureAwait(false)));
        roles.MapGet("/permissions", async (RoleService s, CancellationToken ct) => TypedResults.Ok(await s.PermissionsAsync(ct).ConfigureAwait(false)));
        roles.MapPost("/", async (RoleRequest r, RoleService s, CancellationToken ct) => TypedResults.Ok(await s.CreateAsync(r, ct).ConfigureAwait(false)));
        roles.MapPut("/{role}", async (string role, RoleRequest r, RoleService s, CancellationToken ct) =>
            TypedResults.Ok(await s.UpdateAsync(role, r, ct).ConfigureAwait(false)));
        roles.MapPost("/{role}/delete", async (string role, ReasonRequest r, RoleService s, CancellationToken ct) =>
        { await s.DeleteAsync(role, r.Reason, ct).ConfigureAwait(false); return TypedResults.NoContent(); });

        var inv = company.MapGroup("/invitations").WithTags("Invitations").RequirePermission(PermissionKeys.UsersManage);
        inv.MapGet("/", async (InvitationService s, CancellationToken ct) => TypedResults.Ok(await s.ListAsync(ct).ConfigureAwait(false)));
        inv.MapPost("/", async (InviteRequest r, InvitationService s, CancellationToken ct) => TypedResults.Ok(await s.InviteAsync(r, ct).ConfigureAwait(false)));
        inv.MapPost("/{invitation}/resend", async (string invitation, InvitationService s, CancellationToken ct) =>
        { await s.ResendAsync(invitation, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
        inv.MapPost("/{invitation}/cancel", async (string invitation, InvitationService s, CancellationToken ct) =>
        { await s.CancelAsync(invitation, ct).ConfigureAwait(false); return TypedResults.NoContent(); });
    }
}
