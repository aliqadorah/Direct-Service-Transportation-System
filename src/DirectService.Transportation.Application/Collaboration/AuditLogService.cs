using System.Reflection;
using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The company audit log (FR-188, FR-189): read-only for everyone (rows are append-only, the database user cannot update them); filters by
/// user, action, record type, period and text; each row shows the user, time, action, the record by its number or name (never an internal
/// id, SEC-22), the reason and the changed fields old → new; Excel/CSV/PDF export. Visible to administrators (settings.manage or
/// users.manage, DT-121) for the selected company.
/// </summary>
public sealed class AuditLogService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, ReportExporter exporter)
{
    public const int MaxExportRows = 20_000;

    private static readonly HashSet<string> Hidden = ["Id", "TenantId", "CompanyId", "PublicId", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "RowVersion", "_reason"];

    /// <summary>Record types of the tenant (not of one company) shown with the company's log: users, roles, permissions, invitations, sign-ins (review M5).</summary>
    private static readonly string[] TenantTypes = ["CORE.User", "CORE.Role", "CORE.RolePermission", "CORE.UserCompanyRole", "CORE.Invitation", "CORE.Tenant", "CORE.UserRecoveryCode"];

    /// <summary>Personal fields of drivers shown only with drv.view (DT-36, review M4).</summary>
    private static readonly HashSet<string> DriverPersonal = ["NationalId", "Iban", "CliqAlias", "BankName", "Phone1", "Phone2", "Address"];

    private void EnsureAllowed()
    {
        // Checked here too: background exports run later with the requester's current permissions (review M7).
        if (!ctx.HasPermission(Contracts.Security.PermissionKeys.SettingsManage) && !ctx.HasPermission(Contracts.Security.PermissionKeys.UsersManage)) throw Fail.Forbidden();
    }

