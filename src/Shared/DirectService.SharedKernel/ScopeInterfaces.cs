namespace DirectService.SharedKernel;

/// <summary>Row that can be soft-deleted (BR-041). Never hard-deleted.</summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    long? DeletedBy { get; set; }
    string? DeleteReason { get; set; }
}

/// <summary>Row owned by one tenant (TenantId NOT NULL).</summary>
public interface ITenantScoped
{
    long TenantId { get; set; }
}

/// <summary>Row owned by a tenant, or by the platform when TenantId is NULL (users of provider staff, platform audit rows).</summary>
public interface INullableTenantScoped
{
    long? TenantId { get; set; }
}

/// <summary>Row owned by one company of a tenant (CompanyId NOT NULL).</summary>
public interface ICompanyScoped
{
    long CompanyId { get; set; }
}

/// <summary>Append-only log (core.AuditLogs, core.MaintenanceLogs, trn.TRN_RateHistory): rows are never updated or deleted, and not audited.</summary>
public interface IAppendOnly;

/// <summary>High-frequency technical row whose changes are not business events (sessions, record locks, notifications, the email outbox): never audited.</summary>
public interface INotAudited;

/// <summary>The tenant row itself: its audit rows belong to the tenant <see cref="Id"/>.</summary>
public interface ITenantRoot
{
    long Id { get; }
}

/// <summary>The company row itself: its audit rows belong to the company <see cref="Id"/>.</summary>
public interface ICompanyRoot
{
    long Id { get; }
}

/// <summary>
/// The audit log row (core.AuditLogs) the shared audit interceptor writes, one per changed row (FR-188). Exactly one entity of the model
/// implements it.
/// </summary>
public interface IAuditRow
{
    long? TenantId { get; set; }
    long? CompanyId { get; set; }
    long? UserId { get; set; }
    DateTime OccurredAt { get; set; }
    string Action { get; set; }
    string? EntityType { get; set; }
    long? EntityId { get; set; }
    string? OldValues { get; set; }
    string? NewValues { get; set; }
    string? IpAddress { get; set; }
}

/// <summary>Master-data record that is archived instead of deleted once referenced (Status 'Active' | 'Archived', FR-162).</summary>
public interface IArchivable
{
    string Status { get; set; }
}
