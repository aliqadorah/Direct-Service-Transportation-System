using DirectService.Transportation.Contracts.Common;

namespace DirectService.Transportation.Contracts.Platform;

/// <summary>Stored tenant and billing states (design: core.Tenants, core.SubscriptionInvoices, core.SupportAccessGrants).</summary>
public static class LifecycleCodes
{
    public const string Trial = "Trial";
    public const string Active = "Active";
    public const string PastDue = "PastDue";
    public const string Suspended = "Suspended";
    public const string Cancelled = "Cancelled";
    public const string Deleted = "Deleted";
    public const string Due = "Due";
    public const string Paid = "Paid";
    public const string Failed = "Failed";
    public const string Credited = "Credited";
    public const string Requested = "Requested";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Expired = "Expired";
    public const string Monthly = "Monthly";
    public const string Annual = "Annual";
}

// ------------------------------------------------------------------ plans (5.3, P-03)

public sealed record PlanRequest(string? Code, string? NameAr, string? NameEn, decimal? Price, string? BillingCycle, int? MaxCompanies, int? MaxActiveUsers, int? MaxTripsPerMonth,
    int? MaxStorageMb, bool IsActive, string? RowVersion);

/// <summary>A plan; <paramref name="Price"/> is the final, tax-inclusive fee and <paramref name="TaxRate"/> the sales-tax percent it includes.</summary>
public sealed record PlanResponse(string Id, string Code, string NameAr, string NameEn, decimal Price, string BillingCycle, int MaxCompanies, int MaxActiveUsers,
    int MaxTripsPerMonth, int MaxStorageMb, bool IsActive, int Tenants, string RowVersion, decimal TaxRate = 0m);

// ------------------------------------------------------------------ usage and tenants (5.5, P-11)

/// <summary>Usage against the plan's limits: active non-demo companies, active users, trips saved this calendar month, storage in MB.</summary>
public sealed record UsageResponse(int Companies, int MaxCompanies, int ActiveUsers, int MaxActiveUsers, int TripsThisMonth, int MaxTripsPerMonth, decimal StorageMb, int MaxStorageMb);

public sealed record ConsoleTenantResponse(
    string Id, string Name, string Status, PlanSummary Plan, string BillingCycle, UsageResponse Usage, DateTime SignedUpAt, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd,
    DateTime? GraceEndsAt, DateTime? CancelledAt, DateTime? RetentionEndsAt, string? OwnerEmail, string RowVersion);

public sealed record PlanSummary(string Id, string Code, string Name);

public sealed record ConsoleTenantQuery(string? Status, string? Q, int Page = 1, int PageSize = 50);

public sealed record ConsoleTenantDetails(ConsoleTenantResponse Tenant, IReadOnlyList<SubscriptionInvoiceResponse> Invoices, IReadOnlyList<SupportAccessResponse> SupportAccess,
    IReadOnlyList<RefundDueResponse>? Refunds = null, IReadOnlyList<RefundedResponse>? Refunded = null);

/// <summary>Payments to refund and those marked refunded (DT-184, DT-185).</summary>
public sealed record RefundsResponse(IReadOnlyList<RefundDueResponse> Due, IReadOnlyList<RefundedResponse> Refunded);

/// <summary>A payment marked refunded by staff (DT-185): the refund date and reference they entered, who marked it and when.</summary>
public sealed record RefundedResponse(RefundDueResponse Payment, DateOnly RefundedOn, string Reference, string? MarkedBy, DateTime MarkedAt);

/// <summary>Marks a payment to refund as refunded outside the platform (DT-185).</summary>
public sealed record MarkRefundedRequest(DateOnly? RefundedOn, string? Reference);

/// <summary>
/// A payment received but not applied, for staff to refund (DT-184): read from the invoice's "PaidNotApplied" audit entry — no refund is
/// processed or recorded here. Kind: <see cref="RefundKinds"/>. The invoice may be hidden from lists (a replaced checkout).
/// </summary>
public sealed record RefundDueResponse(string InvoiceId, string Number, string TenantId, string TenantName, decimal Amount, DateTime? PaidAt, string Kind);

public static class RefundKinds
{
    /// <summary>A checkout staff cancelled by a full credit (DT-183), paid later from its old payment page.</summary>
    public const string Credited = "credited";
    /// <summary>A checkout replaced by a newer one, paid later from its old payment page.</summary>
    public const string Replaced = "replaced";
    /// <summary>Paid but not applied for another reason (tenant cancelled or deleted, stale upgrade, suspended by staff).</summary>
    public const string NotApplied = "notApplied";
}

/// <summary>Account actions (P-12): extend a trial by days, apply a credit to an invoice (D-11), suspend or reactivate with a reason.</summary>
public sealed record ExtendTrialRequest(int? Days, string? Reason);

public sealed record ApplyCreditRequest(string? InvoiceId, decimal? Amount, string? Reason);

