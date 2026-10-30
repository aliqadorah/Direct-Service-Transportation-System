using DirectService.Transportation.Application.Reports;
using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Application.Collaboration;

/// <summary>
/// The background export kinds of this application for the shared export engine (Stage 2 session 10, DT-334): standard reports, the audit
/// log and the full copy of the company, with the same checks and files as before.
/// </summary>
public sealed class TransportationExportJobs(IRequestContext ctx, StandardReportService standard, AuditLogService audit, DirectService.Platform.Application.Tenants.CompanyExportService company)
    : IExportJobProvider
{
    public IReadOnlyList<string> Kinds { get; } = ["standard", "audit-log", "company"];

    public Task CheckAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        switch (kind)
        {
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
        return Task.CompletedTask;
    }

    public async Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        return kind switch
        {
            "company" => ctx.HasPermission(PermissionKeys.TenantManage) ? await company.ExportAsync(ct).ConfigureAwait(false) : throw Fail.Forbidden(),
            "standard" => await standard.ExportAsync(r.Key!, r.Query ?? new StandardReportQuery(r.From, r.To, null, null, null, null, null, null, null, null, null), r.Format, ct)
                .ConfigureAwait(false),
            _ => await audit.ExportAsync(r.Audit ?? new AuditLogQuery(null, null, null, r.From, r.To, null), r.Format, ct).ConfigureAwait(false),
        };
    }
}

/// <summary>The export notifications (CORE.ExportReady with the file name and link, CORE.ExportFailed) as in-app notifications.</summary>
public sealed class ExportNotifications(NotificationService notifications) : IExportNotifier
{
    public Task ReadyAsync(ExportJob job, string fileName, string link, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportReady, new Dictionary<string, string?> { ["file"] = fileName }, link, ct);
    }

    public Task FailedAsync(ExportJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        return notifications.NotifyAsync([job.UserId], job.CompanyId, NotificationService.ExportFailed, new Dictionary<string, string?>(), null, ct);
    }
}
