using System.Collections.Frozen;
using System.Reflection;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// Maps namespaced entity names (the audit/lock naming, e.g. TRN.Trip — the pinned stored names, platform design §e, E-1/E-2) to entity types and resolves URL tokens
/// to internal ids through the normal query filters, so a record of another tenant/company is never found.
/// </summary>
public sealed class EntityResolver(MasterDbContext db, IUrlTokenService tokens) : IEntityResolver
{
    private FrozenDictionary<string, Type> Types => EntityTypeMap.Records(db.Model);

    private static readonly MethodInfo ResolveGeneric =
        typeof(EntityResolver).GetMethod(nameof(ResolveTyped), BindingFlags.NonPublic | BindingFlags.Instance)!;

    public Type? TypeOf(string entityType) => Types.GetValueOrDefault(entityType);

    public async Task<long?> ResolveIdAsync(string entityType, string token, CancellationToken ct)
    {
        var type = TypeOf(entityType);
        if (type is null) return null;
        var publicId = tokens.Decode(type, token);
        if (publicId is null) return null;
        var task = (Task<long?>)ResolveGeneric.MakeGenericMethod(type).Invoke(this, [publicId.Value, ct])!;
        return await task.ConfigureAwait(false);
    }

    private Task<long?> ResolveTyped<T>(Guid publicId, CancellationToken ct) where T : AuditableEntity =>
        db.Set<T>().AsNoTracking().Where(e => e.PublicId == publicId).Select(e => (long?)e.Id).SingleOrDefaultAsync(ct);
}
