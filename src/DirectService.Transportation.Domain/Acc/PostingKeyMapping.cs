// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.PostingKeyMappings â€” Company-scoped.
/// </summary>
[StoredName("ACC.PostingKeyMapping")]
[TokenLabel("PostingKeyMapping")]
public class PostingKeyMapping : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced posting key.</summary>
    public string PostingKey { get; set; } = string.Empty;
    /// <summary>Account the key posts to.</summary>
    public long AccountId { get; set; }
    public Account Account { get; set; } = null!;
}
