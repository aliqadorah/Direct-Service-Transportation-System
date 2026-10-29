using DirectService.Shared.Contracts;

namespace DirectService.Shared.Infrastructure.Collaboration;

/// <summary>
/// Large exports in the background (FR-151): the request is checked with the caller's permissions, queued and answered at once (202); the
/// worker generates the file with the requester's permissions and the larger background limits, stores it under the requester's folder for
/// seven days and sends a notification with the download link; only that user can download it, in that company. Shared since Stage 2
/// session 10 (DT-334): report-builder and saved-report exports here, the module kinds (standard reports, audit log, company copy) through the
/// modules' <see cref="IExportJobProvider"/>s, the notifications through <see cref="IExportNotifier"/>.
/// </summary>
public sealed class ExportService(IRequestContext ctx, IExportQueue queue, IFileStore files, IReportBuilder builder, ISavedReportService saved,
    IReportExporter exporter, IEnumerable<IExportJobProvider> providers, IExportNotifier notifier, ExportOptions options)
{
    private readonly IReadOnlyList<IExportJobProvider> _providers = providers.ToList();

    public static readonly TimeSpan Keep = ExportFiles.Keep;

    /// <summary>Root folder of export files in the file store (pinned path format, platform design §e, E-33; defined in <see cref="ExportFiles"/>).</summary>
    public const string Root = ExportFiles.Root;

    internal static string Folder(long tenantId, long companyId, Guid userPid) => ExportFiles.Folder(tenantId, companyId, userPid);

    private IExportJobProvider ProviderOf(string kind) => _providers.First(p => p.Kinds.Contains(kind));

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
                if (!ctx.HasPermission(options.ReportPermission)) throw Fail.Forbidden();
                await builder.ValidateAsync(r.Source ?? throw Fail.Field("source", SharedErrorCodes.ReportSource),
                    r.Definition ?? throw Fail.Field("definition", SharedErrorCodes.ReportDefinition), ct).ConfigureAwait(false);
                break;
            case "saved":
                if (!ctx.HasPermission(options.ReportPermission)) throw Fail.Forbidden();
                await saved.GetAsync(r.Report ?? throw Fail.Field("report", SharedErrorCodes.ValidationRequired, new FieldRef("report")), ct).ConfigureAwait(false);
                break;
            default:
                await ProviderOf(kind).CheckAsync(kind, r, ct).ConfigureAwait(false);
                break;
        }
        if (r.Format is not null && r.Format.Trim().ToLowerInvariant() is not ("xlsx" or "excel" or "csv" or "pdf" or "html"))
            throw Fail.Field("format", SharedErrorCodes.ValidationFormat, new FieldRef("format"));
        var job = new ExportJob(Guid.NewGuid(), ctx.TenantId ?? throw Fail.Forbidden(), ctx.CompanyId ?? throw Fail.Forbidden(), ctx.UserId ?? throw Fail.Forbidden(),
            ctx.UserPublicId ?? throw Fail.Forbidden(), ctx.UserName, ctx.Language, r with { Kind = kind });
        if (!queue.Enqueue(job)) throw new AppException(SharedErrorCodes.RateLimited, 429);
        return new ExportJobResponse(job.Id.ToString("N"), "Queued");
    }

    /// <summary>builder and saved here; the other kinds (standard, audit-log, company) as the modules' providers list them.</summary>
    private string Kind(string? kind) => kind?.Trim().ToLowerInvariant() is { } k && (k is "builder" or "saved" || _providers.Any(p => p.Kinds.Contains(k)))
        ? k
        : throw Fail.Field("kind", SharedErrorCodes.ValidationFormat, new FieldRef("kind"));

    /// <summary>Runs a job inside a scope prepared for the requester (worker); stores the file and notifies.</summary>
    public async Task RunAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var r = job.Request;
        var file = r.Kind switch
        {
            "builder" => await exporter.ExportAsync(ReportTable.FromResult(await builder.RunAsync(r.Source, r.Definition, r.From, r.To, null, ct).ConfigureAwait(false)),
                r.Format, "report", ct).ConfigureAwait(false),
            "saved" => await saved.ExportAsync(r.Report!, r.From, r.To, r.Format, ct).ConfigureAwait(false),
            var kind => await ProviderOf(kind!).RunAsync(kind!, r, ct).ConfigureAwait(false),
        };
        var key = Folder(job.TenantId, job.CompanyId, job.UserPublicId) + job.Id.ToString("N") + "/" + file.FileName;
        await files.SaveAsync(key, file.Content, ct).ConfigureAwait(false);
        await notifier.ReadyAsync(job, file.FileName, "/exports/" + job.Id.ToString("N"), ct).ConfigureAwait(false);
    }

    /// <summary>Tells the requester that the export failed (worker).</summary>
    public Task FailedAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifier.FailedAsync(job, ct);
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
