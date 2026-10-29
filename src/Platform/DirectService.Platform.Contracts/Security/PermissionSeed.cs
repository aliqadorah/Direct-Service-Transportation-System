namespace DirectService.Platform.Contracts.Security;

/// <summary>
/// One seeded core.Permissions row (platform design a.6, e.2 E-20): fixed Id and PublicId, the stored key and group, the Arabic and English
/// descriptions. Every module seeds the keys it owns through an <see cref="IPermissionSeedProvider"/>; the Platform model contributor writes
/// all rows with the same <c>HasData</c> values as before the split.
/// </summary>
public sealed record PermissionSeed(long Id, string PublicId, string Key, string Group, string Ar, string En);

/// <summary>The permission rows a module owns (keys with its prefix), seeded by the Platform model contributor.</summary>
public interface IPermissionSeedProvider
{
    IReadOnlyList<PermissionSeed> Permissions { get; }
}
