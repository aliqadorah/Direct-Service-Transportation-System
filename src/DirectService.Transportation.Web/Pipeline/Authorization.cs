using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Application.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DirectService.Transportation.Web.Pipeline;

/// <summary>Permission requirement (ADR-06): company-level key, or a tenant-level key held in any active company.</summary>
public sealed record PermissionRequirement(string Key, bool TenantLevel) : IAuthorizationRequirement;

/// <summary>The caller must hold a role in the company of the route (any permission).</summary>
public sealed class CompanyMemberRequirement : IAuthorizationRequirement;

/// <summary>Endpoint metadata naming the permission it requires (checked by the endpoint coverage test).</summary>
public sealed record PermissionMetadata(string Key, bool TenantLevel);

/// <summary>Marks a write endpoint that stays allowed while the tenant/company is read-only (e.g. exports).</summary>
public sealed class AllowWhenReadOnlyMetadata;

/// <summary>Builds "perm:&lt;key&gt;" and "tenantperm:&lt;key&gt;" policies on demand.</summary>
internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Member = "member";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (policyName == Member)
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new CompanyMemberRequirement()).Build();
        if (policyName.StartsWith("perm:", StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[5..], false)).Build();
        if (policyName.StartsWith("tenantperm:", StringComparison.Ordinal))
            return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(policyName[11..], true)).Build();
        return await base.GetPolicyAsync(policyName).ConfigureAwait(false);
    }
}

internal sealed class PermissionHandler(IRequestContext ctx, AccessService access) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        foreach (var requirement in context.PendingRequirements.ToList())
        {
            switch (requirement)
            {
                case CompanyMemberRequirement when ctx.CompanyId is not null:
                    context.Succeed(requirement);
                    break;
                case PermissionRequirement { TenantLevel: false } p when ctx.CompanyId is not null && ctx.HasPermission(p.Key):
                    context.Succeed(requirement);
                    break;
                case PermissionRequirement { TenantLevel: true } p when ctx.UserId is long userId && ctx.TenantId is not null
                    && await access.HasTenantPermissionAsync(userId, p.Key, CancellationToken.None).ConfigureAwait(false):
                    context.Succeed(requirement);
                    break;
            }
        }
    }
}

internal static class EndpointConventions
{
    /// <summary>Requires a company-level permission key (PRD 3.4, namespaced per D-01).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization("perm:" + key).WithMetadata(new PermissionMetadata(key, false));

    /// <summary>Requires a tenant-level permission key (held in any active company of the tenant).</summary>
    public static TBuilder RequireTenantPermission<TBuilder>(this TBuilder builder, string key) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization("tenantperm:" + key).WithMetadata(new PermissionMetadata(key, true));

    public static TBuilder AllowWhenReadOnly<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AllowWhenReadOnlyMetadata());
}
