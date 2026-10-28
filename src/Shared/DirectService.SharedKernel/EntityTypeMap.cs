using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DirectService.SharedKernel;

/// <summary>
/// Stored entity names (e.g. <c>TRN.Trip</c>) → entity types, built from the EF model and the pinned <see cref="StoredNameAttribute"/>
/// (platform design §e, E-1/E-2) instead of scanning one assembly and reading namespaces, so entities in any module are found and a moved
/// class keeps its name. Built once per model.
/// </summary>
public static class EntityTypeMap
{
    private sealed record Maps(IModel Model, FrozenDictionary<string, Type> Records, FrozenDictionary<string, Type> WithLongId);

    private static Maps? _maps;

    /// <summary>Every concrete <see cref="AuditableEntity"/> of the model (records with a public id: locks, tokens, history links, exports).</summary>
    public static FrozenDictionary<string, Type> Records(IModel model) => For(model).Records;

    /// <summary>Every entity of the model with a <c>long Id</c> (the records plus the append-only logs), as the audit history names them.</summary>
    public static FrozenDictionary<string, Type> WithLongId(IModel model) => For(model).WithLongId;

    /// <summary>The entity CLR types of the model (each once).</summary>
    public static IReadOnlyList<Type> EntityTypes(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return model.GetEntityTypes().Where(e => !e.IsOwned()).Select(e => e.ClrType).Distinct().ToList();
    }

    private static Maps For(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_maps is { } maps && ReferenceEquals(maps.Model, model)) return maps;
        var types = EntityTypes(model);
        maps = new Maps(model,
            types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(AuditableEntity).IsAssignableFrom(t))
                .ToFrozenDictionary(StoredNames.Of, t => t, StringComparer.Ordinal),
            types.Where(t => t is { IsClass: true, IsAbstract: false } && t.GetProperty("Id")?.PropertyType == typeof(long))
                .ToFrozenDictionary(StoredNames.Of, t => t, StringComparer.Ordinal));
        _maps = maps;
        return maps;
    }
}
