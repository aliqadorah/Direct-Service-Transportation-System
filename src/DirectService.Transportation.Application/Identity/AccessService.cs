using DirectService.Transportation.Application.Abstractions;
using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DirectService.Transportation.Application.Identity;

/// <summary>A user's role and permission keys in one company.</summary>
public sealed record CompanyGrant(long CompanyId, Guid CompanyPublicId, string CompanyName, string? CompanyNameEn, string CompanyStatus,
    long RoleId, Guid RolePublicId, string RoleName, bool IsPresetRole, IReadOnlySet<string> Permissions);

/// <summary>
/// Resolves what a user may do (PRD 3.3: permissions per user per company, checked on the server).
/// Results are cached briefly per user and dropped on any role, grant or user change (<see cref="Invalidate"/>).
/// </summary>
public sealed class AccessService(IAppDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);
    private static readonly string[] AllFilters = ["Tenant", "Company"];

    /// <summary>All companies of the user's tenant where the user holds a role (active or archived).</summary>
    public async Task<IReadOnlyList<CompanyGrant>> GrantsAsync(long userId, CancellationToken ct)
    {
        if (cache.TryGetValue<IReadOnlyList<CompanyGrant>>(Key(userId), out var cached) && cached is not null)
            return cached;

        var rows = await db.Set<UserCompanyRole>().IgnoreQueryFilters(AllFilters).AsNoTracking()
            .Where(r => r.UserId == userId && !r.Company.IsDeleted && !r.Role.IsDeleted)
            .Select(r => new
            {
                r.CompanyId, CompanyPublicId = r.Company.PublicId, CompanyName = r.Company.Name, r.Company.NameEn,
                CompanyStatus = r.Company.Status, r.RoleId, RolePublicId = r.Role.PublicId, RoleName = r.Role.Name, r.Role.IsPreset,
                Keys = r.Role.RolePermissions.Select(p => p.Permission.PermissionKey).ToList(),
            })
            .ToListAsync(ct).ConfigureAwait(false);

        IReadOnlyList<CompanyGrant> grants = rows
            .Select(r => new CompanyGrant(r.CompanyId, r.CompanyPublicId, r.CompanyName, r.NameEn, r.CompanyStatus,
                r.RoleId, r.RolePublicId, r.RoleName, r.IsPreset, r.Keys.ToHashSet(StringComparer.Ordinal)))
            .OrderBy(g => g.CompanyName, StringComparer.CurrentCulture)
            .ToList();
        cache.Set(Key(userId), grants, CacheFor);
        return grants;
    }

    public async Task<CompanyGrant?> GrantAsync(long userId, long companyId, CancellationToken ct) =>
        (await GrantsAsync(userId, ct).ConfigureAwait(false)).FirstOrDefault(g => g.CompanyId == companyId);

    /// <summary>Tenant-level permission (e.g. core.tenant.manage): held in at least one active company.</summary>
    public async Task<bool> HasTenantPermissionAsync(long userId, string key, CancellationToken ct) =>
        (await GrantsAsync(userId, ct).ConfigureAwait(false)).Any(g => g.CompanyStatus == "Active" && g.Permissions.Contains(key));

    public void Invalidate(long userId) => cache.Remove(Key(userId));

    private static string Key(long userId) => "grants:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
