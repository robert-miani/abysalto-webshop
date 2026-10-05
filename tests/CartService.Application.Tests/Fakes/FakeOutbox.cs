namespace CartService.Application.Tests.Fakes;

using System.Collections.Generic;
using CartService.Application.Abstractions;
using CartService.Application.Events;

/// <summary>
/// Behaves like the real outbox: an event only counts as stored when the unit of work saves successfully.
/// </summary>
internal sealed class FakeOutbox : IOutbox
{
    private readonly List<IntegrationEvent> _stored = new List<IntegrationEvent>();
    private readonly List<IntegrationEvent> _pending = new List<IntegrationEvent>();

    public IReadOnlyList<IntegrationEvent> StoredEvents => _stored;

    public void Add(IntegrationEvent integrationEvent)
    {
        _pending.Add(integrationEvent);
    }

    internal void Commit()
    {
        _stored.AddRange(_pending);
        _pending.Clear();
    }

    internal void Discard()
    {
        _pending.Clear();
    }
}
