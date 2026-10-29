using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DirectService.Transportation.Application.Abstractions;

/// <summary>A monthly site rate read under an update lock.</summary>
public sealed record LockedMonthlyRate(long LoadingSiteId, string RateMonth, decimal? OwnFleetRate, decimal? ExternalFleetRate);

/// <summary>
/// Monthly site rates read WITH (UPDLOCK) inside the trip transaction, so clearing rates and saving trips serialise (FR-035,
/// B4 review M2). <paramref name="loadingSiteId"/> null = every site of the months.
/// </summary>
public interface IMonthlyRateLock
{
    Task<IReadOnlyList<LockedMonthlyRate>> ReadAsync(IReadOnlyCollection<string> months, long? loadingSiteId, CancellationToken ct);
}

/// <summary>Refuses activity dated in a closed period (BR-036, FR-133).</summary>
public interface IPeriodGuard
{
    Task EnsureOpenAsync(DateOnly date, CancellationToken ct);
}
