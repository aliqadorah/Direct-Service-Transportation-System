using Microsoft.EntityFrameworkCore;

namespace DirectService.Transportation.Infrastructure.Persistence;

/// <summary>
/// This assembly's part of the composed <see cref="MasterDbContext"/> (platform design d): every trn entity configuration under
/// Configurations, the foreign keys to acc tables by entity name (<see cref="CrossModuleRelationships"/>, Stage 3 session 3) and the SQL functions of the
/// reports. Its permission rows are seeded by the Platform contributor from <see cref="Seed.ReferenceDataSeed"/> (Stage 2 session 11).
/// </summary>
public sealed class TransportationModelContributor : IModelContributor
{
    public void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportationModelContributor).Assembly);
        CrossModuleRelationships.Apply(modelBuilder);
        MapJsonFunctions(modelBuilder);
    }

    /// <summary>JSON_VALUE and a numeric TRY_CONVERT over it, for custom values in reports (<see cref="SqlJson"/>).</summary>
    private static void MapJsonFunctions(ModelBuilder modelBuilder)
    {
        var value = typeof(SqlJson).GetMethod(nameof(SqlJson.Value))!;
        var number = typeof(SqlJson).GetMethod(nameof(SqlJson.Number))!;
        modelBuilder.HasDbFunction(value).HasName("JSON_VALUE").IsBuiltIn();
        modelBuilder.HasDbFunction(number).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRY_CONVERT",
                [
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("decimal(18,4)"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("JSON_VALUE", args, true, [true, false], typeof(string), args[0].TypeMapping),
                ],
                true, [false, true], typeof(decimal), null));
        var date = typeof(SqlJson).GetMethod(nameof(SqlJson.Date))!;
        modelBuilder.HasDbFunction(date).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRY_CONVERT",
                [
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("date"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("JSON_VALUE", args, true, [true, false], typeof(string), args[0].TypeMapping),
                ],
                true, [false, true], typeof(DateOnly), null));
        var fold = typeof(SqlText).GetMethod(nameof(SqlText.Fold))!;
        modelBuilder.HasDbFunction(fold).HasTranslation(args =>
            new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFunctionExpression("TRANSLATE",
                [
                    args[0],
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("N'" + SqlText.From + "'"),
                    new Microsoft.EntityFrameworkCore.Query.SqlExpressions.SqlFragmentExpression("N'" + SqlText.To + "'"),
                ],
                true, [true, false, false], typeof(string), args[0].TypeMapping));
    }
}
