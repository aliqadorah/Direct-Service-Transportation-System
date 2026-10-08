namespace DirectService.Transportation.Application.Abstractions;

/// <summary>
/// Who is acting and in which tenant/company. Drives the EF Core query filters and the
/// stamping/audit interceptor (ADR-04). Set by the server from the signed-in context —
/// never from user input.
/// </summary>
public interface IDataScope
{
    /// <summary>Tenant of the current request; null for anonymous requests and provider staff.</summary>
    long? TenantId { get; }

    /// <summary>Selected company; null for tenant-level and platform operations.</summary>
    long? CompanyId { get; }

    /// <summary>Acting user; null for system jobs.</summary>
    long? UserId { get; }

    /// <summary>Client IP address recorded in the audit log.</summary>
    string? IpAddress { get; }

    /// <summary>
    /// Temporarily act in another tenant/company (e.g. sign-up creating the first company, a
    /// background job processing a company's queue). Dispose to restore the previous scope.
    /// </summary>
    IDisposable Use(long? tenantId, long? companyId, long? userId);
}

/// <summary>
/// Per-operation information the audit interceptor cannot infer: the business action name
/// (Void, Approve, Archive…) and the reason for a soft delete.
/// </summary>
public interface IChangeContext
{
    /// <summary>Records the audit action for an entity changed in the current unit of work.</summary>
    void SetAction(object entity, string action);

    /// <summary>The action previously set for the entity, if any.</summary>
    string? GetAction(object entity);

    /// <summary>Clears per-save state after a successful save.</summary>
    void Reset();
}
