using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Common;
using DirectService.Transportation.Application.Identity;
using DirectService.Transportation.Contracts.Security;
using DirectService.Transportation.Domain.Core;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// Builds the request context from the validated session (ADR-04): user, tenant and tenant status,
/// language; sets the data scope used by the EF Core filters. Never reads tenant or company from input.
/// </summary>
internal sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext ctx, DataScope scope, IAppDbContext db, PrintMode print)
    {
        ctx.IpAddress = http.Connection.RemoteIpAddress?.ToString();
        // On-screen print preview (SCR-09): the HTML rendered like the print, not logged as one. Only HTML: a PDF is always a logged print
        // (FR-058, FR-090), so a "preview" can never produce an unlogged original.
        print.Preview = http.Request.Method == HttpMethods.Get && string.Equals(http.Request.Query["preview"], "true", StringComparison.OrdinalIgnoreCase)
            && string.Equals(http.Request.Query["format"], "html", StringComparison.OrdinalIgnoreCase)
            && (http.Request.Path.Value?.EndsWith("/print", StringComparison.OrdinalIgnoreCase) ?? false);
        ctx.UserAgent = http.Request.Headers.UserAgent.ToString();
        if (http.Items[typeof(SessionPrincipal)] is SessionPrincipal p)
        {
            ctx.UserId = p.UserId;
            ctx.UserPublicId = p.UserPublicId;
            ctx.UserName = p.FullName;
            ctx.IsPlatformStaff = p.IsPlatformStaff;
            ctx.Language = p.LanguageCode;
            ctx.SessionId = p.SessionId;
            ctx.TenantId = p.TenantId;
            if (p.TenantId is not null)
            {
                ctx.TenantStatus = await db.Set<Tenant>().AsNoTracking().Where(t => t.Id == p.TenantId)
                    .Select(t => t.Status).SingleOrDefaultAsync(http.RequestAborted).ConfigureAwait(false);
            }
            scope.Set(p.TenantId, null, p.UserId, ctx.IpAddress);
        }
        else
        {
            // Anonymous: the account pages' language switch (?lang=ar|en), else the browser's preference.
            var query = http.Request.Query["lang"].ToString();
            var lang = query is "ar" or "en" ? query : http.Request.GetTypedHeaders().AcceptLanguage
                .OrderByDescending(l => l.Quality ?? 1)
                .Select(l => l.Value.Value?[..Math.Min(2, l.Value.Length)])
                .FirstOrDefault(l => l is "ar" or "en");
            ctx.Language = lang ?? "ar";
            scope.Set(null, null, null, ctx.IpAddress);
        }
        var culture = CultureInfo.GetCultureInfo(ctx.Language == "en" ? "en-GB" : "ar-JO");
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        await next(http).ConfigureAwait(false);
    }
}

