// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Accounting.Domain.Acc;

/// <summary>
/// acc.EntryLines â€” Company-scoped.
/// </summary>
[StoredName("ACC.EntryLine")]
[TokenLabel("EntryLine")]
public class EntryLine : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Entry.</summary>
    public long JournalEntryId { get; set; }
    /// <summary>Line order within the entry.</summary>
    public int LineNo { get; set; }
    /// <summary>Account posted.</summary>
    public long AccountId { get; set; }
    /// <summary>Debit amount.</summary>
    public decimal Debit { get; set; }
    /// <summary>Credit amount.</summary>
    public decimal Credit { get; set; }
    /// <summary>Cost centre.</summary>
    public long? CostCentreId { get; set; }
    /// <summary>Module that owns the party.</summary>
    public string? PartyModule { get; set; }
    /// <summary>Kind of party for control-account lines.</summary>
    public string? PartyType { get; set; }
    /// <summary>Internal id of the party (no FK).</summary>
    public long? PartyId { get; set; }
    /// <summary>Free-text note for this line.</summary>
    public string? Note { get; set; }
    /// <summary>Line reference (مرجع السطر, DT-254); a new line starts with the entry's Reference.</summary>
    public string? Reference { get; set; }
    /// <summary>Tax code of the line (DT-255); on an automatic tax line, the code it was calculated with.</summary>
    public long? TaxCodeId { get; set; }
    /// <summary>The code's rate (percent) when the line was posted.</summary>
    public decimal? TaxRate { get; set; }
    /// <summary>Tax on the line's amount (the amount is before tax); NULL on an automatic tax line.</summary>
    public decimal? TaxAmount { get; set; }
    /// <summary>Tax account the tax was posted to (rates above 0 %); NULL on an automatic tax line.</summary>
    public long? TaxAccountId { get; set; }
    /// <summary>Automatic tax line: the line whose tax it carries (cannot be edited on its own).</summary>
    public long? TaxForLineId { get; set; }
    public JournalEntry JournalEntry { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public CostCentre? CostCentre { get; set; }
    public TaxCode? TaxCode { get; set; }
    public Account? TaxAccount { get; set; }
    public EntryLine? TaxForLine { get; set; }
}
