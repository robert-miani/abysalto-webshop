namespace CartService.Api.IntegrationTests.Caching;

using Xunit;

[CollectionDefinition(Name)]
public sealed class RedisCollection : ICollectionFixture<RedisFixture>
{
    public const string Name = "Redis";
}
