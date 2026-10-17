namespace DirectService.Transportation.Domain.Acc;

/// <summary>
/// acc.JournalTemplates — Company-scoped (revision 6 part B, owner change request 15 Oct 2026). A journal entry template (نموذج قيد): a name,
/// the entry description and Reference, and lines (account, cost centre, line description, optional amounts). "Use template" fills a new
/// entry, which is then edited and saved as a draft or posted as usual; a template never posts anything (no recurring posting).
/// </summary>
public class JournalTemplate : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Template name, unique per company among templates not deleted.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Entry description (البيان) the template fills in.</summary>
    public string? Description { get; set; }
    /// <summary>Entry Reference (المرجع) the template fills in.</summary>
    public string? Reference { get; set; }
    /// <summary>The lines (JSON array: account id, cost centre id, line description, debit, credit — amounts optional; internal ids).</summary>
    public string Lines { get; set; } = "[]";
}
