using DirectService.Platform.Contracts.Security;

namespace DirectService.Platform.Application.Tenants;

/// <summary>
/// The platform's background export kind for the shared export engine: the full copy of the company (<see cref="CompanyExportService"/>),
/// with the same check and file as before (served by the module's <c>TransportationExportJobs</c> until Stage 2 session 14).
/// </summary>
public sealed class PlatformExportJobs(IRequestContext ctx, CompanyExportService company) : IExportJobProvider
{
    public IReadOnlyList<string> Kinds { get; } = ["company"];

    public Task CheckAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        // The full copy of the company is the Owner's (5.6): tenant.manage, also while cancelled (retention).
        if (!ctx.HasPermission(PlatformPermissionKeys.TenantManage)) throw Fail.Forbidden();
        return Task.CompletedTask;
    }

    public async Task<GeneratedFile> RunAsync(string kind, ExportRequest r, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        return ctx.HasPermission(PlatformPermissionKeys.TenantManage) ? await company.ExportAsync(ct).ConfigureAwait(false) : throw Fail.Forbidden();
    }
}
