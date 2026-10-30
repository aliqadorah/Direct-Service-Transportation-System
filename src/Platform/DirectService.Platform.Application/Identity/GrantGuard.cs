using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using DirectService.Platform.Contracts.Errors;
using DirectService.Platform.Contracts.Security;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Prevents privilege escalation through user and role administration (security review B1, finding SR-01):
/// <list type="bullet">
/// <item>a caller can only grant permissions they hold themselves in the current company (roles assigned,
/// invited with, created or edited);</item>
/// <item>only a caller holding core.tenant.manage may act on a user who holds core.tenant.manage in any company
/// (the Owner level), and only such a caller may edit or delete a role containing a key they lack.</item>
/// </list>
/// </summary>
public sealed class GrantGuard(IAppDbContext db, IRequestContext ctx, AccessService access)
{
    /// <summary>The caller must hold every key of <paramref name="keys"/> in the current company.</summary>
    public void EnsureCanGrant(IEnumerable<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(k => !ctx.HasPermission(k))) throw Fail.Forbidden();
    }

    /// <summary>
    /// The caller may act on the target user only if the target is not above the caller anywhere: in every company where the target has a
    /// role, the caller must manage users and hold every permission the target holds there (security review 18 Oct 2026, DT-202 — otherwise
    /// an administrator of one company could set the password of a user who has a role in another company and sign in with that access).
    /// With accountWide false (changing the role in, or removing from, the current company only): the target must not be
    /// Owner-level anywhere and must hold no permission here that the caller lacks.
    /// </summary>
    public Task EnsureCanManageUserAsync(long targetUserId, CancellationToken ct) => EnsureCanManageUserAsync(targetUserId, true, ct);

    /// <inheritdoc cref="EnsureCanManageUserAsync(long, CancellationToken)"/>
    public async Task EnsureCanManageUserAsync(long targetUserId, bool accountWide, CancellationToken ct)
    {
        if (ctx.HasPermission(PlatformPermissionKeys.TenantManage)) return;
        var callerId = ctx.UserId ?? throw Fail.Forbidden();
        var mine = (await access.GrantsAsync(callerId, ct).ConfigureAwait(false)).ToDictionary(g => g.CompanyId);
        foreach (var g in await access.GrantsAsync(targetUserId, ct).ConfigureAwait(false))
        {
            if (!accountWide && g.CompanyId != ctx.CompanyId)
            {
                if (g.Permissions.Contains(PlatformPermissionKeys.TenantManage)) throw Fail.Forbidden();
                continue;
            }
            if (!mine.TryGetValue(g.CompanyId, out var m) || !m.Permissions.Contains(PlatformPermissionKeys.UsersManage)
                || g.Permissions.Any(k => !m.Permissions.Contains(k)))
                throw Fail.Forbidden();
        }
    }

    /// <summary>Permission keys of a role (by id).</summary>
    public async Task<IReadOnlyList<string>> RoleKeysAsync(long roleId, CancellationToken ct) =>
        await db.Set<RolePermission>().AsNoTracking().Where(p => p.RoleId == roleId)
            .Select(p => p.Permission.PermissionKey).ToListAsync(ct).ConfigureAwait(false);
}
