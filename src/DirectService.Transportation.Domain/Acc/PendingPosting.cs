// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.PendingPostings â€” Company-scoped.
/// </summary>
[StoredName("ACC.PendingPosting")]
[TokenLabel("PendingPosting")]
public class PendingPosting : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Module that owns the document waiting to post.</summary>
    public string SourceModule { get; set; } = string.Empty;
    /// <summary>Type of the document waiting to post.</summary>
    public string SourceType { get; set; } = string.Empty;
    /// <summary>Internal id of the document waiting to post (no FK).</summary>
    public long SourceId { get; set; }
    /// <summary>Failure reason.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Number of attempts made so far.</summary>
    public int Attempts { get; set; }
    /// <summary>Last retry time.</summary>
    public DateTime? LastAttemptAt { get; set; }
    /// <summary>When posted successfully.</summary>
    public DateTime? ResolvedAt { get; set; }
}
