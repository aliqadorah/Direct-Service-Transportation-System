using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Contracts.Errors;
using Microsoft.AspNetCore.Antiforgery;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>
/// Refuses writes while the tenant is suspended/cancelled (BR-010), the company is archived (D-09) or the
/// caller is in a read-only support session (D-13). GET/HEAD are always allowed (BR-013).
/// </summary>
internal sealed class WriteGuardFilter(IRequestContext ctx) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && ctx.IsReadOnly
            && http.GetEndpoint()?.Metadata.GetMetadata<AllowWhenReadOnlyMetadata>() is null)
        {
            var code = ctx.IsSupportSession ? ErrorCodes.SupportReadOnly
                : ctx.CompanyStatus == "Archived" ? ErrorCodes.CompanyArchived
                : ErrorCodes.TenantReadOnly;
            throw new AppException(code, StatusCodes.Status403Forbidden);
        }
        return next(context);
    }
}

/// <summary>Antiforgery token (header X-XSRF-TOKEN) on every state-changing API call (SEC-12).</summary>
internal sealed class AntiforgeryFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext).ConfigureAwait(false);
            }
            catch (AntiforgeryValidationException)
            {
                throw new AppException(ErrorCodes.Antiforgery, StatusCodes.Status400BadRequest);
            }
        }
        return await next(context).ConfigureAwait(false);
    }
}
