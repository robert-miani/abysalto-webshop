namespace CartService.Infrastructure.Outbox;

using System;
using CartService.Application.Abstractions;
using CartService.Application.Events;
using CartService.Infrastructure.Persistence;

/// <summary>
/// Adds the event to the same database context as the cart, so one <c>SaveChanges</c> writes both in one
/// transaction: either the cart changes and the event exists, or neither.
/// </summary>
internal sealed class OutboxWriter : IOutbox
{
    private readonly CartDbContext _context;

    public OutboxWriter(CartDbContext context)
    {
        _context = context;
    }

    public void Add(IntegrationEvent integrationEvent)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = integrationEvent.Type,
            OrderingKey = integrationEvent.OrderingKey,
            Payload = CloudEventSerializer.Serialize(integrationEvent),
            OccurredAt = integrationEvent.OccurredAt,
        });
    }
}
