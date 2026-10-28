// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Cfg;

/// <summary>
/// cfg.CustomFieldDefinitions â€” Company-scoped.
/// </summary>
[StoredName("CFG.CustomFieldDefinition")]
[TokenLabel("CustomFieldDefinition")]
public class CustomFieldDefinition : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Namespaced entity.</summary>
    public string EntityType { get; set; } = string.Empty;
    /// <summary>Key used in CustomValues JSON.</summary>
    public string FieldKey { get; set; } = string.Empty;
    /// <summary>Label in Arabic.</summary>
    public string LabelAr { get; set; } = string.Empty;
    /// <summary>Label in English.</summary>
    public string? LabelEn { get; set; }
    /// <summary>Field type.</summary>
    public string FieldType { get; set; } = string.Empty;
    /// <summary>Choices for List fields (JSON array).</summary>
    public string? Options { get; set; }
    /// <summary>Default value.</summary>
    public string? DefaultValue { get; set; }
    /// <summary>Help shown under the field.</summary>
    public string? HelpText { get; set; }
    /// <summary>Value required on save.</summary>
    public bool IsRequired { get; set; }
    /// <summary>Show as a list column.</summary>
    public bool ShowAsColumn { get; set; }
    /// <summary>Display order (ascending).</summary>
    public int SortOrder { get; set; }
    /// <summary>Hidden without deleting.</summary>
    public bool IsHidden { get; set; }
}
