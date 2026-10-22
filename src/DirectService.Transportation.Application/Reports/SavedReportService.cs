using System.Text.Json;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.MasterData;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Common;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Cfg;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Application.Reports;

/// <summary>
/// Saved reports (FR-145, FR-146) and dashboard pins (FR-140). A report is private to its owner or shared with the company; shared reports
/// are seen by users who can open their source (FR-147) and edited by the owner or an administrator (settings.manage, DT-116). Runs and
/// exports apply the viewer's permissions; every run is logged (its last run is shown). Pins belong to a user: a saved report the user can
/// see, a measure of its definition, warning and critical thresholds (value ≥ threshold), an order; the card shows the measure's grand
/// total and its state normal / warning / exceeded.
/// </summary>
public sealed class SavedReportService(IAppDbContext db, IRequestContext ctx, MasterDataSupport support, ReportBuilderService builder, ReportExporter exporter,
    AuditTrail audit, IChangeContext changes)
{
    public const string EntityType = "CFG.SavedReport";

    private long UserId => ctx.UserId ?? throw new InvalidOperationException("Saved reports need a user.");

    private bool En => ctx.Language == "en";

    private bool IsAdministrator => ctx.HasPermission(PermissionKeys.SettingsManage);

    private static ReportDefinition Parse(string json) => JsonSerializer.Deserialize<ReportDefinition>(json, ReportJson.Options) ?? throw new InvalidOperationException("Empty report definition.");

    /// <summary>Reports the caller may see: own ones and shared ones whose source the caller may use.</summary>
    private IQueryable<SavedReport> Visible()
    {
        var me = UserId;
        var sources = ReportSources.All.Where(builder.CanUse).Select(s => s.Key).ToList();
        return db.Set<SavedReport>().AsNoTracking().Where(r => sources.Contains(r.Source) && (r.OwnerUserId == me || r.IsShared));
    }

    private async Task<SavedReport> FindVisibleAsync(string token, CancellationToken ct)
    {
        var pid = support.Decode<SavedReport>(token) ?? throw Fail.NotFound();
        return await Visible().SingleOrDefaultAsync(r => r.PublicId == pid, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }

    public async Task<PagedResult<SavedReportResponse>> ListAsync(SavedReportListQuery q, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(q);
        var me = UserId;
        var query = Visible();
        query = (q.Scope?.Trim().ToLowerInvariant()) switch
        {
            null or "" or "all" => query,
            "mine" => query.Where(r => r.OwnerUserId == me),
            "shared" => query.Where(r => r.IsShared),
            "pinned" => query.Where(r => db.Set<DashboardPin>().Any(p => p.SavedReportId == r.Id && p.UserId == me)),
            _ => throw Fail.Field("scope", ErrorCodes.ValidationFormat, new FieldRef("scope")),
        };
        if (!string.IsNullOrWhiteSpace(q.Source)) query = query.Where(r => r.Source == q.Source);
        if (MasterDataSupport.SearchText(q.Q) is { } text) query = query.Where(r => r.Name.Contains(text));
        var paging = new PageRequest(q.Page, q.PageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var ids = await query.OrderBy(r => r.Name).ThenBy(r => r.Id).Skip(paging.Skip).Take(paging.SafePageSize).Select(r => r.Id).ToListAsync(ct).ConfigureAwait(false);
        return new PagedResult<SavedReportResponse>(await ResponsesAsync(ids, ct).ConfigureAwait(false), paging.SafePage, paging.SafePageSize, total);
    }

    public async Task<SavedReportResponse> GetAsync(string token, CancellationToken ct)
    {
        var r = await FindVisibleAsync(token, ct).ConfigureAwait(false);
        return (await ResponsesAsync([r.Id], ct).ConfigureAwait(false))[0];
    }

    public async Task<SavedReportResponse> CreateAsync(SavedReportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var (name, source, json) = await ValidateAsync(r, ct).ConfigureAwait(false);
        var report = new SavedReport { OwnerUserId = UserId, Name = name, Source = source, Definition = json, IsShared = r.IsShared };
        db.Set<SavedReport>().Add(report);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ResponsesAsync([report.Id], ct).ConfigureAwait(false))[0];
    }

    public async Task<SavedReportResponse> UpdateAsync(string token, SavedReportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var visible = await FindVisibleAsync(token, ct).ConfigureAwait(false);
        var report = await db.Set<SavedReport>().SingleAsync(x => x.Id == visible.Id, ct).ConfigureAwait(false);
        EnsureCanEdit(report);
        var (name, source, json) = await ValidateAsync(r, ct).ConfigureAwait(false);
        db.ExpectRowVersion(report, r.RowVersion);
        report.Name = name;
        report.Source = source;
        report.Definition = json;
        report.IsShared = r.IsShared;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await ResponsesAsync([report.Id], ct).ConfigureAwait(false))[0];
    }

    /// <summary>Soft delete with its pins (every user's).</summary>
    public async Task DeleteAsync(string token, CancellationToken ct)
    {
        var visible = await FindVisibleAsync(token, ct).ConfigureAwait(false);
        var report = await db.Set<SavedReport>().SingleAsync(x => x.Id == visible.Id, ct).ConfigureAwait(false);
        EnsureCanEdit(report);
        var why = En ? "Report deleted" : "حُذف التقرير";
        foreach (var pin in await db.Set<DashboardPin>().Where(p => p.SavedReportId == report.Id)
                     .ToListAsync(ct).ConfigureAwait(false))
        {
            pin.DeleteReason = why;
            db.Set<DashboardPin>().Remove(pin);
        }
        report.DeleteReason = why;
        db.Set<SavedReport>().Remove(report);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private void EnsureCanEdit(SavedReport r)
    {
        if (r.OwnerUserId != UserId && !(r.IsShared && IsAdministrator)) throw Fail.Forbidden();
    }

    private async Task<(string Name, string Source, string Json)> ValidateAsync(SavedReportRequest r, CancellationToken ct)
    {
        var name = TextNormalizer.CleanText(r.Name);
        var errors = new List<FieldError>();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150) errors.Add(new FieldError("name", ErrorCodes.ReportName, [150]));
        if (r.Definition is null) errors.Add(new FieldError("definition", ErrorCodes.ReportDefinition, []));
        if (errors.Count > 0) throw Fail.Fields(errors);
        var source = builder.Source(r.Source).Key;
        await builder.ValidateAsync(source, r.Definition!, ct).ConfigureAwait(false);
        return (name!, source, JsonSerializer.Serialize(r.Definition, ReportJson.Options));
    }

    // ------------------------------------------------------------ run and export (FR-146)

    public async Task<ReportResult> RunAsync(string token, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var r = await FindVisibleAsync(token, ct).ConfigureAwait(false);
        var result = await builder.RunAsync(r.Source, Parse(r.Definition), from, to, r.Name, ct).ConfigureAwait(false);
        audit.Record(EntityType, r.Id, "Run");
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return result;
    }

    public async Task<GeneratedFile> ExportAsync(string token, DateOnly? from, DateOnly? to, string? format, CancellationToken ct)
    {
        var result = await RunAsync(token, from, to, ct).ConfigureAwait(false);
        return await exporter.ExportAsync(StandardReportService.ToTable(result), format, "report", ct).ConfigureAwait(false);
    }

    private async Task<List<SavedReportResponse>> ResponsesAsync(IReadOnlyList<long> ids, CancellationToken ct)
    {
        var me = UserId;
        var rows = await db.Set<SavedReport>().AsNoTracking().Where(r => ids.Contains(r.Id))
            .Select(r => new
            {
                R = r, OwnerPid = r.OwnerUser.PublicId, Owner = r.OwnerUser.FullName,
                Pinned = db.Set<DashboardPin>().Any(p => p.SavedReportId == r.Id && p.UserId == me),
                LastRun = db.Set<AuditLog>().Where(a => a.EntityType == EntityType && a.EntityId == r.Id && a.Action == "Run").Max(a => (DateTime?)a.OccurredAt),
            }).ToListAsync(ct).ConfigureAwait(false);
        var byId = rows.ToDictionary(x => x.R.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).Select(x => new SavedReportResponse(support.Token<SavedReport>(x.R.PublicId), x.R.Name, x.R.Source,
            Parse(x.R.Definition), x.R.IsShared, new Ref(support.Token<User>(x.OwnerPid), x.Owner), x.R.OwnerUserId == me || (x.R.IsShared && IsAdministrator), x.Pinned,
            x.LastRun is DateTime t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null, DateTime.SpecifyKind(x.R.CreatedAt, DateTimeKind.Utc),
            Convert.ToBase64String(x.R.RowVersion))).ToList();
    }

    // ============================================================ pins (FR-140)

    public async Task<IReadOnlyList<DashboardPinResponse>> PinsAsync(CancellationToken ct)
    {
        var me = UserId;
        var pins = await db.Set<DashboardPin>().AsNoTracking().Where(p => p.UserId == me).OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => new { P = p, ReportPid = p.SavedReport.PublicId, p.SavedReport.Name, p.SavedReport.Source, p.SavedReport.Definition, p.SavedReport.OwnerUserId, p.SavedReport.IsShared })
            .ToListAsync(ct).ConfigureAwait(false);
        var result = new List<DashboardPinResponse>();
        foreach (var x in pins)
        {
            var index = int.TryParse(x.P.Measure, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : 0;
            var label = x.P.Measure;
            decimal? value = null;
            string? problem = null;
            // A report no longer visible (unshared, source permission removed) shows neither its name nor a value (FR-141, review B10-9).
            var visible = (x.OwnerUserId == me || x.IsShared) && builder.CanUse(ReportSources.Find(x.Source)!);
            try
            {
                if (!visible) throw Fail.Forbidden();
                var def = Parse(x.Definition);
                label = def.Measures is { } ms && index < ms.Count ? ms[index].Function + (ms[index].Field is { } f ? " " + f : string.Empty) : x.P.Measure;
                value = await builder.TotalAsync(x.Source, def, index, ct).ConfigureAwait(false);
            }
            catch (AppException ex)
            {
                problem = ErrorCatalog.Format(ex.Code, ctx.Language);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken pin never takes the dashboard down.
                problem = ErrorCatalog.Format(ErrorCodes.ServerError, ctx.Language);
            }
            var reportRef = visible ? new Ref(support.Token<SavedReport>(x.ReportPid), x.Name) : new Ref(string.Empty, string.Empty);
            result.Add(new DashboardPinResponse(support.Token<DashboardPin>(x.P.PublicId), reportRef, index, label, value,
                x.P.WarningThreshold, x.P.CriticalThreshold, problem is not null ? "unavailable" : State(value, x.P.WarningThreshold, x.P.CriticalThreshold), x.P.SortOrder, problem));
        }
        return result;
    }

    /// <summary>Exceeded at or above the critical threshold, warning at or above the warning threshold, otherwise normal.</summary>
    public static string State(decimal? value, decimal? warning, decimal? critical) =>
        value is not decimal v ? "normal" : critical is decimal c && v >= c ? "exceeded" : warning is decimal w && v >= w ? "warning" : "normal";

    public async Task<DashboardPinResponse> PinAsync(DashboardPinRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var report = await FindVisibleAsync(r.SavedReportId ?? throw Fail.Field("savedReportId", ErrorCodes.ValidationRequired, new FieldRef("savedReportId")), ct).ConfigureAwait(false);
        var measures = Parse(report.Definition).Measures?.Count ?? 0;
        var measure = r.Measure ?? 0;
        new Validator().Check(measure >= 0 && measure < measures, "measure", ErrorCodes.ReportMeasure)
            .Check(r.WarningThreshold is null || r.CriticalThreshold is null || r.WarningThreshold <= r.CriticalThreshold, "warningThreshold", ErrorCodes.ReportThresholds)
            .ThrowIfInvalid();
        var me = UserId;
        var order = (await db.Set<DashboardPin>().Where(p => p.UserId == me).MaxAsync(p => (int?)p.SortOrder, ct).ConfigureAwait(false) ?? 0) + 10;
        var pin = new DashboardPin
        {
            UserId = me, SavedReportId = report.Id, Measure = measure.ToString(System.Globalization.CultureInfo.InvariantCulture), WarningThreshold = r.WarningThreshold,
            CriticalThreshold = r.CriticalThreshold, SortOrder = order,
        };
        db.Set<DashboardPin>().Add(pin);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await PinsAsync(ct).ConfigureAwait(false)).Single(p => p.Id == support.Token<DashboardPin>(pin.PublicId));
    }

    public async Task<DashboardPinResponse> UpdatePinAsync(string token, DashboardPinRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var pin = await MyPinAsync(token, ct).ConfigureAwait(false);
        var report = await db.Set<SavedReport>().AsNoTracking().SingleAsync(x => x.Id == pin.SavedReportId, ct).ConfigureAwait(false);
        var measure = r.Measure ?? int.Parse(pin.Measure, System.Globalization.CultureInfo.InvariantCulture);
        new Validator().Check(measure >= 0 && measure < (Parse(report.Definition).Measures?.Count ?? 0), "measure", ErrorCodes.ReportMeasure)
            .Check(r.WarningThreshold is null || r.CriticalThreshold is null || r.WarningThreshold <= r.CriticalThreshold, "warningThreshold", ErrorCodes.ReportThresholds)
            .ThrowIfInvalid();
        pin.Measure = measure.ToString(System.Globalization.CultureInfo.InvariantCulture);
        pin.WarningThreshold = r.WarningThreshold;
        pin.CriticalThreshold = r.CriticalThreshold;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return (await PinsAsync(ct).ConfigureAwait(false)).Single(p => p.Id == token);
    }

    public async Task<IReadOnlyList<DashboardPinResponse>> ReorderPinsAsync(PinOrderRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        var me = UserId;
        var pins = await db.Set<DashboardPin>().Where(p => p.UserId == me).ToListAsync(ct).ConfigureAwait(false);
        var order = r.PinIds ?? [];
        for (var i = 0; i < order.Count; i++)
        {
            var pid = support.Decode<DashboardPin>(order[i]) ?? throw Fail.Field("pinIds", ErrorCodes.ValidationFormat, new FieldRef("pinIds"));
            var pin = pins.SingleOrDefault(p => p.PublicId == pid) ?? throw Fail.Field("pinIds", ErrorCodes.ValidationFormat, new FieldRef("pinIds"));
            pin.SortOrder = (i + 1) * 10;
            changes.SetAction(pin, "Reorder");
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await PinsAsync(ct).ConfigureAwait(false);
    }

    public async Task UnpinAsync(string token, CancellationToken ct)
    {
        var pin = await MyPinAsync(token, ct).ConfigureAwait(false);
        pin.DeleteReason = En ? "Removed from the dashboard" : "أُزيل من اللوحة";
        db.Set<DashboardPin>().Remove(pin);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task<DashboardPin> MyPinAsync(string token, CancellationToken ct)
    {
        var pid = support.Decode<DashboardPin>(token) ?? throw Fail.NotFound();
        var me = UserId;
        return await db.Set<DashboardPin>().SingleOrDefaultAsync(p => p.PublicId == pid && p.UserId == me, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
    }
}
