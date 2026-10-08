using DirectService.Transportation.Application.Abstractions;
using DirectService.Transportation.Domain.Core;
using DirectService.Transportation.Infrastructure.Persistence;
using DirectService.Transportation.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Tests;

/// <summary>
/// Creates the integration-test database from the migrations once per test run. Tests isolate
/// themselves by creating their own tenant/company (ADR-18) — no data is ever deleted.
/// Connection: DST_TEST_DB environment variable, else the local SQL Server with Windows authentication.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("DST_TEST_DB")
        ?? "Server=localhost;Database=DirectServiceMaster_Tests;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    public async ValueTask InitializeAsync()
    {
        await using var ctx = CreateContext(new DataScope());
        // The test database is disposable test infrastructure (not business data): recreate it from the migrations.
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.MigrateAsync();
        // Same isolation behaviour as production (database/00_CreateDatabase.sql).
        await ctx.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public MasterDbContext CreateContext(DataScope scope, TimeProvider? clock = null)
    {
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(new AuditSaveChangesInterceptor(scope, new ChangeContext(), clock ?? TimeProvider.System))
            .Options;
        return new MasterDbContext(options, scope);
    }

    /// <summary>Creates a tenant with one user and one company; returns their ids.</summary>
    public async Task<TestTenant> CreateTenantAsync(string? companyName = null)
    {
        var scope = new DataScope();
        await using var ctx = CreateContext(scope);
        var tenant = new Tenant { Name = "Test tenant " + Guid.NewGuid().ToString("N")[..8], PlanId = 1 };
        ctx.Add(tenant);
        await ctx.SaveChangesAsync();

        scope.Set(tenant.Id, null, null);
        var email = $"owner-{Guid.NewGuid():N}@example.test";
        var user = new User
        {
            FullName = "Owner",
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            PasswordHash = "AQAAAAIAAYagAAAAEtest-hash",
            SecurityStamp = Guid.NewGuid().ToString("N"),
        };
        var company = new Company { Name = companyName ?? "Company " + Guid.NewGuid().ToString("N")[..8] };
        ctx.AddRange(user, company);
        await ctx.SaveChangesAsync();
        return new TestTenant(tenant.Id, company.Id, user.Id);
    }

    /// <summary>Adds a second company to an existing tenant.</summary>
    public async Task<long> AddCompanyAsync(TestTenant t)
    {
        var scope = new DataScope();
        scope.Set(t.TenantId, null, t.UserId);
        await using var ctx = CreateContext(scope);
        var company = new Company { Name = "Company " + Guid.NewGuid().ToString("N")[..8] };
        ctx.Add(company);
        await ctx.SaveChangesAsync();
        return company.Id;
    }

    public (MasterDbContext Ctx, DataScope Scope) ContextFor(TestTenant t, long? companyId = null)
    {
        var scope = new DataScope();
        scope.Set(t.TenantId, companyId ?? t.CompanyId, t.UserId, "127.0.0.1");
        return (CreateContext(scope), scope);
    }
}

public sealed record TestTenant(long TenantId, long CompanyId, long UserId);

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
