using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Web.Client.Layout;

/// <summary>
/// A menu entry: text key, icon (DT-235: every entry has its own), route (relative to the company, or absolute when it starts with "/"), and
/// the permissions that show it (any of). <see cref="Part"/> is the labelled group of a long section it belongs to (DT-238).
/// </summary>
public sealed record NavItem(string Key, string Icon, string Route, params string[] AnyOf)
{
    public string? Part { get; init; }
}

/// <summary>A menu group of the PRD (UX-11) with its icon; a group with a route and no items is a single entry.</summary>
public sealed record NavGroup(string Key, string Icon, string? Route, IReadOnlyList<NavItem> Items, params string[] AnyOf);

/// <summary>
/// The side menu of the approved board (SCR-00): PRD groups in order; entries the user's role cannot use are hidden (the API still
/// checks every request — SEC-05). "Setup checklist" shows only while setup is incomplete and "Tenant" only for core.tenant.manage (DT-66).
/// Screen routes are relative to <c>/c/{company}/</c>.
/// </summary>
public static class NavModel
{
    public const string Setup = "setup";

    public static readonly IReadOnlyList<NavGroup> Company =
    [
        new("nav.dashboard", "home", "", []),
        new("nav.setup", "check", Setup, []),
        new("nav.operations", "truck", null,
        [
            new("nav.trips", "route", "trips", PermissionKeys.TripsView),
            new("nav.newTrip", "plus", "trips/new", PermissionKeys.TripsEdit),
            new("nav.tripImport", "import", "trips/import", PermissionKeys.TripsEdit),
            new("nav.shortage", "trend-down", "trips/shortage", PermissionKeys.TripsView),
        ]),
        new("nav.billing", "file", null,
        [
            .. Part("nav.part.invoicing",
              new("nav.invoices", "receipt", "invoices", PermissionKeys.InvView),
              new("nav.newTripInvoice", "file-plus", "invoices/new", PermissionKeys.InvIssue),
              new("nav.manualInvoice", "edit", "invoices/manual", PermissionKeys.InvIssue),
              new("nav.creditNotes", "file-minus", "credit-notes", PermissionKeys.InvView),
              new("nav.einvoicing", "qr", "e-invoicing", PermissionKeys.InvView)
            ),
            .. Part("nav.part.collections",
              new("nav.receipts", "cash", "receipts", PermissionKeys.RcptManage, PermissionKeys.InvView),
              new("nav.driverAccounts", "wallet", "driver-accounts", PermissionKeys.DrvView),
              new("nav.partnerAccounts", "swap", "partner-accounts", PermissionKeys.DrvView)
            ),
        ]),
        new("nav.accounting", "book", null,
        [
            // Revision 6 part B: the accounting dashboard is the first page under Accounting (each part by its own permission).
            new("nav.accountingDashboard", "gauge", "accounting/dashboard", PermissionKeys.AcctView, PermissionKeys.JeView, PermissionKeys.JeExport, PermissionKeys.AcctPost,
                PermissionKeys.AcctApar, PermissionKeys.AcctSetup, PermissionKeys.BudgetView, PermissionKeys.ReportsView, PermissionKeys.SettingsManage),
            .. Part("nav.part.entries",
              new("nav.journal", "book-open", "accounting/journal", PermissionKeys.JeView, PermissionKeys.AcctView),
              new("nav.newJournal", "plus", "accounting/journal/new", PermissionKeys.AcctPost),
              new("nav.journalImport", "import", "accounting/journal/import", PermissionKeys.AcctPostEntry),   // DT-201: importing posts entries
              new("nav.journalTemplates", "copy", "accounting/journal-templates", PermissionKeys.AcctPost),   // revision 6 part B
              new("nav.automatic", "zap", "accounting/automatic", PermissionKeys.JeView)
            ),
            .. Part("nav.part.balances",
              new("nav.receivables", "in", "accounting/receivables", PermissionKeys.AcctApar, PermissionKeys.AcctView),
              new("nav.payables", "out", "accounting/payables", PermissionKeys.AcctApar, PermissionKeys.AcctView),
              new("nav.cashBanks", "bank", "accounting/cash-banks", PermissionKeys.AcctView)
            ),
            // Revision 6: the general ledger screen was removed (its address opens the account statement of several accounts).
            .. Part("nav.part.analysis",
              new("nav.trialBalance", "scale", "accounting/trial-balance", PermissionKeys.AcctView),
              new("nav.profitLoss", "trend-up", "accounting/profit-loss", PermissionKeys.AcctView),
              new("nav.balanceSheet", "columns", "accounting/balance-sheet", PermissionKeys.AcctView),
              new("nav.statement", "list", "accounting/statement", PermissionKeys.AcctView, PermissionKeys.ReportsView),
              new("nav.budgetVsActual", "chart", "accounting/budget-vs-actual", PermissionKeys.BudgetView)
            ),
            .. Part("nav.part.acctSetup",
              new("nav.chart", "tree", "accounting/chart", PermissionKeys.AcctView),
              new("nav.costCentres", "hub", "accounting/cost-centres", PermissionKeys.AcctView),
              new("nav.budgets", "target", "accounting/budgets", PermissionKeys.BudgetView),
              new("nav.periods", "calendar", "accounting/periods", PermissionKeys.AcctView),
              new("nav.maintenance", "wrench", "accounting/maintenance", PermissionKeys.AcctMaint)
            ),
        ]),
        new("nav.reports", "chart", null,
        [
            new("nav.opReports", "activity", "reports/operational", PermissionKeys.ReportsView),
            new("nav.finReports", "pie", "reports/financial", PermissionKeys.ReportsView, PermissionKeys.AcctView),
            new("nav.reportBuilder", "sliders", "reports/builder", PermissionKeys.ReportsView),
            new("nav.savedReports", "bookmark", "reports/saved", PermissionKeys.ReportsView),
        ]),
        new("nav.masterData", "db", null,
        [
            .. Part("nav.part.parties",
              new("nav.drivers", "steering", "masters/drivers"),
              new("nav.customers", "store", "masters/customers"),
              new("nav.partners", "briefcase", "masters/partners"),
              new("nav.ownershipTypes", "key", "masters/ownership-types")
            ),
            .. Part("nav.part.sites",
              new("nav.materials", "box", "masters/materials"),
              new("nav.loadingSites", "upload", "masters/loading-sites"),
              new("nav.unloadingSites", "download", "masters/unloading-sites")
            ),
            .. Part("nav.part.prices",
              new("nav.tariffs", "tag", "masters/tariffs", PermissionKeys.MastersManage, PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.ReportsView),
              new("nav.monthlyRates", "percent", "masters/monthly-rates", PermissionKeys.MastersManage, PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.ReportsView)
            ),
        ]),
        new("nav.settings", "gear", null,
        [
            .. Part("nav.part.company",
              new("nav.companySettings", "building", "settings/company", PermissionKeys.SettingsManage),
              new("nav.numbering", "hash", "settings/numbering", PermissionKeys.SettingsManage),
              new("nav.mapping", "link", "settings/mapping", PermissionKeys.SettingsManage),
              new("nav.einvoiceSettings", "qr", "settings/e-invoicing", PermissionKeys.SettingsManage),
              new("nav.layoutDesigner", "layout", "settings/invoice-layout", PermissionKeys.InvIssue),   // FR-055: the layout designer belongs to inv.issue
              new("nav.fields", "form", "settings/fields", PermissionKeys.SettingsManage)
            ),
            .. Part("nav.part.access",
              new("nav.users", "users", "settings/users", PermissionKeys.UsersManage),
              new("nav.audit", "history", "settings/audit-log", PermissionKeys.UsersManage, PermissionKeys.SettingsManage)
            ),
        ]),
        new("nav.tenant", "building", null,
        [
            new("nav.subscription", "card", "/tenant/subscription", PermissionKeys.TenantManage),
            new("nav.companies", "buildings", "/tenant/companies", PermissionKeys.TenantManage),
            new("nav.dataExport", "export", "/tenant/export", PermissionKeys.TenantManage),
            new("nav.supportApprovals", "shield", "/tenant/support-access", PermissionKeys.TenantManage),
        ], PermissionKeys.TenantManage),
        new("nav.help", "info", "help", []),   // D-18 help pages, for every member
    ];

