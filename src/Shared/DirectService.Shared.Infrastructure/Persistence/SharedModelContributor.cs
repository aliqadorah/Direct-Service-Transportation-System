using Microsoft.EntityFrameworkCore;

namespace DirectService.Shared.Infrastructure.Persistence;

/// <summary>
/// The Shared part of the composed <see cref="MasterDbContext"/> (platform design a.4, cfg schema): the entity configurations of this
/// assembly. Their foreign keys to core.Tenants / Companies / Users are added by the module that owns those tables.
/// </summary>
public sealed class SharedModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SharedModelContributor).Assembly);
}
