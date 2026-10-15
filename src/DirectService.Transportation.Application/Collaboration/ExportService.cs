using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Errors;
using DirectService.Transportation.Contracts.Reports;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>Stores generated files (exports) — provider-neutral; the development implementation writes to a local folder (DEF-01).</summary>
public interface IFileStore
{
    Task SaveAsync(string key, byte[] content, CancellationToken ct);

    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct);

    Task<byte[]?> ReadAsync(string key, CancellationToken ct);

    /// <summary>Removes files under the prefix older than the age; returns how many.</summary>
    Task<int> PurgeAsync(string prefix, TimeSpan age, CancellationToken ct);

    /// <summary>Total bytes of the files under the prefix (storage usage, P-07).</summary>
    Task<long> SizeAsync(string prefix, CancellationToken ct);
}

/// <summary>What a background export produces: report builder, saved report, standard report or audit log, in a format.</summary>
public sealed record ExportRequest(string? Kind, string? Source, ReportDefinition? Definition, string? Report, string? Key, StandardReportQuery? Query, AuditLogQuery? Audit,
    DateOnly? From, DateOnly? To, string? Format);

/// <summary>A queued export with the requester's identity (the worker runs it with the requester's permissions).</summary>
public sealed record ExportJob(Guid Id, long TenantId, long CompanyId, long UserId, Guid UserPublicId, string? UserName, string Language, ExportRequest Request);

/// <summary>The in-process queue of export jobs (a channel; the jobs table is not part of the design).</summary>
public interface IExportQueue
{
    bool Enqueue(ExportJob job);

    IAsyncEnumerable<ExportJob> ReadAllAsync(CancellationToken ct);

    bool TryRead(out ExportJob? job);
}

/// <summary>Limits of the current operation: background exports may return more rows than screens (FR-151).</summary>
public sealed class ExportLimits
{
    public bool Background { get; set; }
}

