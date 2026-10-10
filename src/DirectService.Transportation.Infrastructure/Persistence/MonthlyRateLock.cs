using DirectService.Transportation.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// Reads monthly site rates WITH (UPDLOCK, ROWLOCK) inside the trip transaction (B4 review M2): clearing a site's rates and saving a
/// trip that uses them serialise, so the FR-035 check in the clearing transaction always sees the trip. Raw SQL (table hint) —
/// listed in BUILD_STATUS.md.
/// </summary>
public sealed class MonthlyRateLock(MasterDbContext db, IDataScope scope) : IMonthlyRateLock
{
    private sealed record RateRow(long LoadingSiteId, string RateMonth, decimal? OwnFleetRate, decimal? ExternalFleetRate);

    public async Task<IReadOnlyList<LockedMonthlyRate>> ReadAsync(IReadOnlyCollection<string> months, long? loadingSiteId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(months);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Monthly rates are locked only inside the trip's transaction.");
        var companyId = scope.CompanyId ?? throw new InvalidOperationException("No company in scope.");
        var result = new List<LockedMonthlyRate>();
        foreach (var month in months.Distinct(StringComparer.Ordinal))
        {
            var rows = loadingSiteId is long site
                ? await db.Database.SqlQuery<RateRow>($"""
                    SELECT LoadingSiteId, RateMonth, OwnFleetRate, ExternalFleetRate
                    FROM trn.TRN_MonthlySiteRates WITH (UPDLOCK, ROWLOCK)
                    WHERE CompanyId = {companyId} AND RateMonth = {month} AND LoadingSiteId = {site} AND IsDeleted = 0
                    """).ToListAsync(ct).ConfigureAwait(false)
                : await db.Database.SqlQuery<RateRow>($"""
                    SELECT LoadingSiteId, RateMonth, OwnFleetRate, ExternalFleetRate
                    FROM trn.TRN_MonthlySiteRates WITH (UPDLOCK, ROWLOCK)
                    WHERE CompanyId = {companyId} AND RateMonth = {month} AND IsDeleted = 0
                    """).ToListAsync(ct).ConfigureAwait(false);
            result.AddRange(rows.Select(r => new LockedMonthlyRate(r.LoadingSiteId, r.RateMonth, r.OwnFleetRate, r.ExternalFleetRate)));
        }
        return result;
    }
}