    private IQueryable<AuditLog> Filter(AuditLogQuery q)
    {
        EnsureAllowed();
        var companyId = ctx.CompanyId ?? throw Fail.Forbidden();
        var tenantId = ctx.TenantId;
        var query = db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == companyId || (a.CompanyId == null && a.TenantId == tenantId && (a.EntityType == null || TenantTypes.Contains(a.EntityType))));
        if (!string.IsNullOrWhiteSpace(q.User))
        {
            var pid = support.Decode<User>(q.User) ?? throw Fail.Field("user", ErrorCodes.ValidationFormat, "user");
            query = query.Where(a => a.User != null && a.User.PublicId == pid);
        }
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action == q.Action);
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        if (q.From is DateOnly from) query = query.Where(a => a.OccurredAt >= from.ToDateTime(TimeOnly.MinValue));
        if (q.To is DateOnly to) query = query.Where(a => a.OccurredAt < to.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(a => ((a.NewValues != null && a.NewValues.Contains(text)) || (a.OldValues != null && a.OldValues.Contains(text)))
            // Erased personal data cannot be found again through the text of the log (SEC-19).
            && !db.Set<AuditLog>().Any(e => e.Action == ErasedRecords.EraseAction && e.EntityType == a.EntityType && e.EntityId == a.EntityId));
        return query;
    }

    public async Task<PagedResult<AuditLogEntry>> ListAsync(AuditLogQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var query = Filter(q);
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await Load(query.OrderByDescending(a => a.Id).Skip(paging.Skip).Take(paging.SafePageSize), ct).ConfigureAwait(false);
        return new PagedResult<AuditLogEntry>(rows, paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>Record types and actions present in the company's log (filter choices).</summary>
    public async Task<object> FacetsAsync(CancellationToken ct)
    {
        EnsureAllowed();
        var companyId = ctx.CompanyId ?? throw Fail.Forbidden();
        var types = await db.Set<AuditLog>().AsNoTracking().Where(a => a.CompanyId == companyId && a.EntityType != null).Select(a => a.EntityType!).Distinct().OrderBy(t => t).ToListAsync(ct)
            .ConfigureAwait(false);
        var actions = await db.Set<AuditLog>().AsNoTracking().Where(a => a.CompanyId == companyId).Select(a => a.Action).Distinct().OrderBy(t => t).ToListAsync(ct).ConfigureAwait(false);
        // FR-189 "filter by user": the people found in this company's log (their tokens are what the user filter takes).
        // Users with a row in this company's log (EXISTS per user rather than DISTINCT over the whole log, review L6).
        var users = (await db.Set<User>().AsNoTracking().IgnoreQueryFilters()
                .Where(u => db.Set<AuditLog>().Any(a => a.CompanyId == companyId && a.UserId == u.Id))
                .Select(u => new { u.PublicId, u.FullName }).OrderBy(u => u.FullName).Take(500).ToListAsync(ct).ConfigureAwait(false))
            .Select(u => new { id = support.Token<User>(u.PublicId), name = u.FullName }).ToList();
        return new { entityTypes = types, actions, users };
    }

    public async Task<GeneratedFile> ExportAsync(AuditLogQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var rows = await Load(Filter(q).OrderByDescending(a => a.Id).Take(MaxExportRows), ct).ConfigureAwait(false);
        string L(string ar, string en) => exporter.L(ar, en);
        var table = new ReportTable(L("سجل التدقيق", "Audit log"), [],
            [new(L("الوقت", "Time"), ReportValue.Date), new(L("المستخدم", "User")), new(L("الإجراء", "Action")), new(L("نوع السجل", "Record type")), new(L("السجل", "Record")),
             new(L("السبب", "Reason")), new(L("التغييرات", "Changes")), new(L("عنوان IP", "IP address"))],
            rows.Select(r => (IReadOnlyList<object?>)[r.OccurredAt, r.User, r.Action, r.EntityType, r.Record, r.Reason,
                string.Join(" | ", r.Changes.Select(c => $"{c.Field}: {c.Old} → {c.New}")), r.IpAddress]).ToList());
        return await exporter.ExportAsync(table, format, "audit-log", ct).ConfigureAwait(false);
    }

    private async Task<List<AuditLogEntry>> Load(IQueryable<AuditLog> q, CancellationToken ct)
    {
        var rows = await q.Select(a => new { a.Id, a.OccurredAt, User = a.User != null ? a.User.FullName : null, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues, a.IpAddress })
            .ToListAsync(ct).ConfigureAwait(false);
        var names = new Dictionary<(string, long), string>();
        foreach (var g in rows.Where(r => r.EntityType is not null && r.EntityId is not null).GroupBy(r => r.EntityType!))
            foreach (var (id, name) in await NamesAsync(g.Key, g.Select(r => r.EntityId!.Value).Distinct().ToList(), ct).ConfigureAwait(false))
                names[(g.Key, id)] = name;
        var erased = await ErasedRecords.FindAsync(db, rows.Where(r => r.EntityType is not null && r.EntityId is not null).Select(r => (r.EntityType!, r.EntityId!.Value)), ct)
            .ConfigureAwait(false);
        return rows.Select(r =>
        {
            var oldValues = Parse(r.OldValues);
            var newValues = Parse(r.NewValues);
            var reason = newValues.GetValueOrDefault("_reason");
            var mask = r.EntityType == "TRN.Driver" && !ctx.HasPermission(Contracts.Security.PermissionKeys.DrvView);
            var isErased = r.EntityType is not null && r.EntityId is long eid && erased.Contains((r.EntityType, eid));
            var changes = oldValues.Keys.Union(newValues.Keys).Where(k => !Hidden.Contains(k) && oldValues.GetValueOrDefault(k) != newValues.GetValueOrDefault(k))
                .Select(k => (mask && DriverPersonal.Contains(k)) || (isErased && ErasedRecords.IsPersonal(r.EntityType, k)) ? new AuditChange(k, ErasedRecords.Masked, ErasedRecords.Masked)
                    : new AuditChange(k, oldValues.GetValueOrDefault(k), newValues.GetValueOrDefault(k))).ToList();
            var record = r.EntityType is not null && r.EntityId is long id ? names.GetValueOrDefault((r.EntityType, id)) : null;
            return new AuditLogEntry(DateTime.SpecifyKind(r.OccurredAt, DateTimeKind.Utc), r.User, r.Action, r.EntityType, record, reason, changes, r.IpAddress);
        }).ToList();
    }

    private static Dictionary<string, string?> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return [];
            return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => p.Value.GetString(),
                _ => p.Value.GetRawText(),
            });
        }
        catch (JsonException)
        {
            return [];
        }
    }

    // ------------------------------------------------------------ record names

    private static readonly string[] NameProperties = ["Number", "ReceiptNo", "Name", "FullName", "Code", "PeriodMonth", "PostingKey", "FieldKey", "Email"];

    private static readonly Dictionary<string, Type> Types = typeof(AuditableEntity).Assembly.GetTypes()
        .Where(t => t.IsClass && !t.IsAbstract && typeof(AuditableEntity).IsAssignableFrom(t))
        .GroupBy(t => (t.Namespace?.Split('.').LastOrDefault()?.ToUpperInvariant() ?? "APP") + "." + t.Name)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    private static readonly MethodInfo NamesMethod = typeof(AuditLogService).GetMethod(nameof(NamesOfAsync), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Number or name of each record (deleted ones included), by its type; records of other companies are never read.</summary>
    private async Task<IReadOnlyList<(long Id, string Name)>> NamesAsync(string entityType, List<long> ids, CancellationToken ct)
    {
        if (!Types.TryGetValue(entityType, out var type)) return [];
        var prop = NameProperties.Select(n => type.GetProperty(n)).FirstOrDefault(p => p is not null && p.PropertyType == typeof(string));
        if (prop is null) return [];
        var task = (Task<IReadOnlyList<(long, string)>>)NamesMethod.MakeGenericMethod(type).Invoke(this, [ids, prop.Name, ct])!;
        return await task.ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<(long, string)>> NamesOfAsync<T>(List<long> ids, string property, CancellationToken ct) where T : AuditableEntity
    {
        // Soft-deleted records keep their names in the log; tenant and company filters still apply.
        var rows = await db.Set<T>().AsNoTracking().IgnoreQueryFilters(["SoftDelete"]).Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, Name = EF.Property<string?>(e, property) }).ToListAsync(ct).ConfigureAwait(false);
        return rows.Where(r => r.Name is not null).Select(r => (r.Id, r.Name!)).ToList();
    }
}
