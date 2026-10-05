namespace CartService.Api.IntegrationTests.Caching;

using CartService.Api.IntegrationTests.Persistence;
using Xunit;

/// <summary>
/// For tests that need both databases: the API reads from PostgreSQL and caches in Redis.
/// </summary>
[CollectionDefinition(Name)]
public sealed class CacheCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture>
{
    public const string Name = "Cache";
}