public sealed record TenantStatusRequest(string? Reason);

// ------------------------------------------------------------------ subscription (5.3, P-04 … P-07)

public sealed record SubscriptionInvoiceResponse(string Id, string Number, DateOnly PeriodStart, DateOnly PeriodEnd, decimal Amount, decimal Tax, decimal Total, string Status,
    DateTime? PaidAt, string? GatewayReference, DateTime CreatedAt);

public sealed record SubscriptionResponse(
    string Status, PlanResponse Plan, string BillingCycle, DateTime? TrialEndsAt, DateTime? CurrentPeriodEnd, DateTime? GraceEndsAt, DateTime? RetentionEndsAt, UsageResponse Usage,
    IReadOnlyList<PlanResponse> Plans);

/// <summary>Start a payment for a plan (subscribe, renew after failure, or upgrade pro-rated): the invoice and the payment page of the gateway (SEC-10).</summary>
public sealed record CheckoutRequest(string? PlanId);

public sealed record CheckoutResponse(SubscriptionInvoiceResponse Invoice, string PaymentUrl);

public sealed record PlanChangeRequest(string? PlanId);

/// <summary>A refused downgrade lists each limit the current usage exceeds (BR-012).</summary>
public sealed record LimitExceeded(string Limit, decimal Usage, decimal Max);

// ------------------------------------------------------------------ support access (5.5, P-13, BR-014)

public sealed record SupportAccessRequest(string? TenantId, string? Reason);

/// <summary>An approval: explicit times, or (from the screen) a length in <see cref="Hours"/> starting now on the server's clock.</summary>
public sealed record SupportApprovalRequest(DateTime? StartsAt, DateTime? EndsAt, string? Reason, int? Hours = null);

/// <summary>
/// A support request or window; <see cref="DecisionReason"/> is the Owner's reason for a refusal (owner testing 19 Oct 2026); <see cref="OwnerEmail"/>
/// (provider console only) tells same-named subscribers apart (DT-219).
/// </summary>
public sealed record SupportAccessResponse(string Id, string Tenant, string RequestedBy, string Reason, string Status, DateTime? StartsAt, DateTime? EndsAt, string? DecidedBy,
    DateTime CreatedAt, string RowVersion, string? DecisionReason = null, string? OwnerEmail = null);

/// <summary>
/// A subscriber found for a support request (owner testing 19 Oct 2026, DT-214): enough to tell two subscribers with the same name apart —
/// owner email, plan, sign-up date, status — and, when the match came from another user's email, that email.
/// </summary>
public sealed record SupportTenantOption(string Id, string Name, string? OwnerEmail, string PlanCode, string PlanName, DateTime SignedUpAt, string Status,
    string? MatchedEmail);

// ------------------------------------------------------------------ announcements and health (5.5, P-14, P-15)

public sealed record AnnouncementRequest(string? Title, string? Message, DateTime? StartsAt, DateTime? EndsAt, string? RowVersion);

public sealed record AnnouncementResponse(string Id, string Title, string Message, DateTime StartsAt, DateTime EndsAt, string RowVersion);

public sealed record HealthResponse(
    bool Database, IReadOnlyDictionary<string, int> TenantsByStatus, int Users, int Companies, int TripsThisMonth, int PendingPostings, int EInvoicesWaiting, long ProcessMemoryMb,
    TimeSpan Uptime, DateTime GeneratedAt);

/// <summary>Kinds of hit of the provider console's search (GET /platform/search); invoices and subscriptions carry their tenant's id.</summary>
public static class ConsoleSearchKinds
{
    public const string Tenant = "tenant";
    public const string Subscription = "subscription";
    public const string Invoice = "subscriptionInvoice";
    public const string Staff = "staff";
}

// ------------------------------------------------------------------ platform staff (owner feedback 13 Oct 2026, item 8b)

/// <summary>States of a platform staff account on the staff screen.</summary>
public static class StaffStates
{
    public const string Active = "Active";
    public const string Disabled = "Disabled";
    /// <summary>Invited; the link has not been used yet and is still valid.</summary>
    public const string Invited = "Invited";
    /// <summary>Invited; the 48-hour link has expired (resend or cancel).</summary>
    public const string Expired = "Expired";
}

/// <summary>A platform staff member: state, last sign-in, the invitation's dates while pending, and whether it is the reader.</summary>
public sealed record StaffResponse(string Id, string FullName, string Email, string State, DateTime? LastSignInAt, DateTime? InvitedAt, DateTime? InvitationExpiresAt,
    bool TwoFactorEnabled, bool IsSelf);

/// <summary>Invites a staff member by e-mail (a single-use link valid for 48 hours; no public sign-up).</summary>
public sealed record StaffInviteRequest(string? FullName, string? Email);

/// <summary>Deactivation needs a reason (audited); reactivation takes none.</summary>
public sealed record StaffStatusRequest(string? Reason);
