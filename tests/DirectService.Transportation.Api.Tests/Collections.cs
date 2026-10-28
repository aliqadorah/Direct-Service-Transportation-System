namespace DirectService.Transportation.Api.Tests;

// The shared factories live in DirectService.Testing; xUnit finds collection definitions only in the test assembly.

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}

[CollectionDefinition(Name)]
public sealed class SystemEmailCollection : ICollectionFixture<SystemEmailFactory>
{
    public const string Name = "SystemEmail";
}
