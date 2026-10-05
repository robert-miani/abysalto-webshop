namespace CartService.Api.IntegrationTests.Persistence;

using Xunit;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}
