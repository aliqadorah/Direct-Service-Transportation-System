using Microsoft.EntityFrameworkCore;

namespace DirectService.SharedKernel;

/// <summary>
/// Records whose personal data was erased on request (SEC-19). The record itself is cleared in place; the audit log is append-only and the
/// database user cannot change it (FR-189), so values written before the erasure stay there as evidence and every view of the log — the
/// record's history, the audit-log screen and its export — shows the personal fields of an erased record as <see cref="Masked"/>.
/// </summary>
public static class ErasedRecords
{
    public const string EraseAction = "Anonymise";

    public const string Masked = "***";

    private static readonly Dictionary<string, HashSet<string>> Personal = new(StringComparer.Ordinal)
    {
        ["TRN.Driver"] = ["FullName", "NationalId", "Phone1", "Phone2", "Address", "LicenceNo", "BankName", "Iban", "CliqAlias", "Notes", "CustomValues"],
        ["CORE.User"] = ["FullName", "Email", "NormalizedEmail", "UserName", "NormalizedUserName"],
        ["CORE.Invitation"] = ["Email"],
    };

    /// <summary>Whether a field of a record type holds personal data cleared by an erasure.</summary>
    public static bool IsPersonal(string? entityType, string field) => entityType is not null && Personal.TryGetValue(entityType, out var fields) && fields.Contains(field);

    /// <summary>The erased records among the given ones (an "Anonymise" row exists in the audit log).</summary>
    public static async Task<HashSet<(string Type, long Id)>> FindAsync(IAppDbContext db, IEnumerable<(string Type, long Id)> records, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var wanted = records.Where(r => Personal.ContainsKey(r.Type)).Distinct().ToList();
        if (wanted.Count == 0) return [];
        var types = wanted.Select(r => r.Type).Distinct().ToList();
        var ids = wanted.Select(r => (long?)r.Id).Distinct().ToList();
        var found = await db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Action == EraseAction && a.EntityType != null && types.Contains(a.EntityType) && ids.Contains(a.EntityId))
            .Select(a => new { a.EntityType, a.EntityId }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        return found.Select(a => (a.EntityType!, a.EntityId!.Value)).ToHashSet();
    }
}
