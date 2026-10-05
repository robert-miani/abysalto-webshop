namespace CartService.Api.IntegrationTests.Messaging;

using System;
using System.IO;
using System.Threading.Tasks;
using Testcontainers.ServiceBus;
using Xunit;

/// <summary>
/// One Azure Service Bus emulator for all messaging tests. It is configured with the same topic and subscription
/// as Docker Compose, from the shared file in infra/servicebus-emulator.
/// </summary>
public sealed class ServiceBusFixture : IAsyncLifetime
{
    public const string TopicName = "cart-events";
    public const string SubscriptionName = "order-service";

    private const string Image = "mcr.microsoft.com/azure-messaging/servicebus-emulator:2.0.1";

    private readonly ServiceBusContainer _container = new ServiceBusBuilder(Image)
        .WithAcceptLicenseAgreement(true)
        .WithConfig(Path.Combine(AppContext.BaseDirectory, "ServiceBus", "Config.json"))
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
