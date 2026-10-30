namespace DirectService.Platform.Contracts.Modules;

// What a module (Transportation, Accounting, …) contributes to the Platform services (platform design a.3, Stage 2 session 12, DT-346).
// Each module registers its implementations; the Platform services use every registered one, in registration order. Browser-safe: plain
// values only (no EF Core, no SharedKernel).

/// <summary>Adds a module's reference rows to a company just created (ownership types, settings, number sequences, layouts, tax codes…).</summary>
public interface ICompanyProvisioner
{
    /// <summary>Adds and saves the rows for the saved company, inside the caller's transaction, under the company's own scope.</summary>
    Task ProvisionAsync(long tenantId, long companyId, long? actingUserId, CancellationToken ct);
}

/// <summary>Fills the demo company with a module's sample data through its normal services.</summary>
public interface IDemoDataContributor
{
    /// <summary>Called inside the demo company's transaction, scope and request context (the Owner, every permission).</summary>
    Task AddDemoDataAsync(CancellationToken ct);
}

/// <summary>The state of one step of the setup checklist as a module works it out from the company's data.</summary>
/// <param name="Key">Step key (also the stored mark <c>CORE.Setup.&lt;key&gt;</c>).</param>
/// <param name="Status">"Done", "InProgress" or "NotStarted" before the owner's own mark.</param>
public sealed record SetupStepState(string Key, string TitleAr, string TitleEn, string Status, string? Detail, string Link, IReadOnlyList<string>? Items = null);

/// <summary>A module's steps of the setup checklist, shown between the platform's own first steps (account, profile) and its last (users).</summary>
public interface ISetupStepProvider
{
    /// <summary>Step keys in the order shown.</summary>
    IReadOnlyList<string> Steps { get; }

    /// <summary>The steps of the current company in the language of the request (<paramref name="language"/> "ar" or "en").</summary>
    Task<IReadOnlyList<SetupStepState>> StatesAsync(string language, CancellationToken ct);
}

/// <summary>A measure of a module counted against the plan's limits (e.g. trips saved per calendar month).</summary>
public interface IUsageMeter
{
    /// <summary>The measure (<see cref="UsageMeasures"/>).</summary>
    string Measure { get; }

    /// <summary>The tenant's count since <paramref name="fromUtc"/> (all companies, by explicit tenant id).</summary>
    Task<int> CountAsync(long tenantId, DateTime fromUtc, CancellationToken ct);
}

/// <summary>Measures of <see cref="IUsageMeter"/>.</summary>
public static class UsageMeasures
{
    /// <summary>Trips saved per calendar month (plan limit MaxTripsPerMonth).</summary>
    public const string Trips = "trips";
}

/// <summary>Clears a module's personal data of a tenant at the end of retention (G-18, SEC-19); the platform soft-deletes every row itself.</summary>
public interface ITenantDataEraser
{
    /// <summary>Clears the personal fields in place (records kept), by explicit tenant id, inside the caller's transaction.</summary>
    Task AnonymiseAsync(long tenantId, DateTime now, CancellationToken ct);
}

/// <summary>A module's permission keys and the keys of each preset role (values pinned: they are stored in core.Permissions / RolePermissions).</summary>
public interface IPermissionCatalog
{
    IReadOnlyList<string> Keys { get; }

    /// <summary>Preset role name → its keys from this module.</summary>
    IReadOnlyDictionary<string, IReadOnlyList<string>> PresetRoles { get; }

    /// <summary>The module's keys a support session holds (G-09: the view permissions, nothing that creates or changes; Stage 2 session 13).</summary>
    IReadOnlyList<string> SupportSessionKeys { get; }
}

/// <summary>
/// A module's notification types (stored in core.Notifications.Type; values pinned) with their texts, the links to its records, the
/// parameters worked out when they are shown and the older notifications a reader's list replaces by one (DT-189; Stage 2 session 14).
/// </summary>
public interface INotificationTypeProvider
{
    IReadOnlyCollection<string> Types { get; }

    /// <summary>Title, text and details of a stored notification of one of <see cref="Types"/> in the reader's language ("ar" or "en");
    /// <paramref name="extra"/> holds the parameters of <see cref="ExtraAsync"/>.</summary>
    Collaboration.NotificationText Render(string type, string message, string language, IReadOnlyDictionary<string, string>? extra = null);

    /// <summary>The parameters of a stored message of one of <see cref="Types"/> (the current form, or read back from an older text); null when unknown.</summary>
    Dictionary<string, string>? Parameters(string type, string message);

    /// <summary>Where a stored link (starting with "/") to one of the module's records or screens leads for the reader; null when it is not the module's.</summary>
    Task<Collaboration.NotificationTarget?> ResolveLinkAsync(string link, CancellationToken ct);

    /// <summary>Parameters worked out when notifications of <paramref name="type"/> are shown in the current company (e.g. a count); null for none.</summary>
    Task<IReadOnlyDictionary<string, string>?> ExtraAsync(string type, CancellationToken ct);

    /// <summary>Older notifications that the reader's list replaces by one, the first time it is shown in a company.</summary>
    IReadOnlyList<Collaboration.NotificationMerge> Merges { get; }
}

/// <summary>A module's figures on the provider's health page (P-15), over every tenant (Stage 2 session 13).</summary>
public interface IHealthFigureSource
{
    /// <summary>The module's counts by measure (<see cref="HealthMeasures"/>); <paramref name="monthStartUtc"/> starts the current month.</summary>
    Task<IReadOnlyDictionary<string, int>> CountAsync(DateTime monthStartUtc, CancellationToken ct);
}

/// <summary>Measures of <see cref="IHealthFigureSource"/> (fields of the health page).</summary>
public static class HealthMeasures
{
    /// <summary>Trips saved this calendar month.</summary>
    public const string Trips = "trips";

    /// <summary>Automatic postings waiting to be resolved.</summary>
    public const string PendingPostings = "pendingPostings";

    /// <summary>E-invoice submissions queued or rejected.</summary>
    public const string EInvoices = "eInvoices";
}
