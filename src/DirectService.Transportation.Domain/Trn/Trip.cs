// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Trn;

/// <summary>
/// trn.TRN_Trips â€” Company-scoped.
/// </summary>
public class Trip : AuditableEntity, ITenantScoped, ICompanyScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Owning company — company isolation filter on every query.</summary>
    public long CompanyId { get; set; }
    /// <summary>Weighbridge / delivery receipt number as entered.</summary>
    public string ReceiptNo { get; set; } = string.Empty;
    /// <summary>ReceiptNo normalised (upper-case, spaces removed) for uniqueness.</summary>
    public string ReceiptNoKey { get; set; } = string.Empty;
    /// <summary>Trip date.</summary>
    public DateOnly TripDate { get; set; }
    /// <summary>Loading date; its month selects the monthly driver rate.</summary>
    public DateOnly LoadingDate { get; set; }
    /// <summary>Unloading date.</summary>
    public DateOnly? UnloadingDate { get; set; }
    /// <summary>Driver (and truck) that carried the load.</summary>
    public long DriverId { get; set; }
    /// <summary>Customer billed.</summary>
    public long CustomerId { get; set; }
    /// <summary>Loading site.</summary>
    public long LoadingSiteId { get; set; }
    /// <summary>Unloading site.</summary>
    public long UnloadingSiteId { get; set; }
    /// <summary>Material carried.</summary>
    public long MaterialId { get; set; }
    /// <summary>Partner company for sub-contracted trips (copied from the driver).</summary>
    public long? PartnerId { get; set; }
    /// <summary>Driver's plate number copied at save, so later truck changes do not alter history.</summary>
    public string TruckPlateAtSave { get; set; } = string.Empty;
    /// <summary>Fleet class at save (from the driver's ownership type).</summary>
    public string FleetClass { get; set; } = string.Empty;
    /// <summary>Weight loaded, tons.</summary>
    public decimal LoadedWeight { get; set; }
    /// <summary>Weight delivered, tons.</summary>
    public decimal? UnloadedWeight { get; set; }
    /// <summary>Quantity billed to the customer (unloaded weight if "Bill on unloaded weight" is on and recorded, else loaded).</summary>
    public decimal BillingQty { get; set; }
    /// <summary>Quantity the driver/partner is paid on (loaded if "Pay driver on loaded weight" is on, else unloaded, falling back to loaded).</summary>
    public decimal WageQty { get; set; }
    /// <summary>Price per ton charged to the customer.</summary>
    public decimal CustomerPrice { get; set; }
    /// <summary>Tariff price found at save (NULL if no tariff matched).</summary>
    public decimal? TariffPrice { get; set; }
    /// <summary>Driver rate per ton frozen at save (0 for partner trips).</summary>
    public decimal DriverRate { get; set; }
    /// <summary>Where the driver rate came from.</summary>
    public string RateSource { get; set; } = string.Empty;
    /// <summary>Month of the monthly rate used (YYYY-MM).</summary>
    public string RateMonth { get; set; } = string.Empty;
    /// <summary>Partner rate per ton (partner trips).</summary>
    public decimal? PartnerRate { get; set; }
    /// <summary>BillingQty × CustomerPrice.</summary>
    public decimal Revenue { get; set; }
    /// <summary>WageQty × DriverRate (0 for partner trips).</summary>
    public decimal DriverWage { get; set; }
    /// <summary>WageQty × PartnerRate (partner trips), else 0.</summary>
    public decimal PartnerCost { get; set; }
    /// <summary>LoadedWeight − UnloadedWeight.</summary>
    public decimal? ShortageWeight { get; set; }
    /// <summary>ShortageWeight ÷ LoadedWeight × 100.</summary>
    public decimal? ShortagePct { get; set; }
    /// <summary>1 when ShortagePct is above the company tolerance (default 0.5 %).</summary>
    public bool IsShortageFlagged { get; set; }
    /// <summary>1 when CustomerPrice differs from TariffPrice; breach value = (price − tariff) × BillingQty.</summary>
    public bool IsTariffBreach { get; set; }
    /// <summary>1 when the driver rate was typed manually instead of the monthly rate.</summary>
    public bool IsManualRate { get; set; }
    /// <summary>1 for rows imported in tolerant mode with defaults applied.</summary>
    public bool NeedsReview { get; set; }
    /// <summary>What needs review.</summary>
    public string? ReviewNote { get; set; }
    /// <summary>Trip status.</summary>
    public string Status { get; set; } = "Active";
    /// <summary>Invoice the trip is billed on.</summary>
    public long? InvoiceId { get; set; }
    /// <summary>Driver settlement that paid the wage.</summary>
    public long? SettlementId { get; set; }
    /// <summary>Wage accrual or partner-cost entry posted on save.</summary>
    public long? JournalEntryId { get; set; }
    /// <summary>Why the record was voided.</summary>
    public string? VoidReason { get; set; }
    /// <summary>User who voided the record.</summary>
    public long? VoidedBy { get; set; }
    /// <summary>UTC time the record was voided.</summary>
    public DateTime? VoidedAt { get; set; }
    /// <summary>Import batch that created the trip.</summary>
    public long? ImportBatchId { get; set; }
    /// <summary>Free-text notes.</summary>
    public string? Notes { get; set; }
    /// <summary>Values of admin-defined custom fields, as a JSON object keyed by FieldKey (definitions in cfg.CustomFieldDefinitions).</summary>
    public string? CustomValues { get; set; }
    public Driver Driver { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public LoadingSite LoadingSite { get; set; } = null!;
    public UnloadingSite UnloadingSite { get; set; } = null!;
    public Material Material { get; set; } = null!;
    public Partner? Partner { get; set; }
    public Invoice? Invoice { get; set; }
    public Settlement? Settlement { get; set; }
    public JournalEntry? JournalEntry { get; set; }
    public User? VoidedByUser { get; set; }
    public ImportBatch? ImportBatch { get; set; }
}
