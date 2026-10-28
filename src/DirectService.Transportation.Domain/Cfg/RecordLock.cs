// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.RecordLocks â€” Company-scoped.
/// </summary>
[StoredName("CFG.RecordLock")]
[TokenLabel("RecordLock")]
public class RecordLock : AuditableEntity, ITenantScoped, ICompanyScoped, INotAudited
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced entity locked.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Locked row id.</summary>
    public long EntityId { get; set; }
    /// <summary>Lock holder.</summary>
    public long UserId { get; set; }
    /// <summary>When locked.</summary>
    public DateTime AcquiredAt { get; set; } = DateTime.UtcNow;
    /// <summary>Last activity; lock expires 90 s after it.</summary>
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
}
