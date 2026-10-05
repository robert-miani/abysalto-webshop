namespace CartService.Infrastructure.Messaging;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Where the outbox relay publishes events.
/// </summary>
public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";

    /// <summary>
    /// A connection string, which is how the local emulator is reached. Real environments leave it empty and set
    /// <see cref="FullyQualifiedNamespace"/> instead, so no secret is needed.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// The namespace of the Azure Service Bus, for example <c>retail-prod.servicebus.windows.net</c>. The service
    /// signs in with its workload identity.
    /// </summary>
    public string? FullyQualifiedNamespace { get; init; }

    /// <summary>The topic that receives the events of the cart service.</summary>
    [Required]
    public string TopicName { get; init; } = "cart-events";
}
