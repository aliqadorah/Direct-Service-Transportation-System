namespace DirectService.SharedKernel;

/// <summary>
/// The standard columns every table carries (Database design §4), except the append-only logs.
/// </summary>
public abstract class AuditableEntity : ISoftDeletable
{
    /// <summary>Internal surrogate key. Never shown in URLs, screens or exports.</summary>
    public long Id { get; set; }

    /// <summary>Random public identifier; hashed into every URL instead of Id (SEC-22).</summary>
    public Guid PublicId { get; set; } = Guid.NewGuid();

    /// <summary>UTC time the row was created.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>User who created the row; NULL only for rows created by a system job.</summary>
    public long? CreatedBy { get; set; }

    /// <summary>UTC time of the last update.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>User who last updated the row.</summary>
    public long? UpdatedBy { get; set; }

    /// <summary>Soft-delete flag (BR-041).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC time of the soft delete.</summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>User who soft-deleted the row.</summary>
    public long? DeletedBy { get; set; }

    /// <summary>Why the row was deleted.</summary>
    public string? DeleteReason { get; set; }

    /// <summary>Concurrency token; a stale update is refused.</summary>
    public byte[] RowVersion { get; set; } = [];
}
