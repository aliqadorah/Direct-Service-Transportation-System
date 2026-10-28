// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.SharedKernel;

/// <summary>
/// cfg.NumberSequences â€” Company-scoped.
/// </summary>
[StoredName("CFG.NumberSequence")]
[TokenLabel("NumberSequence")]
public class NumberSequence : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced document type.</summary>
    public string DocumentType { get; set; } = string.Empty;
    /// <summary>Number prefix, e.g. INV-.</summary>
    public string Prefix { get; set; } = string.Empty;
    /// <summary>Zero-padded width.</summary>
    public byte Digits { get; set; } = 5;
    /// <summary>Next number to issue.</summary>
    public long NextNumber { get; set; } = 1;
}
