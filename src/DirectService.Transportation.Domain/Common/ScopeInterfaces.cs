namespace DirectService.Transportation.Domain.Common;

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

/// <summary>Append-only log (core.AuditLogs, core.MaintenanceLogs): rows are never updated or deleted.</summary>
public interface IAppendOnly;

/// <summary>Master-data record that is archived instead of deleted once referenced (Status 'Active' | 'Archived', FR-162).</summary>
public interface IArchivable
{
    string Status { get; set; }
}

/// <summary>Loading or unloading site (two separate lists with the same fields, FR-160).</summary>
public interface ISite : IArchivable
{
    string Name { get; set; }
    string? Area { get; set; }
}

/// <summary>A numbered cash or bank document with a status and its journal entry (receipts, advances, settlements, partner payments).</summary>
public interface ICashDocument
{
    long Id { get; }
    Guid PublicId { get; }
    string Number { get; set; }
    string Status { get; set; }
    long? JournalEntryId { get; set; }
}
