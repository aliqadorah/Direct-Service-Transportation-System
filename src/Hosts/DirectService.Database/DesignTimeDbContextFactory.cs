using DirectService.Shared.Infrastructure.Persistence;
using DirectService.Transportation.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace DirectService.Database;

/// <summary>
/// Used only by the EF Core tools (migrations, script). Connection string from
/// ConnectionStrings:MasterDb in user-secrets or the DST_ConnectionStrings__MasterDb environment
/// variable; falls back to the local development database (Windows authentication, no secret).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MasterDbContext>
{
    public const string LocalDevelopment =
        "Server=localhost;Database=DirectServiceMaster;Integrated Security=true;TrustServerCertificate=true;Encrypt=true";

    public MasterDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables("DST_")
            .Build();
        var cs = config.GetConnectionString("MasterDb") ?? LocalDevelopment;
        var options = new DbContextOptionsBuilder<MasterDbContext>()
            .UseMasterDatabase(cs)
            .Options;
        return new MasterDbContext(options, new DataScope(), MasterModel.Contributors);
    }
}
