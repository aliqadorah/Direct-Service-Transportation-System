namespace DirectService.Platform.Application.Billing;

/// <summary>Provider settings (subscription invoices, lifecycle periods).</summary>
public sealed class PlatformOptions
{
    public const string Section = "Platform";

    public decimal TaxRate { get; set; } = 16m;
    public string Currency { get; set; } = "JOD";
    public int CurrencyDecimals { get; set; } = 3;
    public string SellerName { get; set; } = "Direct Service";
    public string? SellerTaxNumber { get; set; }
    public int GraceDays { get; set; } = 7;
    public int SuspendedDaysBeforeCancel { get; set; } = 30;
    public int RetentionDays { get; set; } = 90;

    /// <summary>
    /// Setup code of the first-run provider setup (/provider/setup) while no platform staff exists; when empty a one-time code is generated at
    /// start-up and written to the log. Set it through an environment variable (Platform__SetupCode) or user secrets, never in appsettings.
    /// </summary>
    public string? SetupCode { get; set; }
}

/// <summary>A payment page for an invoice (the card is entered only there, SEC-10).</summary>
public sealed record GatewayCheckout(string Reference, string PaymentUrl);

/// <summary>A verified gateway notification: the payment reference, whether it succeeded, and the amount and currency actually paid.</summary>
/// <remarks>Metadata given at checkout comes back with the event (the plan and kind of the invoice, DT-124).</remarks>
public sealed record GatewayEvent(string Reference, bool Succeeded, string? Message, IReadOnlyDictionary<string, string> Metadata, decimal? Amount = null, string? Currency = null);

/// <summary>Kinds of subscription invoice carried in the checkout metadata: a full period, or a pro-rated upgrade within the current period.</summary>
public static class InvoiceKinds
{
    public const string Period = "period";
    public const string Upgrade = "upgrade";
}

/// <summary>A platform-wide lock for background jobs that must run on one instance at a time (the lifecycle run, review F5).</summary>
public interface IPlatformLock
{
    /// <summary>The lock, or null when another instance holds it; disposing releases it.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string name, CancellationToken ct);
}

/// <summary>
/// The payment gateway (DEF-02, provider-neutral): a hosted payment page per invoice, verified webhooks, and charging the stored payment
/// method of a tenant at renewal. Card data never reaches the platform (SEC-10).
/// </summary>
public interface IPaymentGateway
{
    Task<GatewayCheckout> CreateCheckoutAsync(string invoiceNumber, decimal total, string currency, string tenantName, IReadOnlyDictionary<string, string> metadata, CancellationToken ct);

    /// <summary>The event of a webhook call, or null when the signature does not verify.</summary>
    GatewayEvent? VerifyWebhook(string body, string? signature);

    /// <summary>
    /// Charges the tenant's saved payment method at renewal. The invoice number is the idempotency key: charging the same invoice again (a run
    /// that failed after the charge) returns the first charge instead of taking the money twice.
    /// </summary>
    Task<GatewayEvent> ChargeAsync(string tenantKey, string invoiceNumber, decimal total, string currency, CancellationToken ct);
}

/// <summary>Platform-wide gap-free numbers of subscription invoices (raw SQL with UPDLOCK in Infrastructure).</summary>
public interface IPlatformNumbers
{
    Task<string> NextSubscriptionInvoiceAsync(CancellationToken ct);
}