/// <summary>
/// Resolves the {company} route token (ADR-04): the company must belong to the user's tenant and the user
/// must hold a role in it; otherwise 404 (an unknown and a foreign company look the same, SEC-22). Archived
/// companies are visible only to users with core.tenant.manage. Sets permissions for authorization.
/// </summary>
internal sealed class CompanyContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestContext ctx, DataScope scope, AccessService access, IUrlTokenService tokens, IAppDbContext db, TimeProvider clock)
    {
        // API requests only: the pages /c/{company}/… are the WebAssembly shell without data (no prerendering); it asks the API, which is
        // checked here. A page address of a closed support window or another tenant's company then shows the app's own message (with the
        // reason, DT-220) instead of a raw problem document.
        if (http.GetRouteValue("company") is string token && ctx.UserId is long userId && http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            var publicId = tokens.Decode<Company>(token);
            if (ctx.IsPlatformStaff && ctx.TenantId is null)
            {
                await SupportSessionAsync(http, ctx, scope, db, clock, publicId, userId).ConfigureAwait(false);
                return;
            }
            var grants = publicId is null ? [] : await access.GrantsAsync(userId, http.RequestAborted).ConfigureAwait(false);
            var grant = grants.FirstOrDefault(g => g.CompanyPublicId == publicId);
            var isTenantManager = grants.Any(g => g.CompanyStatus == "Active" && g.Permissions.Contains(PermissionKeys.TenantManage));
            // A cancelled tenant is open to its Owner only, for data export during retention (5.2).
            var closed = ctx.TenantStatus is "Cancelled" && grant?.Permissions.Contains(PermissionKeys.TenantManage) != true;
            if (grant is null || (grant.CompanyStatus != "Active" && !isTenantManager) || closed || ctx.TenantStatus is "Deleted")
            {
                await ProblemWriter.WriteAsync(http, Contracts.Errors.ErrorCodes.NotFound, StatusCodes.Status404NotFound, ctx.Language).ConfigureAwait(false);
                return;
            }
            ctx.CompanyId = grant.CompanyId;
            ctx.CompanyPublicId = grant.CompanyPublicId;
            ctx.CompanyStatus = grant.CompanyStatus;
            ctx.RoleName = grant.RoleName;
            ctx.Permissions = grant.Permissions;
            scope.Set(ctx.TenantId, grant.CompanyId, userId, ctx.IpAddress);
        }
        await next(http).ConfigureAwait(false);
    }

    /// <summary>
    /// Provider staff inside an approved, current support window of the company's tenant (BR-014, D-13): read-only view permissions; every request
    /// is written to the tenant's audit log. Without a window the company is not found (404).
    /// </summary>
    private async Task SupportSessionAsync(HttpContext http, RequestContext ctx, DataScope scope, IAppDbContext db, TimeProvider clock, Guid? companyPublicId, long userId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var company = companyPublicId is Guid pid
            ? await db.Set<Company>().AsNoTracking().IgnoreQueryFilters().Where(c => c.PublicId == pid && !c.IsDeleted && c.Status == "Active")
                .Select(c => new { c.Id, c.PublicId, c.TenantId, TenantStatus = db.Set<Tenant>().IgnoreQueryFilters().Where(t => t.Id == c.TenantId).Select(t => t.Status).FirstOrDefault() }).SingleOrDefaultAsync(http.RequestAborted).ConfigureAwait(false)
            : null;
        var window = company is null ? false : await db.Set<SupportAccessGrant>().AsNoTracking().IgnoreQueryFilters()
            .AnyAsync(g => g.TenantId == company.TenantId && g.RequestedBy == userId && !g.IsDeleted && g.Status == "Approved" && g.StartsAt <= now && g.EndsAt > now,
                http.RequestAborted).ConfigureAwait(false);
        // A window of this staff member for the tenant that is closed (ended by the subscriber, expired, not approved): a clear 403 with the
        // reason, when and by whom (owner testing 20 Oct 2026, DT-220). Without any grant for that tenant the company stays "not found".
        if (company is not null && !window
            && await Application.Platform.SupportAccessService.WhyClosedAsync(db, company.TenantId, userId, now, http.RequestAborted).ConfigureAwait(false) is { } closed)
        {
            var at = closed.At?.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
            await ProblemWriter.WriteAsync(http, closed.Code, StatusCodes.Status403Forbidden, ctx.Language, args: [at, closed.By],
                extensions: new Dictionary<string, object?> { ["reason"] = closed.Code, ["endedAt"] = closed.At, ["endedBy"] = closed.By }).ConfigureAwait(false);
            return;
        }
        if (company is null || !window)
        {
            await ProblemWriter.WriteAsync(http, Contracts.Errors.ErrorCodes.NotFound, StatusCodes.Status404NotFound, ctx.Language).ConfigureAwait(false);
            return;
        }
        ctx.TenantId = company.TenantId;
        ctx.TenantStatus = company.TenantStatus;
        ctx.CompanyId = company.Id;
        ctx.CompanyPublicId = company.PublicId;
        ctx.CompanyStatus = "Active";
        ctx.RoleName = "Support";
        ctx.IsSupportSession = true;
        ctx.Permissions = Application.Platform.SupportAccessService.SessionPermissions;
        scope.Set(company.TenantId, company.Id, userId, ctx.IpAddress);
        // The status written is the one the client gets: an exception is mapped the way the exception handler will map it (review F6).
        int? status = null;
        try
        {
            await next(http).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            status = ex switch
            {
                AppException a => a.Status,
                DbUpdateConcurrencyException => StatusCodes.Status409Conflict,
                OperationCanceledException => 499,
                _ => StatusCodes.Status500InternalServerError,
            };
            throw;
        }
        finally
        {
            try
            {
                // Only the audit row is saved here, never something the failed request left tracked.
                db.ChangeTracker.Clear();
                db.Set<AuditLog>().Add(new AuditLog
                {
                    TenantId = company.TenantId, CompanyId = company.Id, UserId = userId, OccurredAt = now, Action = "SupportRequest", EntityType = "SUPPORT",
                    NewValues = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                        { ["method"] = http.Request.Method, ["path"] = http.Request.Path.Value, ["status"] = status ?? http.Response.StatusCode }),
                    IpAddress = ctx.IpAddress,
                });
                await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception) when (status is not null)
            {
                // Never hide the request's own exception behind a failure to write its audit row.
            }
        }
    }
}

/// <summary>Security headers on every response (SEC-12).</summary>
internal sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var h = http.Response.Headers;
        h.XContentTypeOptions = "nosniff";
        h.XFrameOptions = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        // WebAssembly needs 'wasm-unsafe-eval' (no 'unsafe-eval'); inline style attributes are used by components (no inline scripts);
        // fonts and images are served by the application or embedded as data: (logos, QR codes).
        h.ContentSecurityPolicy = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; "
            + "font-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        // API answers carry one company's data: never kept by the browser or a proxy, so a later request (another company, another user
        // on the same computer) is always answered by the server (DT-187). An endpoint may still set its own policy.
        if (http.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) h.CacheControl = "no-store";
        return next(http);
    }
}
