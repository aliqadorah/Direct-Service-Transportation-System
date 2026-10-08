using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

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

    /// <summary>The caller may act on the target user only if the target is not above the caller.</summary>
    public async Task EnsureCanManageUserAsync(long targetUserId, CancellationToken ct)
    {
        if (ctx.HasPermission(PermissionKeys.TenantManage)) return;
        var grants = await access.GrantsAsync(targetUserId, ct).ConfigureAwait(false);
        if (grants.Any(g => g.Permissions.Contains(PermissionKeys.TenantManage))) throw Fail.Forbidden();
    }

    /// <summary>Permission keys of a role (by id).</summary>
    public async Task<IReadOnlyList<string>> RoleKeysAsync(long roleId, CancellationToken ct) =>
        await db.Set<RolePermission>().AsNoTracking().Where(p => p.RoleId == roleId)
            .Select(p => p.Permission.PermissionKey).ToListAsync(ct).ConfigureAwait(false);
}
