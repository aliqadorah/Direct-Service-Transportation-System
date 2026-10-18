using DirectService.Transportation.Contracts.Security;

namespace DirectService.Transportation.Web.Client.Layout;

/// <summary>A menu entry: text key, route (relative to the company, or absolute when it starts with "/"), and the permissions that show it (any of).</summary>
public sealed record NavItem(string Key, string Route, params string[] AnyOf);

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
            new("nav.trips", "trips", PermissionKeys.TripsView),
            new("nav.newTrip", "trips/new", PermissionKeys.TripsEdit),
            new("nav.tripImport", "trips/import", PermissionKeys.TripsEdit),
            new("nav.shortage", "trips/shortage", PermissionKeys.TripsView),
        ]),
        new("nav.billing", "file", null,
        [
            new("nav.invoices", "invoices", PermissionKeys.InvView),
            new("nav.newTripInvoice", "invoices/new", PermissionKeys.InvIssue),
            new("nav.manualInvoice", "invoices/manual", PermissionKeys.InvIssue),
            new("nav.creditNotes", "credit-notes", PermissionKeys.InvView),
            new("nav.einvoicing", "e-invoicing", PermissionKeys.InvView),
            new("nav.receipts", "receipts", PermissionKeys.RcptManage, PermissionKeys.InvView),
            new("nav.driverAccounts", "driver-accounts", PermissionKeys.DrvView),
            new("nav.partnerAccounts", "partner-accounts", PermissionKeys.DrvView),
        ]),
        new("nav.accounting", "book", null,
        [
            // Revision 6 part B: the accounting dashboard is the first page under Accounting (each part by its own permission).
            new("nav.accountingDashboard", "accounting/dashboard", PermissionKeys.AcctView, PermissionKeys.JeView, PermissionKeys.JeExport, PermissionKeys.AcctPost,
                PermissionKeys.AcctApar, PermissionKeys.AcctSetup, PermissionKeys.BudgetView, PermissionKeys.ReportsView, PermissionKeys.SettingsManage),
            new("nav.chart", "accounting/chart", PermissionKeys.AcctView),
            new("nav.journal", "accounting/journal", PermissionKeys.JeView, PermissionKeys.AcctView),
            new("nav.newJournal", "accounting/journal/new", PermissionKeys.AcctPost),
            new("nav.journalImport", "accounting/journal/import", PermissionKeys.AcctPostEntry),   // DT-201: importing posts entries
            new("nav.journalTemplates", "accounting/journal-templates", PermissionKeys.AcctPost),   // revision 6 part B
            new("nav.automatic", "accounting/automatic", PermissionKeys.JeView),
            // Revision 6: the general ledger screen was removed (its address opens the account statement of several accounts).
            new("nav.trialBalance", "accounting/trial-balance", PermissionKeys.AcctView),
            new("nav.profitLoss", "accounting/profit-loss", PermissionKeys.AcctView),
            new("nav.balanceSheet", "accounting/balance-sheet", PermissionKeys.AcctView),
            new("nav.statement", "accounting/statement", PermissionKeys.AcctView, PermissionKeys.ReportsView),
            // Revision 6 part B: budgets and Budget vs Actual.
            new("nav.budgets", "accounting/budgets", PermissionKeys.BudgetView),
            new("nav.budgetVsActual", "accounting/budget-vs-actual", PermissionKeys.BudgetView),
            new("nav.payables", "accounting/payables", PermissionKeys.AcctApar, PermissionKeys.AcctView),
            new("nav.receivables", "accounting/receivables", PermissionKeys.AcctApar, PermissionKeys.AcctView),
            new("nav.cashBanks", "accounting/cash-banks", PermissionKeys.AcctView),
            new("nav.costCentres", "accounting/cost-centres", PermissionKeys.AcctView),
            new("nav.periods", "accounting/periods", PermissionKeys.AcctView),
            new("nav.maintenance", "accounting/maintenance", PermissionKeys.AcctMaint),
        ]),
        new("nav.reports", "chart", null,
        [
            new("nav.opReports", "reports/operational", PermissionKeys.ReportsView),
            new("nav.finReports", "reports/financial", PermissionKeys.ReportsView, PermissionKeys.AcctView),
            new("nav.reportBuilder", "reports/builder", PermissionKeys.ReportsView),
            new("nav.savedReports", "reports/saved", PermissionKeys.ReportsView),
        ]),
        new("nav.masterData", "db", null,
        [
            new("nav.drivers", "masters/drivers"),
            new("nav.customers", "masters/customers"),
            new("nav.partners", "masters/partners"),
            new("nav.materials", "masters/materials"),
            new("nav.loadingSites", "masters/loading-sites"),
            new("nav.unloadingSites", "masters/unloading-sites"),
            new("nav.ownershipTypes", "masters/ownership-types"),
            new("nav.tariffs", "masters/tariffs", PermissionKeys.MastersManage, PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.ReportsView),
            new("nav.monthlyRates", "masters/monthly-rates", PermissionKeys.MastersManage, PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.ReportsView),
        ]),
        new("nav.settings", "gear", null,
        [
            new("nav.companySettings", "settings/company", PermissionKeys.SettingsManage),
            new("nav.mapping", "settings/mapping", PermissionKeys.SettingsManage),
            new("nav.numbering", "settings/numbering", PermissionKeys.SettingsManage),
            new("nav.einvoiceSettings", "settings/e-invoicing", PermissionKeys.SettingsManage),
            new("nav.layoutDesigner", "settings/invoice-layout", PermissionKeys.InvIssue),   // FR-055: the layout designer belongs to inv.issue
            new("nav.fields", "settings/fields", PermissionKeys.SettingsManage),
            new("nav.users", "settings/users", PermissionKeys.UsersManage),
            new("nav.audit", "settings/audit-log", PermissionKeys.UsersManage, PermissionKeys.SettingsManage),
        ]),
        new("nav.tenant", "building", null,
        [
            new("nav.subscription", "/tenant/subscription", PermissionKeys.TenantManage),
            new("nav.companies", "/tenant/companies", PermissionKeys.TenantManage),
            new("nav.dataExport", "/tenant/export", PermissionKeys.TenantManage),
            new("nav.supportApprovals", "/tenant/support-access", PermissionKeys.TenantManage),
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
        ("nav.staff", "/console/staff", "users"),
        ("nav.health", "/console/health", "activity"),
    ];

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
