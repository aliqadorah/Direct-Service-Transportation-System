using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>Refuses activity in a closed period (BR-036, FR-133), across operations and accounting.</summary>
public sealed class PeriodGuard(IAppDbContext db) : IPeriodGuard
{
    public async Task EnsureOpenAsync(DateOnly date, CancellationToken ct)
    {
        var month = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var closed = await db.Set<Domain.Acc.Period>().AsNoTracking()
            .AnyAsync(p => p.PeriodMonth == month && p.Status == "Closed", ct).ConfigureAwait(false);
        if (closed)
            throw Fail.Rule(ErrorCodes.PeriodClosed, date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), month);
    }
}