    /// <summary>Provider console menu (separate shell, DT-66).</summary>
    public static readonly IReadOnlyList<(string Key, string Route, string Icon)> Console =
    [
        ("nav.tenants", "/console/tenants", "users"),
        ("nav.plans", "/console/plans", "layers"),
        ("nav.supportAccess", "/console/support-access", "shield"),
        ("nav.announcements", "/console/announcements", "megaphone"),
        ("nav.staff", "/console/staff", "idcard"),
        ("nav.health", "/console/health", "activity"),
    ];

    /// <summary>Entries of one labelled group inside a long section (DT-238: sections of more than about eight entries are split).</summary>
    private static NavItem[] Part(string key, params NavItem[] items) => [.. items.Select(i => i with { Part = key })];

    public static bool Allowed(IReadOnlyCollection<string> granted, string[] anyOf) => anyOf.Length == 0 || anyOf.Any(granted.Contains);

    /// <summary>The menu for a user: groups and entries filtered by permission; the setup entry only while setup is incomplete.</summary>
    public static IReadOnlyList<NavGroup> For(IReadOnlyCollection<string> granted, bool setupIncomplete) =>
        Company
            .Where(g => Allowed(granted, g.AnyOf) && (g.Route != Setup || setupIncomplete))
            .Select(g => g with { Items = g.Items.Where(i => Allowed(granted, i.AnyOf)).ToList() })
            .Where(g => g.Route is not null || g.Items.Count > 0)
            .ToList();

    /// <summary>
    /// Whether the screen at a company address is open to these permissions (UX-13): the menu entry with the longest matching route decides
    /// ("trips/new" before "trips"); addresses without an entry are open (the API still checks every request).
    /// </summary>
    public static bool RouteAllowed(IReadOnlyCollection<string> granted, string relativeUrl)
    {
        ArgumentNullException.ThrowIfNull(relativeUrl);
        var path = relativeUrl.Split('?', '#')[0].Trim('/');
        if (!path.StartsWith("c/", StringComparison.Ordinal)) return true;
        var rest = path.Split('/', 3) is { Length: 3 } parts ? parts[2] : string.Empty;
        var entry = Company.SelectMany(g => g.Items)
            .Where(i => !i.Route.StartsWith('/') && (rest == i.Route || rest.StartsWith(i.Route + "/", StringComparison.Ordinal)))
            .OrderByDescending(i => i.Route.Length).FirstOrDefault();
        return entry is null || Allowed(granted, entry.AnyOf);
    }

    /// <summary>The full path of a route for a company token.</summary>
    public static string Href(string company, string route) => route.StartsWith('/') ? route : route.Length == 0 ? $"/c/{company}" : $"/c/{company}/{route}";
}
