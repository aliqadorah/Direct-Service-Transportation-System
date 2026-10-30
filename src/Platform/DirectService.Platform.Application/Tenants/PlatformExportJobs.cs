using DirectService.Platform.Application.Audit;
using DirectService.Platform.Contracts.Collaboration;
using DirectService.Platform.Contracts.Security;

namespace DirectService.Platform.Application.Tenants;

/// <summary>
/// The platform's background export kinds for the shared export engine: the audit log (<see cref="AuditLogService"/>) and the full copy
/// of the company (<see cref="CompanyExportService"/>), with the same checks and files as before (served by the module's
/// <c>TransportationExportJobs</c> until Stage 2 session 14).
/// </summary>
public sealed class PlatformExportJobs(IRequestContext ctx, AuditLogService audit, CompanyExportService company) : IExportJobProvider
{
    public IReadOnlyList<string> Kinds { get; } = ["audit-log", "company"];

    public Task CheckAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        if (kind == "company")
        {
            // The full copy of the company is the Owner's (5.6): tenant.manage, also while cancelled (retention).
            if (!ctx.HasPermission(PlatformPermissionKeys.TenantManage)) throw Fail.Forbidden();
        }
        else if (!ctx.HasPermission(PlatformPermissionKeys.SettingsManage) && !ctx.HasPermission(PlatformPermissionKeys.UsersManage))
        {
            throw Fail.Forbidden();
        }
        return Task.CompletedTask;
    }

    public async Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        return kind == "company"
            ? ctx.HasPermission(PlatformPermissionKeys.TenantManage) ? await company.ExportAsync(ct).ConfigureAwait(false) : throw Fail.Forbidden()
            : await audit.ExportAsync(r.Audit ?? new AuditLogQuery(null, null, null, r.From, r.To, null), r.Format, ct).ConfigureAwait(false);
    }
}
