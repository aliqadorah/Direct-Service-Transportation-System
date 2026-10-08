// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.JournalExportBatches â€” Company-scoped.
/// </summary>
public class JournalExportBatch : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Batch number.</summary>
    public string BatchNo { get; set; } = string.Empty;
    /// <summary>First period exported (YYYY-MM).</summary>
    public string PeriodFrom { get; set; } = string.Empty;
    /// <summary>Last period (YYYY-MM).</summary>
    public string PeriodTo { get; set; } = string.Empty;
    /// <summary>File format.</summary>
    public string Format { get; set; } = string.Empty;
    /// <summary>Entries exported.</summary>
    public int EntryCount { get; set; }
    /// <summary>Σ debits exported.</summary>
    public decimal TotalDebit { get; set; }
    /// <summary>Σ credits exported.</summary>
    public decimal TotalCredit { get; set; }
    /// <summary>User.</summary>
    public long ExportedBy { get; set; }
    /// <summary>Time.</summary>
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public User ExportedByUser { get; set; } = null!;
}
