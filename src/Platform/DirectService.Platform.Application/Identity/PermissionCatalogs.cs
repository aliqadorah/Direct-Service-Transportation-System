using DirectService.Platform.Contracts.Modules;

namespace DirectService.Platform.Application.Identity;

/// <summary>
/// Every permission key and the keys of each preset role over the modules' <see cref="IPermissionCatalog"/>s, in registration order (Stage 2
/// session 13). With one catalogue the answers are that catalogue's, in its order.
/// </summary>
public static class PermissionCatalogs
{
    /// <summary>Every key of every catalogue, first occurrence kept.</summary>
    public static IReadOnlyList<string> AllKeys(this IEnumerable<IPermissionCatalog> catalogs) =>
        catalogs.SelectMany(c => c.Keys).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Whether a catalogue has the key.</summary>
    public static bool IsKey(this IEnumerable<IPermissionCatalog> catalogs, string key) => catalogs.Any(c => c.Keys.Contains(key));

    /// <summary>The keys of the preset role <paramref name="name"/> (<c>PlatformPresetRoles</c>) from every catalogue, first occurrence kept.</summary>
    public static IReadOnlyList<string> PresetRoleKeys(this IEnumerable<IPermissionCatalog> catalogs, string name) =>
        catalogs.SelectMany(c => c.PresetRoles.TryGetValue(name, out var keys) ? keys : []).Distinct(StringComparer.Ordinal).ToList();
}
