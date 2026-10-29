using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DirectService.SharedKernel;

/// <summary>
/// FR-163: an issued document keeps the names it was issued with. Master data is renamed by id (the cascade is immediate everywhere else),
/// and every rename is in the append-only audit log (old → new, with its time); so the name a record had when a document was issued is the
/// old value of the first rename after that moment, or the current name when it was not renamed since. No snapshot columns are needed
/// (the database design has none). A record erased on request (SEC-19) never gets its old name back.
/// </summary>
public sealed class IssuedNames(IAppDbContext db, IRequestContext ctx)
{
    /// <summary>The name of one record when the document was issued (<paramref name="issuedAt"/> null = not issued yet: the current name).</summary>
    public async Task<string> OfAsync(string entityType, long id, string field, string current, DateTime? issuedAt, CancellationToken ct) =>
        issuedAt is null ? current : (await ManyAsync(entityType, [id], field, issuedAt.Value, ct).ConfigureAwait(false)).GetValueOrDefault(id, current);

    /// <summary>The names, at <paramref name="issuedAt"/>, of the records renamed since; records not renamed are absent (use the current name).</summary>
    public async Task<IReadOnlyDictionary<long, string>> ManyAsync(string entityType, IReadOnlyCollection<long> ids, string field, DateTime issuedAt, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<long, string>();
        var key = "\"" + field + "\"";
        // The current company's rows only (master data belongs to one company); also lets IX_AuditLogs_Entity serve the query.
        var company = ctx.CompanyId ?? throw new InvalidOperationException("Issued names need a company context.");
        var rows = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == company && a.EntityType == entityType && a.EntityId != null && ids.Contains(a.EntityId.Value) && a.OccurredAt > issuedAt
                && (a.Action == ErasedRecords.EraseAction || (a.OldValues != null && a.OldValues.Contains(key))))
            .OrderBy(a => a.OccurredAt).ThenBy(a => a.Id)
            .Select(a => new { Id = a.EntityId!.Value, a.Action, a.OldValues })
            .ToListAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<long, string>();
        foreach (var r in rows.GroupBy(r => r.Id))
        {
            if (r.Any(x => x.Action == ErasedRecords.EraseAction)) continue;   // erased: the current (erased) value stays
            // The first change after the issue that carries a usable old value of the field.
            foreach (var row in r)
            {
                using var doc = JsonDocument.Parse(row.OldValues!);
                if (doc.RootElement.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } name)
                {
                    result[r.Key] = name;
                    break;
                }
            }
        }
        return result;
    }
}
