// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.SubscriptionInvoices â€” Tenant-scoped.
/// </summary>
[StoredName("CORE.SubscriptionInvoice")]
[TokenLabel("SubscriptionInvoice")]
public class SubscriptionInvoice : AuditableEntity, ITenantScoped
{
    /// <summary>Owning tenant — tenant isolation filter on every query.</summary>
    public long TenantId { get; set; }
    /// <summary>Provider invoice number.</summary>
    public string Number { get; set; } = string.Empty;
    /// <summary>First day billed.</summary>
    public DateOnly PeriodStart { get; set; }
    /// <summary>Last day billed.</summary>
    public DateOnly PeriodEnd { get; set; }
    /// <summary>Charge before tax.</summary>
    public decimal Amount { get; set; }
    /// <summary>Sales tax amount.</summary>
    public decimal Tax { get; set; }
    /// <summary>Amount + Tax.</summary>
    public decimal Total { get; set; }
    /// <summary>Payment state.</summary>
    public string Status { get; set; } = "Due";
    /// <summary>When payment succeeded.</summary>
    public DateTime? PaidAt { get; set; }
    /// <summary>Payment-gateway transaction reference (provider deferred; stored as text).</summary>
    public string? GatewayReference { get; set; }
}