/// <summary>
/// Large exports in the background (FR-151): the request is checked with the caller's permissions, queued and answered at once (202); the
/// worker generates the file with the requester's permissions and the larger background limits, stores it under the requester's folder for
/// seven days and sends a notification with the download link; only that user can download it, in that company.
/// </summary>
public sealed class ExportService(IRequestContext ctx, IExportQueue queue, IFileStore files, ReportBuilderService builder, SavedReportService saved,
    StandardReportService standard, AuditLogService audit, ReportExporter exporter, NotificationService notifications, Platform.CompanyExportService company)
{
    public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

    internal static string Folder(long tenantId, long companyId, Guid userPid) => $"exports/{tenantId}/{companyId}/{userPid:N}/";

    public async Task<ExportJobResponse> EnqueueAsync(ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        // A support session is read-only and its grants are not the worker's to use: no exports from it (review F14).
        if (ctx.IsSupportSession) throw Fail.Forbidden();
        var kind = Kind(r.Kind);
        // Checked now with the caller's permissions, so a refused request fails at once (the worker checks again).
        switch (kind)
        {
            case "builder":
                if (!ctx.HasPermission(PermissionKeys.ReportsView)) throw Fail.Forbidden();
                await builder.ValidateAsync(r.Source ?? throw Fail.Field("source", ErrorCodes.ReportSource),
                    r.Definition ?? throw Fail.Field("definition", ErrorCodes.ReportDefinition), ct).ConfigureAwait(false);
                break;
            case "saved":
                if (!ctx.HasPermission(PermissionKeys.ReportsView)) throw Fail.Forbidden();
                await saved.GetAsync(r.Report ?? throw Fail.Field("report", ErrorCodes.ValidationRequired, "report"), ct).ConfigureAwait(false);
                break;
            case "standard":
                if (!standard.CatalogFor().Any(c => c.Key == r.Key && c.Path is null)) throw Fail.Forbidden();
                break;
            case "company":
                // The full copy of the company is the Owner's (5.6): tenant.manage, also while cancelled (retention).
                if (!ctx.HasPermission(PermissionKeys.TenantManage)) throw Fail.Forbidden();
                break;
            default:
                if (!ctx.HasPermission(PermissionKeys.SettingsManage) && !ctx.HasPermission(PermissionKeys.UsersManage)) throw Fail.Forbidden();
                break;
        }
        if (r.Format is not null && r.Format.Trim().ToLowerInvariant() is not ("xlsx" or "excel" or "csv" or "pdf" or "html"))
            throw Fail.Field("format", ErrorCodes.ValidationFormat, "format");
        var job = new ExportJob(Guid.NewGuid(), ctx.TenantId ?? throw Fail.Forbidden(), ctx.CompanyId ?? throw Fail.Forbidden(), ctx.UserId ?? throw Fail.Forbidden(),
            ctx.UserPublicId ?? throw Fail.Forbidden(), ctx.UserName, ctx.Language, r with { Kind = kind });
        if (!queue.Enqueue(job)) throw new AppException(ErrorCodes.RateLimited, 429);
        return new ExportJobResponse(job.Id.ToString("N"), "Queued");
    }

    private static string Kind(string? kind) => (kind?.Trim().ToLowerInvariant()) switch
    {
        "builder" or "saved" or "standard" or "audit-log" or "company" => kind.Trim().ToLowerInvariant(),
        _ => throw Fail.Field("kind", ErrorCodes.ValidationFormat, "kind"),
    };

    /// <summary>Runs a job inside a scope prepared for the requester (worker); stores the file and notifies.</summary>
    public async Task RunAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var r = job.Request;
        var file = r.Kind switch
        {
            "builder" => await exporter.ExportAsync(StandardReportService.ToTable(await builder.RunAsync(r.Source, r.Definition, r.From, r.To, null, ct).ConfigureAwait(false)),
                r.Format, "report", ct).ConfigureAwait(false),
            "saved" => await saved.ExportAsync(r.Report!, r.From, r.To, r.Format, ct).ConfigureAwait(false),
            "company" => ctx.HasPermission(PermissionKeys.TenantManage) ? await company.ExportAsync(ct).ConfigureAwait(false) : throw Fail.Forbidden(),
            "standard" => await standard.ExportAsync(r.Key!, r.Query ?? new StandardReportQuery(r.From, r.To, null, null, null, null, null, null, null, null, null), r.Format, ct)
                .ConfigureAwait(false),
            _ => await audit.ExportAsync(r.Audit ?? new AuditLogQuery(null, null, null, r.From, r.To, null), r.Format, ct).ConfigureAwait(false),
        };
        var key = Folder(job.TenantId, job.CompanyId, job.UserPublicId) + job.Id.ToString("N") + "/" + file.FileName;
        await files.SaveAsync(key, file.Content, ct).ConfigureAwait(false);
        await notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportReady, new Dictionary<string, string?> { ["file"] = file.FileName },
            "/exports/" + job.Id.ToString("N"), ct).ConfigureAwait(false);
    }

    /// <summary>Tells the requester that the export failed (worker).</summary>
    public Task FailedAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportFailed, new Dictionary<string, string?>(), null, ct);
    }

    /// <summary>The caller's finished export in the current company.</summary>
    public async Task<GeneratedFile> DownloadAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParseExact(id, "N", out var g)) throw Fail.NotFound();
        var folder = Folder(ctx.TenantId ?? throw Fail.NotFound(), ctx.CompanyId ?? throw Fail.NotFound(), ctx.UserPublicId ?? throw Fail.NotFound()) + g.ToString("N") + "/";
        var found = await files.ListAsync(folder, ct).ConfigureAwait(false);
        var key = found.Count > 0 ? found[0] : throw Fail.NotFound();
        var content = await files.ReadAsync(key, ct).ConfigureAwait(false) ?? throw Fail.NotFound();
        var name = key[(key.LastIndexOf('/') + 1)..];
        var type = Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".pdf" => "application/pdf",
            ".html" => "text/html; charset=utf-8",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };
        return new GeneratedFile(name, type, content);
    }
}
