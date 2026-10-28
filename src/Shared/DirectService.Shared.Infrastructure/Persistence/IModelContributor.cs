using Microsoft.EntityFrameworkCore;

namespace DirectService.Shared.Infrastructure.Persistence;

/// <summary>
/// Adds one module's part of the composed <see cref="MasterDbContext"/> model (platform design d): the entity configurations of its
/// assembly, its seed data, mapped database functions and cross-module relationships. Each module's Infrastructure registers one.
/// </summary>
public interface IModelContributor
{
    void Apply(ModelBuilder modelBuilder);
}
