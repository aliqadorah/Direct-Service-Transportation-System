using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Collaboration;
using DirectService.Transportation.Contracts.Accounting;
using DirectService.Transportation.Contracts.Collaboration;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Web.Pipeline;
using Microsoft.AspNetCore.Http.HttpResults;

namespace DirectService.Transportation.Web.Endpoints;

/// <summary>
/// Collaboration (B11, M13): global search (members; each record type by its own permission), the caller's notifications, the company audit
/// log (settings.manage or users.manage, read-only) and background exports (queued with the caller's permissions, downloadable only by them).
/// </summary>
internal static class CollaborationEndpoints
{
    public static void MapCollaborationEndpoints(this RouteGroupBuilder company)
    {
        company.MapGet("/search", async (string? q, GlobalSearchService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.SearchAsync(q, ct).ConfigureAwait(false))).WithTags("Search").RequireMember();

        var n = company.MapGroup("/notifications").WithTags("Notifications");
        n.MapGet("/", async (bool? unread, NotificationService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(unread == true, ct).ConfigureAwait(false))).RequireMember();
        n.MapPost("/{notification}/read", async (string notification, NotificationService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(notification, ct).ConfigureAwait(false);
            return TypedResults.NoContent();
        }).RequireMember().AllowWhenReadOnly();
        n.MapPost("/read-all", async (NotificationService svc, CancellationToken ct) =>
            TypedResults.Ok(new { marked = await svc.MarkAllReadAsync(ct).ConfigureAwait(false) })).RequireMember().AllowWhenReadOnly();

        var a = company.MapGroup("/audit-log").WithTags("Audit log");
        string[] auditors = [PermissionKeys.SettingsManage, PermissionKeys.UsersManage];
        a.MapGet("/", async ([AsParameters] AuditLogQuery q, AuditLogService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.ListAsync(q, ct).ConfigureAwait(false))).RequireAnyPermission(auditors);
        a.MapGet("/facets", async (AuditLogService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.FacetsAsync(ct).ConfigureAwait(false))).RequireAnyPermission(auditors);
        a.MapGet("/export", async ([AsParameters] AuditLogQuery q, string? format, AuditLogService svc, CancellationToken ct) =>
            File(await svc.ExportAsync(q, format, ct).ConfigureAwait(false))).RequireAnyPermission(auditors);

        var e = company.MapGroup("/exports").WithTags("Background exports");
        e.MapPost("/", async (ExportRequest req, ExportService svc, CancellationToken ct) =>
            TypedResults.Accepted((string?)null, await svc.EnqueueAsync(req, ct).ConfigureAwait(false))).RequireMember().AllowWhenReadOnly();   // exports stay available (BR-013)
        e.MapGet("/{export}", async (string export, ExportService svc, CancellationToken ct) =>
            File(await svc.DownloadAsync(export, ct).ConfigureAwait(false))).RequireMember();
    }

    private static FileContentHttpResult File(GeneratedFile f) => TypedResults.File(f.Content, f.ContentType, f.FileName);
}

/// <summary>
/// FR-188 "export": every file returned by an export route of a company is recorded in the audit log (route and file name); prints are
/// logged by their services (FR-058, FR-090).
/// </summary>
internal sealed class ExportAuditFilter(IAppDbContext db, IRequestContext ctx) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var result = await next(context).ConfigureAwait(false);
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        if (result is FileContentHttpResult file && (path.EndsWith("/export", StringComparison.OrdinalIgnoreCase) || path.Contains("/exports", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/backup", StringComparison.OrdinalIgnoreCase)) && ctx.CompanyId is not null)
        {
            var route = (context.HttpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "export";
            db.Set<AuditLog>().Add(new AuditLog
            {
                TenantId = ctx.TenantId, CompanyId = ctx.CompanyId, UserId = ctx.UserId, OccurredAt = DateTime.UtcNow, Action = "Export", EntityType = "EXPORT",
                NewValues = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string?> { ["route"] = route, ["file"] = file.FileDownloadName }),
                IpAddress = ctx.IpAddress,
            });
            await db.SaveChangesAsync(context.HttpContext.RequestAborted).ConfigureAwait(false);
        }
        return result;
    }
}
