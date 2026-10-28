namespace DirectService.Shared.Infrastructure.Configuration;

/// <summary>The configurable entities of every module (<see cref="IFieldCatalogProvider"/>s) for the field engine (Stage 2 session 7, DT-318).</summary>
public sealed class FieldCatalogs(IEnumerable<IFieldCatalogProvider> providers)
{
    private IFieldCatalogProvider? Provider(string? entityType) =>
        entityType is null ? null : providers.FirstOrDefault(p => p.Entities.Contains(entityType, StringComparer.Ordinal));

    public bool IsEntity(string? entityType) => Provider(entityType) is not null;

    public IReadOnlyList<StandardField> For(string entityType) => Provider(entityType)?.For(entityType) ?? throw new KeyNotFoundException(entityType);

    public StandardField? Find(string entityType, string? key) =>
        key is null ? null : For(entityType).FirstOrDefault(f => f.Key == key);

    public bool IsConditional(string entityType, string key) => Provider(entityType)?.IsConditional(entityType, key) ?? false;

    public bool CannotRequire(string entityType, string key) => Provider(entityType)?.CannotRequire(entityType, key) ?? false;

    /// <summary>The stored CustomValues of the entity's records; 404 for an entity no module has.</summary>
    public IQueryable<string?> CustomValues(string entityType) => (Provider(entityType) ?? throw Fail.NotFound()).CustomValues(entityType);
}
