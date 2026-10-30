namespace DirectService.Platform.Contracts.Security;

/// <summary>
/// The platform's permission keys (stored in core.Permissions / RolePermissions; values pinned, e.2). Defined here only: the module's
/// <c>PermissionKeys</c> names them under the same names (Stage 2 session 13).
/// </summary>
public static class PlatformPermissionKeys
{
    public const string SettingsManage = "core.settings.manage";
    public const string UsersManage = "core.users.manage";
    public const string TenantManage = "core.tenant.manage";

    /// <summary>PRD 3.4 name of a namespaced key, e.g. "trn.trips.view" → "trips.view".</summary>
    public static string PrdName(string key) => key[(key.IndexOf('.', StringComparison.Ordinal) + 1)..];
}

/// <summary>
/// The names of the preset roles of PRD 3.5 (stored in core.Roles.Name; fixed codes, the UI translates them). Each module gives the keys of
/// every preset role through its <see cref="Modules.IPermissionCatalog"/>.
/// </summary>
public static class PlatformPresetRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Accountant = "Accountant";
    public const string Dispatcher = "Dispatcher";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Owner, Administrator, Accountant, Dispatcher, Viewer];
}
