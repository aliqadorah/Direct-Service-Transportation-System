using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Identity;

/// <summary>Display names of users for the shared engines (<see cref="IUserNames"/>, DT-312); moves to Platform with the identity services.</summary>
public sealed class UserNames(IAppDbContext db) : IUserNames
{
    public Task<string> FullNameAsync(long userId, CancellationToken ct) =>
        db.Set<User>().IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct)!;
}
