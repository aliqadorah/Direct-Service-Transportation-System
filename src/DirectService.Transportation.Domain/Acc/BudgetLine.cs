namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.BudgetLines — Company-scoped (revision 6 part B). The budgeted amount of one revenue or expense account (a posting account or a parent
/// account), optionally for one cost centre, per month of the budget's year. Amounts are in the natural direction of the account (revenue
/// credit − debit, expense debit − credit). One line per account and cost centre; a line on a parent account excludes lines on its
/// sub-accounts for the same cost centre (no double counting).
/// </summary>
[StoredName("ACC.BudgetLine")]
[TokenLabel("BudgetLine")]
public class BudgetLine : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>The budget.</summary>
    public long BudgetId { get; set; }
    /// <summary>Revenue or expense account (posting account or parent account).</summary>
    public long AccountId { get; set; }
    /// <summary>Optional cost centre.</summary>
    public long? CostCentreId { get; set; }
    /// <summary>January amount.</summary>
    public decimal Month01 { get; set; }
    /// <summary>February amount.</summary>
    public decimal Month02 { get; set; }
    /// <summary>March amount.</summary>
    public decimal Month03 { get; set; }
    /// <summary>April amount.</summary>
    public decimal Month04 { get; set; }
    /// <summary>May amount.</summary>
    public decimal Month05 { get; set; }
    /// <summary>June amount.</summary>
    public decimal Month06 { get; set; }
    /// <summary>July amount.</summary>
    public decimal Month07 { get; set; }
    /// <summary>August amount.</summary>
    public decimal Month08 { get; set; }
    /// <summary>September amount.</summary>
    public decimal Month09 { get; set; }
    /// <summary>October amount.</summary>
    public decimal Month10 { get; set; }
    /// <summary>November amount.</summary>
    public decimal Month11 { get; set; }
    /// <summary>December amount.</summary>
    public decimal Month12 { get; set; }
    /// <summary>Yearly total (Σ months), stored by the application.</summary>
    public decimal Total { get; set; }
    public Budget Budget { get; set; } = null!;
    public Account Account { get; set; } = null!;

    /// <summary>The twelve monthly amounts, January first.</summary>
    public decimal[] Months() => [Month01, Month02, Month03, Month04, Month05, Month06, Month07, Month08, Month09, Month10, Month11, Month12];

    /// <summary>Sets the twelve monthly amounts and the total.</summary>
    public void SetMonths(IReadOnlyList<decimal> m)
    {
        ArgumentNullException.ThrowIfNull(m);
        if (m.Count != 12) throw new ArgumentException("Twelve months expected.", nameof(m));
        (Month01, Month02, Month03, Month04, Month05, Month06) = (m[0], m[1], m[2], m[3], m[4], m[5]);
        (Month07, Month08, Month09, Month10, Month11, Month12) = (m[6], m[7], m[8], m[9], m[10], m[11]);
        Total = m.Sum();
    }
}
