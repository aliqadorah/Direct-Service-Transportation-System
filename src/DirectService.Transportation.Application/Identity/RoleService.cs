using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>
/// Roles of the tenant (PRD 3.3–3.5, FR-173): presets are fixed and cannot be deleted; custom roles are
/// any combination of keys; a custom role in use cannot be deleted until its users are reassigned.
/// Every grant/revoke is audited (3.3) by the interceptor.
/// </summary>
public sealed class RoleService(IAppDbContext db, IChangeContext changes, AccessService access, IUrlTokenService tokens, GrantGuard guard)
{
    public async Task<IReadOnlyList<PermissionItem>> PermissionsAsync(CancellationToken ct) =>
        (await db.Set<Permission>().AsNoTracking().OrderBy(p => p.Id).ToListAsync(ct).ConfigureAwait(false))
            .Select(p => new PermissionItem(p.PermissionKey, PermissionKeys.PrdName(p.PermissionKey), p.GroupName, p.DescriptionAr, p.DescriptionEn))
            .ToList();

    public async Task<IReadOnlyList<RoleResponse>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Set<Role>().AsNoTracking()
            .OrderByDescending(r => r.IsPreset).ThenBy(r => r.Name)
            .Select(r => new
            {
                r.PublicId, r.Name, r.IsPreset, r.RowVersion,
                Keys = r.RolePermissions.Select(p => p.Permission.PermissionKey).ToList(),
                Users = db.Set<UserCompanyRole>().Count(u => u.RoleId == r.Id && !u.User.IsDeleted),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.Select(r => new RoleResponse(tokens.Encode<Role>(r.PublicId), r.Name, r.IsPreset,
            r.Keys.Order(StringComparer.Ordinal).ToList(), r.Users, Convert.ToBase64String(r.RowVersion))).ToList();
    }

    public async Task<RoleResponse> CreateAsync(RoleRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = Validate(r);
        guard.EnsureCanGrant(r.Permissions); // SR-01
        var role = new Role { Name = name, IsPreset = false };
        await SetPermissionsAsync(role, r.Permissions, ct).ConfigureAwait(false);
        db.Set<Role>().Add(role);
        await SaveUniqueAsync(role, name, ct).ConfigureAwait(false);
        return (await ListAsync(ct).ConfigureAwait(false)).Single(x => x.Id == tokens.Encode<Role>(role.PublicId));
    }

    public async Task<RoleResponse> UpdateAsync(string roleToken, RoleRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var role = await FindAsync(roleToken, ct).ConfigureAwait(false);
        if (role.IsPreset) throw Fail.Rule(ErrorCodes.PresetRoleFixed);
        var name = Validate(r);
        guard.EnsureCanGrant(r.Permissions.Concat(role.RolePermissions.Select(p => p.Permission.PermissionKey))); // SR-01
        db.ExpectRowVersion(role, r.RowVersion);
        role.Name = name;
        await SetPermissionsAsync(role, r.Permissions, ct).ConfigureAwait(false);

        // BR-002: removing users.manage must not leave a company without an active user manager.
        if (!r.Permissions.Contains(PermissionKeys.UsersManage))
        {
            var companies = await db.Set<UserCompanyRole>().AsNoTracking().Where(u => u.RoleId == role.Id)
                .Select(u => u.CompanyId).Distinct().ToListAsync(ct).ConfigureAwait(false);
            foreach (var companyId in companies)
            {
                var others = await db.Set<UserCompanyRole>().AsNoTracking().AnyAsync(u => u.CompanyId == companyId && u.RoleId != role.Id
                    && u.User.Status == "Active" && !u.User.IsDeleted
                    && u.Role.RolePermissions.Any(p => p.Permission.PermissionKey == PermissionKeys.UsersManage), ct).ConfigureAwait(false);
                if (!others) throw Fail.Rule(ErrorCodes.LastUserManager);
            }
        }
        await SaveUniqueAsync(role, name, ct).ConfigureAwait(false);
        await InvalidateUsersAsync(role.Id, ct).ConfigureAwait(false);
        return (await ListAsync(ct).ConfigureAwait(false)).Single(x => x.Id == roleToken);
    }

    public async Task DeleteAsync(string roleToken, string reason, CancellationToken ct)
    {
        UserAdminService.RequireReasonPublic(reason);
        var role = await FindAsync(roleToken, ct).ConfigureAwait(false);
        if (role.IsPreset) throw Fail.Rule(ErrorCodes.PresetRoleFixed);
        guard.EnsureCanGrant(role.RolePermissions.Select(p => p.Permission.PermissionKey)); // SR-01
        var inUse = await db.Set<UserCompanyRole>().CountAsync(u => u.RoleId == role.Id && !u.User.IsDeleted, ct).ConfigureAwait(false);
        if (inUse > 0) throw Fail.Rule(ErrorCodes.RoleInUse, inUse);
        foreach (var p in role.RolePermissions)
        {
            p.DeleteReason = "Role deleted";
            db.Set<RolePermission>().Remove(p);
        }
        role.DeleteReason = reason;
        db.Set<Role>().Remove(role);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Validate(RoleRequest r)
    {
        var name = TextNormalizer.CleanText(r.Name);
        new Validator().Require("name", name).MaxLength("name", name, 100)
            .Check(r.Permissions is not null && r.Permissions.All(k => PermissionKeys.All.Contains(k)), "permissions")
            .Check(!PresetRoles.All.Contains(name ?? string.Empty, StringComparer.OrdinalIgnoreCase), "name", ErrorCodes.DuplicateKey)
            .ThrowIfInvalid();
        return name!;
    }

    private async Task SetPermissionsAsync(Role role, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        var permissions = await db.Set<Permission>().ToDictionaryAsync(p => p.PermissionKey, ct).ConfigureAwait(false);
        foreach (var existing in role.RolePermissions.ToList())
        {
            if (wanted.Remove(existing.Permission.PermissionKey)) continue;
            existing.DeleteReason = "Permission removed from role";
            db.Set<RolePermission>().Remove(existing);
            role.RolePermissions.Remove(existing);
        }
        foreach (var key in wanted)
            role.RolePermissions.Add(new RolePermission { Permission = permissions[key] });
        changes.SetAction(role, role.Id == 0 ? "Create" : "PermissionChange");
    }

    /// <summary>Friendly duplicate check; UX_Roles_TenantId_Name still guards races (mapped to 409 by the API).</summary>
    private async Task SaveUniqueAsync(Role role, string name, CancellationToken ct)
    {
        if (await db.Set<Role>().AnyAsync(x => x.Name == name && x.Id != role.Id, ct).ConfigureAwait(false))
            throw Fail.Conflict(ErrorCodes.DuplicateKey, name);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<Role> FindAsync(string roleToken, CancellationToken ct)
    {
        var publicId = tokens.Decode<Role>(roleToken) ?? throw Fail.NotFound();
        return await db.Set<Role>().Include(r => r.RolePermissions).ThenInclude(p => p.Permission)
            .SingleOrDefaultAsync(r => r.PublicId == publicId, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    private async Task InvalidateUsersAsync(long roleId, CancellationToken ct)
    {
        var users = await db.Set<UserCompanyRole>().AsNoTracking().Where(u => u.RoleId == roleId).Select(u => u.UserId)
            .Distinct().ToListAsync(ct).ConfigureAwait(false);
        foreach (var u in users) access.Invalidate(u);
    }
}
