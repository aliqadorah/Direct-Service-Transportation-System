using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Audit;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The company audit log (FR-188, FR-189): read-only for everyone (rows are append-only, the database user cannot update them); filters by
/// user, action, record type, period and text; each row shows the user, time, action, the record by its number or name (never an internal
/// id, SEC-22), the reason and the changes; Excel/CSV/PDF export. Visible to administrators (settings.manage or users.manage, DT-121) for
/// the selected company. Readable (owner feedback 14 Oct 2026, DT-188): every row is described by <see cref="AuditDescriber"/> — action
/// and record type in words, the event as a sentence, labelled and formatted changes, a journal entry with its lines as one event; the
/// stored values as they are ("technical details") only for settings.manage and only when asked.
/// </summary>
public sealed class AuditLogService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, ReportExporter exporter, AuditDescriber describer)
{
    public const int MaxExportRows = 20_000;

    private const string EntryLine = "ACC.EntryLine";
    private const string JournalEntry = "ACC.JournalEntry";

    /// <summary>Record types of the tenant (not of one company) shown with the company's log: users, roles, permissions, invitations, sign-ins (review M5).</summary>
    private static readonly string[] TenantTypes = ["CORE.User", "CORE.Role", "CORE.RolePermission", "CORE.UserCompanyRole", "CORE.Invitation", "CORE.Tenant", "CORE.UserRecoveryCode"];

    /// <summary>Columns of the technical details that are never shown, even raw (the stored row keys and stamps add nothing).</summary>
    private static readonly HashSet<string> RawHidden = ["Id", "TenantId", "CompanyId", "PublicId", "RowVersion", "_reason"];

    /// <summary>Personal fields of drivers shown only with drv.view (DT-36, review M4).</summary>
    private static readonly HashSet<string> DriverPersonal = ["NationalId", "Iban", "CliqAlias", "BankName", "Phone1", "Phone2", "Address"];

    private void EnsureAllowed()
    {
        // Checked here too: background exports run later with the requester's current permissions (review M7).
        if (!ctx.HasPermission(PermissionKeys.SettingsManage) && !ctx.HasPermission(PermissionKeys.UsersManage)) throw Fail.Forbidden();
    }

    /// <summary>The stored values may be shown to company administrators (settings.manage) who ask for them (DT-188).</summary>
    private bool Technical(AuditLogQuery q) => q.Technical && ctx.HasPermission(PermissionKeys.SettingsManage);

