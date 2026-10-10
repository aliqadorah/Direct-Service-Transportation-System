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
        MapJsonFunctions(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            NameDefaultConstraints(entityType);
            ApplyQueryFilters(modelBuilder, entityType.ClrType);
        }
    }

    /// <summary>JSON_VALUE and a numeric TRY_CONVERT over it, for custom values in reports (<see cref="Application.Abstractions.SqlJson"/>).</summary>
    private static void MapJsonFunctions(ModelBuilder modelBuilder)
    {
        var value = typeof(Application.Abstractions.SqlJson).GetMethod(nameof(Application.Abstractions.SqlJson.Value))!;
        var number = typeof(Application.Abstractions.SqlJson).GetMethod(nameof(Application.Abstractions.SqlJson.Number))!;
        modelBuilder.HasDbFunction(value).HasName("JSON_VALUE").IsBuiltIn();
        modelBuilder.HasDbFunction(number).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRY_CONVERT",
                [
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("decimal(18,4)"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("JSON_VALUE", args, true, [true, false], typeof(string), args[0].TypeMapping),
                ],
                true, [false, true], typeof(decimal), null));
        var date = typeof(Application.Abstractions.SqlJson).GetMethod(nameof(Application.Abstractions.SqlJson.Date))!;
        modelBuilder.HasDbFunction(date).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRY_CONVERT",
                [
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("date"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("JSON_VALUE", args, true, [true, false], typeof(string), args[0].TypeMapping),
                ],
                true, [false, true], typeof(DateOnly), null));
        var fold = typeof(Application.Abstractions.SqlText).GetMethod(nameof(Application.Abstractions.SqlText.Fold))!;
        modelBuilder.HasDbFunction(fold).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRANSLATE",
                [
                    args[0],
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("N'" + Application.Abstractions.SqlText.From + "'"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("N'" + Application.Abstractions.SqlText.To + "'"),
                ],
                true, [true, false, false], typeof(string), args[0].TypeMapping));
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
