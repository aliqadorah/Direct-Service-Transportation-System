using System.Globalization;
using DirectService.Transportation.Application.Abstractions;
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
    public async Task InvokeAsync(HttpContext http, RequestContext ctx, DataScope scope, IAppDbContext db)
    {
        ctx.IpAddress = http.Connection.RemoteIpAddress?.ToString();
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
            var lang = http.Request.GetTypedHeaders().AcceptLanguage
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
    public async Task InvokeAsync(HttpContext http, RequestContext ctx, DataScope scope, AccessService access, IUrlTokenService tokens)
    {
        if (http.GetRouteValue("company") is string token && ctx.UserId is long userId)
        {
            var publicId = tokens.Decode<Company>(token);
            var grants = publicId is null ? [] : await access.GrantsAsync(userId, http.RequestAborted).ConfigureAwait(false);
            var grant = grants.FirstOrDefault(g => g.CompanyPublicId == publicId);
            var isTenantManager = grants.Any(g => g.CompanyStatus == "Active" && g.Permissions.Contains(PermissionKeys.TenantManage));
            if (grant is null || (grant.CompanyStatus != "Active" && !isTenantManager))
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
        h.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        return next(http);
    }
}