    private IQueryable<AuditLog> Filter(AuditLogQuery q)
    {
        EnsureAllowed();
        var companyId = ctx.CompanyId ?? throw Fail.Forbidden();
        var tenantId = ctx.TenantId;
        var query = db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.CompanyId == companyId || (a.CompanyId == null && a.TenantId == tenantId && (a.EntityType == null || TenantTypes.Contains(a.EntityType))));
        if (!string.IsNullOrWhiteSpace(q.User))
        {
            var pid = support.Decode<User>(q.User) ?? throw Fail.Field("user", ErrorCodes.ValidationFormat, new FieldRef("user"));
            // The acting user among the filtered users, as the former navigation AuditLog.User did (DT-323).
            query = query.Where(a => db.Set<User>().Any(u => u.Id == a.UserId && u.PublicId == pid));
        }
        if (!string.IsNullOrWhiteSpace(q.Action)) query = query.Where(a => a.Action == q.Action);
        if (!string.IsNullOrWhiteSpace(q.EntityType)) query = query.Where(a => a.EntityType == q.EntityType);
        // A journal entry and its lines are one event (item 6): lines saved with an entry are shown inside it, unless lines are asked for.
        if (q.EntityType != EntryLine)
            query = query.Where(a => a.EntityType != EntryLine
                || !db.Set<AuditLog>().Any(j => j.EntityType == JournalEntry && j.CompanyId == a.CompanyId && j.OccurredAt == a.OccurredAt));
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
        var rows = await Load(query.OrderByDescending(a => a.Id).Skip(paging.Skip).Take(paging.SafePageSize), Technical(q), ct).ConfigureAwait(false);
        return new PagedResult<AuditLogEntry>(rows, paging.SafePage, paging.SafePageSize, total);
    }

    /// <summary>Record types, actions and users present in the company's log (filter choices), each with its words.</summary>
    public async Task<object> FacetsAsync(CancellationToken ct)
    {
        EnsureAllowed();
        var companyId = ctx.CompanyId ?? throw Fail.Forbidden();
        var en = ctx.Language == "en";
        var types = await db.Set<AuditLog>().AsNoTracking().Where(a => a.CompanyId == companyId && a.EntityType != null).Select(a => a.EntityType!).Distinct().ToListAsync(ct)
            .ConfigureAwait(false);
        var actions = await db.Set<AuditLog>().AsNoTracking().Where(a => a.CompanyId == companyId).Select(a => a.Action).Distinct().ToListAsync(ct).ConfigureAwait(false);
        // FR-189 "filter by user": the people found in this company's log (their tokens are what the user filter takes).
        // Users with a row in this company's log (EXISTS per user rather than DISTINCT over the whole log, review L6).
        var users = (await db.Set<User>().AsNoTracking().IgnoreQueryFilters()
                .Where(u => db.Set<AuditLog>().Any(a => a.CompanyId == companyId && a.UserId == u.Id))
                .Select(u => new { u.PublicId, u.FullName }).OrderBy(u => u.FullName).Take(500).ToListAsync(ct).ConfigureAwait(false))
            .Select(u => new { id = support.Token<User>(u.PublicId), name = u.FullName }).ToList();
        return new
        {
            entityTypes = types.Select(t => new { id = t, name = AuditVocabulary.Entity(t).Title.In(en) }).OrderBy(t => t.name, StringComparer.CurrentCulture).ToList(),
            actions = actions.Select(a => new { id = a, name = AuditVocabulary.Action(a).Label.In(en) }).OrderBy(a => a.name, StringComparer.CurrentCulture).ToList(),
            users,
        };
    }

    /// <summary>Excel / CSV / PDF of the filtered log in the reader's words; with the technical details for an administrator who asks.</summary>
    public async Task<GeneratedFile> ExportAsync(AuditLogQuery q, string? format, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var technical = Technical(q);
        var rows = await Load(Filter(q).OrderByDescending(a => a.Id).Take(MaxExportRows), technical, ct).ConfigureAwait(false);
        string L(string ar, string en) => exporter.L(ar, en);
        var arrow = L("←", "→");   // from old to new in the reading direction
        List<ReportColumn> columns =
        [
            new(L("الوقت", "Time"), ReportValue.Date), new(L("المستخدم", "User")), new(L("الإجراء", "Action")), new(L("نوع السجل", "Record type")), new(L("السجل", "Record")),
            new(L("الحدث", "Event")), new(L("السبب", "Reason")), new(L("التغييرات", "Changes")), new(L("عنوان IP", "IP address")),
        ];
        if (technical) columns.AddRange([new(L("الإجراء (مخزّن)", "Action (stored)")), new(L("نوع السجل (مخزّن)", "Record type (stored)")), new(L("القيم المخزّنة", "Stored values"))]);
        var table = new ReportTable(L("سجل التدقيق", "Audit log"), [], columns,
            rows.Select(r =>
            {
                var changes = string.Join(" | ", r.Changes.Select(c => $"{c.Field}: {c.Old ?? "—"} {arrow} {c.New ?? "—"}")
                    .Concat((r.Lines ?? []).Select(l => $"{l.Account}: {L("مدين", "Debit")} {l.Debit ?? "—"} / {L("دائن", "Credit")} {l.Credit ?? "—"}")));
                IReadOnlyList<object?> cells = [r.OccurredAt, r.User ?? L("النظام", "System"), r.ActionLabel ?? r.Action, r.EntityLabel ?? r.EntityType, r.Record, r.Text, r.Reason, changes, r.IpAddress];
                return technical
                    ? (IReadOnlyList<object?>)[.. cells, r.Action, r.EntityType, string.Join(" | ", (r.Technical ?? []).Select(c => $"{c.Field}: {c.Old ?? "null"} → {c.New ?? "null"}"))]
                    : cells;
            }).ToList());
        return await exporter.ExportAsync(table, format, "audit-log", ct).ConfigureAwait(false);
    }

    private async Task<List<AuditLogEntry>> Load(IQueryable<AuditLog> q, bool technical, CancellationToken ct)
    {
        var rows = await q.Select(a => new { Row = new AuditRow(a.Id, a.OccurredAt, a.UserId, a.Action, a.EntityType, a.EntityId, a.OldValues, a.NewValues), a.IpAddress })
            .ToListAsync(ct).ConfigureAwait(false);
        var described = await describer.DescribeAsync(rows.Select(r => r.Row).ToList(), new DescribeOptions(CreateValues: true, LinkRecord: true), ct).ConfigureAwait(false);
        var erased = technical
            ? await ErasedRecords.FindAsync(db, rows.Where(r => r.Row.EntityType is not null && r.Row.EntityId is not null).Select(r => (r.Row.EntityType!, r.Row.EntityId!.Value)), ct)
                .ConfigureAwait(false)
            : [];
        return rows.Zip(described, (r, d) => new AuditLogEntry(DateTime.SpecifyKind(r.Row.OccurredAt, DateTimeKind.Utc), d.User, r.Row.Action, r.Row.EntityType, d.Record,
                d.Reason, d.Changes.Select(c => new AuditChange(c.Field, c.OldValue, c.NewValue)).ToList(), r.IpAddress, d.ActionLabel, d.EntityLabel, d.Predicate,
                d.Lines.Count == 0 ? null : d.Lines, d.Links.Count == 0 ? null : d.Links, technical ? Raw(r.Row, erased) : null))
            .ToList();
    }

    /// <summary>
    /// The stored values as they are (technical details, DT-188): column names and raw values, old → new. Personal data stays masked the
    /// same way as in the readable view (erased records, drivers without drv.view).
    /// </summary>
    private List<AuditChange> Raw(AuditRow row, HashSet<(string Type, long Id)> erased)
    {
        var oldValues = Parse(row.OldValues);
        var newValues = Parse(row.NewValues);
        var mask = row.EntityType == "TRN.Driver" && !ctx.HasPermission(PermissionKeys.DrvView);
        var isErased = row.EntityType is not null && row.EntityId is long id && erased.Contains((row.EntityType, id));
        return oldValues.Keys.Union(newValues.Keys).Where(k => !RawHidden.Contains(k))
            .Select(k => (mask && DriverPersonal.Contains(k)) || (isErased && ErasedRecords.IsPersonal(row.EntityType, k))
                ? new AuditChange(k, ErasedRecords.Masked, ErasedRecords.Masked)
                : new AuditChange(k, oldValues.GetValueOrDefault(k), newValues.GetValueOrDefault(k)))
            .ToList();
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
}
