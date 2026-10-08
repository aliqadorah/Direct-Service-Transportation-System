namespace DirectService.Transportation.Contracts.Security;

/// <summary>
/// The 22 permission keys of PRD 3.4, namespaced per owner decision D-01 (module prefix + PRD key).
/// The UI shows the PRD names (<see cref="PrdName"/>).
/// </summary>
public static class PermissionKeys
{
    public const string TripsView = "trn.trips.view";
    public const string TripsEdit = "trn.trips.edit";
    public const string TripsVoid = "trn.trips.void";
    public const string InvView = "trn.inv.view";
    public const string InvIssue = "trn.inv.issue";
    public const string InvVoid = "trn.inv.void";
    public const string RcptManage = "trn.rcpt.manage";
    public const string DrvView = "trn.drv.view";
    public const string DrvAdvance = "trn.drv.advance";
    public const string DrvSettle = "trn.drv.settle";
    public const string JeView = "acc.je.view";
    public const string JeExport = "acc.je.export";
    public const string AcctView = "acc.acct.view";
    public const string AcctPost = "acc.acct.post";
    public const string AcctApar = "acc.acct.apar";
    public const string AcctSetup = "acc.acct.setup";
    public const string AcctMaint = "acc.acct.maint";
    public const string ReportsView = "trn.reports.view";
    public const string MastersManage = "trn.masters.manage";
    public const string SettingsManage = "core.settings.manage";
    public const string UsersManage = "core.users.manage";
    public const string TenantManage = "core.tenant.manage";

    public static readonly IReadOnlyList<string> All =
    [
        TripsView, TripsEdit, TripsVoid, InvView, InvIssue, InvVoid, RcptManage, DrvView, DrvAdvance,
        DrvSettle, JeView, JeExport, AcctView, AcctPost, AcctApar, AcctSetup, AcctMaint, ReportsView,
        MastersManage, SettingsManage, UsersManage, TenantManage,
    ];

    /// <summary>PRD 3.4 name of a namespaced key, e.g. "trn.trips.view" → "trips.view".</summary>
    public static string PrdName(string key) => key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..];
}

/// <summary>The preset roles of PRD 3.5. Names are fixed codes; the UI translates them.</summary>
public static class PresetRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Accountant = "Accountant";
    public const string Dispatcher = "Dispatcher";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Permissions =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [Owner] = PermissionKeys.All,
            [Administrator] = PermissionKeys.All.Where(k => k != PermissionKeys.TenantManage).ToArray(),
            [Accountant] =
            [
                PermissionKeys.TripsView, PermissionKeys.InvView, PermissionKeys.InvIssue, PermissionKeys.InvVoid,
                PermissionKeys.RcptManage, PermissionKeys.DrvView, PermissionKeys.DrvAdvance, PermissionKeys.DrvSettle,
                PermissionKeys.JeView, PermissionKeys.JeExport, PermissionKeys.AcctView, PermissionKeys.AcctPost,
                PermissionKeys.AcctApar, PermissionKeys.ReportsView,
            ],
            [Dispatcher] =
            [
                PermissionKeys.TripsView, PermissionKeys.TripsEdit, PermissionKeys.TripsVoid, PermissionKeys.DrvView,
                PermissionKeys.MastersManage, PermissionKeys.ReportsView,
            ],
            [Viewer] =
            [
                PermissionKeys.TripsView, PermissionKeys.InvView, PermissionKeys.DrvView, PermissionKeys.JeView,
                PermissionKeys.AcctView, PermissionKeys.ReportsView,
            ],
        };

    public static readonly IReadOnlyList<string> All = [Owner, Administrator, Accountant, Dispatcher, Viewer];
}
