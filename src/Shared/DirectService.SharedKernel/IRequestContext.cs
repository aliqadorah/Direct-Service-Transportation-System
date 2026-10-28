namespace DirectService.SharedKernel;

/// <summary>
/// Who is calling and in which tenant/company, resolved by the server on every request (ADR-04, SEC-05).
/// Never built from user input.
/// </summary>
public interface IRequestContext
{
    long? UserId { get; }
    Guid? UserPublicId { get; }
    string? UserName { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformStaff { get; }
    string Language { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
    long? SessionId { get; }

    long? TenantId { get; }
    string? TenantStatus { get; }

    long? CompanyId { get; }
    Guid? CompanyPublicId { get; }
    string? CompanyStatus { get; }
    string? RoleName { get; }
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Provider staff working inside an approved support window (read-only, D-13).</summary>
    bool IsSupportSession { get; }

    bool HasPermission(string key);

    /// <summary>True when writes are refused (suspended tenant, archived company, support session).</summary>
    bool IsReadOnly { get; }
}

/// <summary>Mutable implementation filled by the request pipeline.</summary>
public sealed class RequestContext : IRequestContext
{
    private static readonly HashSet<string> NoPermissions = [];

    public long? UserId { get; set; }
    public Guid? UserPublicId { get; set; }
    public string? UserName { get; set; }
    public bool IsAuthenticated => UserId is not null;
    public bool IsPlatformStaff { get; set; }
    public string Language { get; set; } = "ar";
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public long? SessionId { get; set; }
    public long? TenantId { get; set; }
    public string? TenantStatus { get; set; }
    public long? CompanyId { get; set; }
    public Guid? CompanyPublicId { get; set; }
    public string? CompanyStatus { get; set; }
    public string? RoleName { get; set; }
    public IReadOnlySet<string> Permissions { get; set; } = NoPermissions;
    public bool IsSupportSession { get; set; }

    public bool HasPermission(string key) => Permissions.Contains(key);

    public bool IsReadOnly =>
        IsSupportSession
        || TenantStatus is "Suspended" or "Cancelled" or "Deleted"
        || CompanyStatus == "Archived";
}
