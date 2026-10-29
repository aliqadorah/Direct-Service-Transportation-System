using DirectService.Platform.Domain;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Platform.Application.Identity;

/// <summary>Display names of users for the shared engines (<see cref="IUserNames"/>, DT-312, DT-322; Platform since Stage 2 session 12).</summary>
public sealed class UserNames(IAppDbContext db, IUrlTokenService tokens) : IUserNames
{
    public Task<string> FullNameAsync(long userId, CancellationToken ct) =>
        db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct)!;

    public async Task<IReadOnlyDictionary<long, string>> FullNamesAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0) return new Dictionary<long, string>();
        // Platform support staff have no tenant, hence no filter (only ids the caller found in its rows).
        var ids = userIds.Distinct().ToList();
        return await db.Set<User>().IgnoreQueryFilters().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<long, NamedUser>> VisibleAsync(IReadOnlyCollection<long> userIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        if (userIds.Count == 0) return new Dictionary<long, NamedUser>();
        var ids = userIds.Distinct().ToList();
        var rows = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.PublicId, u.FullName })
            .ToListAsync(ct).ConfigureAwait(false);
        return rows.ToDictionary(u => u.Id, u => new NamedUser(tokens.Encode<User>(u.PublicId), u.FullName));
    }
}
