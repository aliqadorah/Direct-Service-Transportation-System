// Generated once from docs/inputs/Database_Design.md (rev. 5) by tools/design/gen_efcore.py.
// Hand-maintained from here on.
namespace DirectService.Transportation.Domain.Core;

/// <summary>
/// core.Permissions â€” Platform-wide.
/// </summary>
[StoredName("CORE.Permission")]
[TokenLabel("Permission")]
public class Permission : AuditableEntity
{
    /// <summary>Namespaced key checked on the server, e.g. trn.trips.view.</summary>
    public string PermissionKey { get; set; } = string.Empty;
    /// <summary>Group shown in the role editor (e.g. Trips, Invoices).</summary>
    public string GroupName { get; set; } = string.Empty;
    /// <summary>What the permission allows (Arabic).</summary>
    public string DescriptionAr { get; set; } = string.Empty;
    /// <summary>What the permission allows (English).</summary>
    public string DescriptionEn { get; set; } = string.Empty;
}
