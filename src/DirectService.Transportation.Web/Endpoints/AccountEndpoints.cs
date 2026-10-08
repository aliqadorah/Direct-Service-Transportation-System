using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Application.Platform;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Platform;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Web.Pipeline;

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
        me.MapGet("/", async (MeService s, CancellationToken ct) => TypedResults.Ok(await s.GetAsync(ct).ConfigureAwait(false)));
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
        me.MapPost("/invitations/accept", async (InvitationAcceptRequest r, InvitationService s, CancellationToken ct) =>
        {
            await s.AcceptExistingAsync(r.Token, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        });
    }

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
