// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Tariffs â€” Company-scoped.
/// </summary>
public class Tariff : AuditableEntity, ITenantScoped, ICompanyScoped, IArchivable
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Whose price this line sets.</summary>
    public string PartyType { get; set; } = string.Empty;
    /// <summary>Customer the price applies to; NULL = all customers.</summary>
    public long? CustomerId { get; set; }
    /// <summary>Partner the rate applies to; NULL = all partners.</summary>
    public long? PartnerId { get; set; }
    /// <summary>Material; NULL = all materials.</summary>
    public long? MaterialId { get; set; }
    /// <summary>Loading site; NULL = all.</summary>
    public long? LoadingSiteId { get; set; }
    /// <summary>Unloading site; NULL = all.</summary>
    public long? UnloadingSiteId { get; set; }
    /// <summary>Price per ton.</summary>
    public decimal Price { get; set; }
    /// <summary>First valid date; NULL = from the beginning.</summary>
    public DateOnly? ValidFrom { get; set; }
    /// <summary>Last valid date; NULL = open-ended.</summary>
    public DateOnly? ValidTo { get; set; }
    /// <summary>Score used to pick the most specific line: party 8 + material 4 + loading site 2 + unloading site 1. Persisted computed column.</summary>
    public byte Specificity { get; private set; }
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Record status.</summary>
    public string Status { get; set; } = "Active";
    public Customer? Customer { get; set; }
    public Partner? Partner { get; set; }
    public Material? Material { get; set; }
    public LoadingSite? LoadingSite { get; set; }
    public UnloadingSite? UnloadingSite { get; set; }
}
