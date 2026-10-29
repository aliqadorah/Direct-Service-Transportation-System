// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Platform.Domain;

/// <summary>
/// core.Notifications â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.Notification")]
[TokenLabel("Notification")]
public class Notification : AuditableEntity, ITenantScoped, INotAudited
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Recipient.</summary>
    public long UserId { get; set; }
    /// <summary>Company the notification relates to.</summary>
    public long? CompanyId { get; set; }
    /// <summary>Namespaced type, e.g. TRN.DriverDocumentExpiry.</summary>
    public string NotificationType { get; set; } = string.Empty;
    /// <summary>Text shown.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>In-app link to the record; contains only hashed PublicIds.</summary>
    public string? Link { get; set; }
    /// <summary>When read; NULL = unread.</summary>
    public DateTime? ReadAt { get; set; }
    public User User { get; set; } = null!;
    public Company? Company { get; set; }
}
