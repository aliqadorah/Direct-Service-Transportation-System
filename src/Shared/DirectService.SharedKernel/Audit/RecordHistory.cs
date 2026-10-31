using DirectService.Shared.Contracts.History;
using Microsoft.EntityFrameworkCore;

namespace DirectService.SharedKernel;

/// <summary>
/// The readable history of a record for every module's services (FR-166, DT-188; Stage 3 session 6c, was <c>MasterDataSupport.HistoryAsync</c>):
/// the newest 200 audit rows of the record in the current company, described by the shared audit describer (<see cref="IAuditDescriber"/>).
/// Technical-only edits (e.g. the journal link set by a posting) are left out.
/// </summary>
public sealed class RecordHistory(IAppDbContext db, IRequestContext ctx, IAuditDescriber describer)
{
    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string entityType, long entityId, CancellationToken ct)
    {
        var companyId = ctx.CompanyId;
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.EntityType == entityType && a.EntityId == entityId)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id).Take(200)
            .Select(a => new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues))
            .ToListAsync(ct).ConfigureAwait(false);
        var described = await describer.DescribeAsync(rows, new DescribeOptions(), ct).ConfigureAwait(false);
        return described.Where(h => !h.IsTechnicalOnly)
            .Select(h => new HistoryEntry(DateTime.SpecifyKind(h.Row.OccurredAt, DateTimeKind.Utc), h.User, h.Row.Action, h.Reason, h.Changes, h.Sentence, h.ActionLabel,
                h.Lines.Count == 0 ? null : h.Lines, h.Links.Count == 0 ? null : h.Links))
            .ToList();
    }
}
