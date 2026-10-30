using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The background export kinds of this application for the shared export engine (Stage 2 session 10, DT-334): standard reports and the audit
/// log, with the same checks and files as before (the full copy of the company is the platform's <c>PlatformExportJobs</c> since Stage 2 session 14).
/// </summary>
public sealed class TransportationExportJobs(IRequestContext ctx, StandardReportService standard, AuditLogService audit)
    : IExportJobProvider
{
    public IReadOnlyList<string> Kinds { get; } = ["standard", "audit-log"];

    public Task CheckAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        switch (kind)
        {
            case "standard":
                if (!standard.CatalogFor().Any(c => c.Key == r.Key && c.Path is null)) throw Fail.Forbidden();
                break;
            default:
                if (!ctx.HasPermission(PermissionKeys.SettingsManage) && !ctx.HasPermission(PermissionKeys.UsersManage)) throw Fail.Forbidden();
                break;
        }
        return Task.CompletedTask;
    }

    public async Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        return kind switch
        {
            "standard" => await standard.ExportAsync(r.Key!, r.Query ?? new StandardReportQuery(r.From, r.To, null, null, null, null, null, null, null, null, null), r.Format, ct)
                .ConfigureAwait(false),
            _ => await audit.ExportAsync(r.Audit ?? new AuditLogQuery(null, null, null, r.From, r.To, null), r.Format, ct).ConfigureAwait(false),
        };
    }
}
