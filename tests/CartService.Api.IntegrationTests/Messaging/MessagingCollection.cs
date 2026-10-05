namespace CartService.Api.IntegrationTests.Messaging;

using CartService.Api.IntegrationTests.Persistence;
using Xunit;

/// <summary>
/// Tests that need both a database and a message broker. Starting the emulator takes a minute, so all of them
/// share one emulator.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MessagingCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<ServiceBusFixture>
{
    public const string Name = "Messaging";
}
