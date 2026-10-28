// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.SharedKernel;

/// <summary>
/// cfg.ImportBatches â€” Company-scoped.
/// </summary>
[StoredName("CFG.ImportBatch")]
[TokenLabel("ImportBatch")]
public class ImportBatch : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced entity imported.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Uploaded file name.</summary>
    public string FileName { get; set; } = string.Empty;
    /// <summary>Strict = reject any bad row; Tolerant = apply defaults and flag Needs review.</summary>
    public string Mode { get; set; } = "Strict";
    /// <summary>Rows in the file.</summary>
    public int TotalRows { get; set; }
    /// <summary>Rows imported.</summary>
    public int ImportedRows { get; set; }
    /// <summary>Rows rejected.</summary>
    public int RejectedRows { get; set; }
    /// <summary>User.</summary>
    public long ImportedBy { get; set; }
    /// <summary>Time.</summary>
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
