using System.Globalization;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Configuration;
using DirectService.Transportation.Contracts.MasterData;
using DirectService.Transportation.Contracts.Pricing;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Domain.Trn;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Pricing;

/// <summary>
/// trn.TRN_RateHistory (FR-026, FR-036): every add, edit, archive, restore or clearing of a tariff line or monthly site rate writes
/// one insert-only row with the values before and after, the user and the time. Values are stored as readable snapshots (names of
/// parties, materials and sites at the time of the change; "All" = null) with the action under "_action".
/// </summary>
public sealed class RateHistoryLog(IAppDbContext db, IRequestContext ctx, TimeProvider clock)
{
    public const string Tariff = "Tariff";
    public const string MonthlySiteRate = "MonthlySiteRate";
    private const string ActionKey = "_action";
    private const string SubjectKey = "_subject";

    /// <summary>Adds a history row (saved with the caller's next SaveChanges). <paramref name="sourceId"/> must be the saved row's id.</summary>
    public void Add(string sourceType, long sourceId, string action, IReadOnlyDictionary<string, object?>? before, IReadOnlyDictionary<string, object?> after, string? subject = null)
    {
        ArgumentNullException.ThrowIfNull(after);
        var newValues = new Dictionary<string, object?>(after) { [ActionKey] = action };
        if (subject is not null) newValues[SubjectKey] = subject;
        var now = clock.GetUtcNow().UtcDateTime;
        db.Set<RateHistory>().Add(new RateHistory
        {
            SourceType = sourceType,
            SourceId = sourceId,
            OldValues = before is null ? null : JsonSerializer.Serialize(before),
            NewValues = JsonSerializer.Serialize(newValues),
            ChangedBy = ctx.UserId ?? throw new InvalidOperationException("Rate history needs the acting user."),
            ChangedAt = now,
        });
    }

    /// <summary>Formats a value for a snapshot: amounts invariant with up to 4 decimals, dates yyyy-MM-dd.</summary>
    public static object? Value(object? v) => v switch
    {
        null => null,
        decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v,
    };

    /// <summary>History of one or more source rows, newest first (at most 500 entries): changed fields old → new.</summary>
    public async Task<IReadOnlyList<RateHistoryEntry>> ReadAsync(string sourceType, IReadOnlyCollection<long> sourceIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceIds);
        if (sourceIds.Count == 0) return [];
        var companyId = ctx.CompanyId;
        var rows = await db.Set<RateHistory>().AsNoTracking()
            .Where(h => h.CompanyId == companyId && h.SourceType == sourceType && sourceIds.Contains(h.SourceId))
            .OrderByDescending(h => h.ChangedAt).ThenByDescending(h => h.Id).Take(500)
            .Select(h => new { h.ChangedAt, h.ChangedBy, h.OldValues, h.NewValues })
            .ToListAsync(ct).ConfigureAwait(false);
        var userIds = rows.Select(r => r.ChangedBy).Distinct().ToList();
        // Names of the users who changed this company's rates (support staff have no tenant, hence no filter).
        var users = await db.Set<User>().IgnoreQueryFilters().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct).ConfigureAwait(false);
        return rows.Select(r =>
        {
            var before = Parse(r.OldValues);
            var after = Parse(r.NewValues);
            var action = after.TryGetValue(ActionKey, out var a) ? CustomFieldService.Text(a) ?? "Update" : "Update";
            var subject = after.TryGetValue(SubjectKey, out var s) ? CustomFieldService.Text(s) : null;
            var changes = new List<FieldChange>();
            foreach (var key in after.Keys.Where(k => !k.StartsWith('_')))
            {
                var o = before.TryGetValue(key, out var ov) ? CustomFieldService.Text(ov) : null;
                var n = CustomFieldService.Text(after[key]);
                if (o != n) changes.Add(new FieldChange(Camel(key), o, n));
            }
            return new RateHistoryEntry(DateTime.SpecifyKind(r.ChangedAt, DateTimeKind.Utc), users.GetValueOrDefault(r.ChangedBy), action, subject, changes);
        }).ToList();
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Camel(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
