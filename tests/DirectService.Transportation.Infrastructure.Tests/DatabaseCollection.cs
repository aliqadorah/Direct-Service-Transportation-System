namespace DirectService.Transportation.Infrastructure.Tests;

/// <summary>The shared database fixture (DirectService.Testing) for the database test classes; xUnit finds collection definitions only in the test assembly.</summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
