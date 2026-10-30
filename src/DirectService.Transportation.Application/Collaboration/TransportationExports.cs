using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The background export kind of this application for the shared export engine (Stage 2 session 10, DT-334): standard reports, with the
/// same check and files as before (the audit log and the full copy of the company are the platform's <c>PlatformExportJobs</c> since Stage 2
/// session 14).
/// </summary>
public sealed class TransportationExportJobs(StandardReportService standard) : IExportJobProvider
{
    public IReadOnlyList<string> Kinds { get; } = ["standard"];

    public Task CheckAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (!standard.CatalogFor().Any(c => c.Key == r.Key && c.Path is null)) throw Fail.Forbidden();
        return Task.CompletedTask;
    }

    public async Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        return await standard.ExportAsync(r.Key!, r.Query ?? new StandardReportQuery(r.From, r.To, null, null, null, null, null, null, null, null, null), r.Format, ct)
            .ConfigureAwait(false);
    }
}
