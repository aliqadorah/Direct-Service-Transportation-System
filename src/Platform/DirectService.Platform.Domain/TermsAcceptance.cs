// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Platform.Domain;

/// <summary>
/// core.TermsAcceptances â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.TermsAcceptance")]
[TokenLabel("TermsAcceptance")]
public class TermsAcceptance : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>User who accepted.</summary>
    public long UserId { get; set; }
    /// <summary>Document accepted.</summary>
    public string DocumentType { get; set; } = string.Empty;
    /// <summary>Version accepted.</summary>
    public string DocumentVersion { get; set; } = string.Empty;
    /// <summary>When accepted.</summary>
    public DateTime AcceptedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
}
