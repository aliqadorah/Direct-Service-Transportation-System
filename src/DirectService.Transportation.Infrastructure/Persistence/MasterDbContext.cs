using System.Reflection;
using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Common;
using DirectService.Transportation.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// The Master Database (NFR-20): shared schemas core / acc / cfg and the Transportation schema trn.
/// Every entity has the named query filters <see cref="QueryFilters.SoftDelete"/>,
/// <see cref="QueryFilters.Tenant"/> and (company-scoped tables) <see cref="QueryFilters.Company"/>.
/// </summary>
public sealed class MasterDbContext(DbContextOptions<MasterDbContext> options, IDataScope scope)
    : DbContext(options), IAppDbContext
{
    /// <summary>Database collation approved at checkpoint 1 (A-19 / Q-07).</summary>
    public const string Collation = "Arabic_100_CI_AI_SC_UTF8";

    private readonly IDataScope _scope = scope;

    // Read by the query filters; EF Core evaluates these per context instance (per request).
    // -1 never matches a real key, so a missing scope returns no rows instead of everything.
    internal long FilterTenantId => _scope.TenantId ?? -1;
    internal long? FilterNullableTenantId => _scope.TenantId;
    internal long FilterCompanyId => _scope.CompanyId ?? -1;

    internal IDataScope Scope => _scope;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Only the indexes listed in the design are created (no automatic index per foreign key).
        configurationBuilder.Conventions.Remove<ForeignKeyIndexConvention>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.UseCollation(Collation);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MasterDbContext).Assembly);
        ReferenceDataSeed.Apply(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            NameDefaultConstraints(entityType);
            ApplyQueryFilters(modelBuilder, entityType.ClrType);
        }
    }

    /// <summary>Default constraints are named DF_&lt;Table&gt;_&lt;Column&gt; (design §3.1).</summary>
    private static void NameDefaultConstraints(IMutableEntityType entityType)
    {
        var table = entityType.GetTableName();
        if (table is null) return;
        foreach (var property in entityType.GetProperties())
        {
            if (property.GetDefaultValueSql() is not null)
            {
                property.SetDefaultConstraintName($"DF_{table}_{property.GetColumnName()}");
            }
        }
    }

    private void ApplyQueryFilters(ModelBuilder modelBuilder, Type clrType)
    {
        if (typeof(ISoftDeletable).IsAssignableFrom(clrType))
            Invoke(nameof(SoftDeleteFilter), clrType, modelBuilder);
        if (typeof(ITenantScoped).IsAssignableFrom(clrType))
            Invoke(nameof(TenantFilter), clrType, modelBuilder);
        if (typeof(INullableTenantScoped).IsAssignableFrom(clrType))
            Invoke(nameof(NullableTenantFilter), clrType, modelBuilder);
        if (typeof(ICompanyScoped).IsAssignableFrom(clrType))
            Invoke(nameof(CompanyFilter), clrType, modelBuilder);
    }

    private void Invoke(string method, Type clrType, ModelBuilder modelBuilder)
    {
        var m = typeof(MasterDbContext)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(clrType);
        m.Invoke(m.IsStatic ? null : this, [modelBuilder]);
    }

    private static void SoftDeleteFilter<T>(ModelBuilder b) where T : class, ISoftDeletable =>
        b.Entity<T>().HasQueryFilter(QueryFilters.SoftDelete, e => !e.IsDeleted);

    private void TenantFilter<T>(ModelBuilder b) where T : class, ITenantScoped =>
        b.Entity<T>().HasQueryFilter(QueryFilters.Tenant, e => e.TenantId == FilterTenantId);

    private void NullableTenantFilter<T>(ModelBuilder b) where T : class, INullableTenantScoped =>
        b.Entity<T>().HasQueryFilter(QueryFilters.Tenant, e => e.TenantId == FilterNullableTenantId);

    private void CompanyFilter<T>(ModelBuilder b) where T : class, ICompanyScoped =>
        b.Entity<T>().HasQueryFilter(QueryFilters.Company, e => e.CompanyId == FilterCompanyId);
}

/// <summary>Names of the EF Core named query filters (used with IgnoreQueryFilters([...])).</summary>
public static class QueryFilters
{
    public const string SoftDelete = "SoftDelete";
    public const string Tenant = "Tenant";
    public const string Company = "Company";
}
