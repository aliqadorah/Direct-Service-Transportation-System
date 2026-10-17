namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.Budgets — Company-scoped (revision 6 part B, owner change request 15 Oct 2026). A budget (الموازنة) of one fiscal year (= calendar year):
/// a name, a status (Draft → Approved, back to Draft) and its lines. Only one Approved budget per company and year
/// (<c>UX_Budgets_CompanyId_FiscalYear_Approved</c>); it is the one compared with the actuals (Budget vs Actual, accounting dashboard).
/// </summary>
public class Budget : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Fiscal year (calendar year, 2000–2100).</summary>
    public int FiscalYear { get; set; }
    /// <summary>Budget name, unique per company and year among budgets not deleted.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Draft or Approved.</summary>
    public string Status { get; set; } = "Draft";
    /// <summary>When it was approved (NULL while Draft).</summary>
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Who approved it (NULL while Draft).</summary>
    public long? ApprovedBy { get; set; }
    public ICollection<BudgetLine> Lines { get; set; } = new List<BudgetLine>();
}
