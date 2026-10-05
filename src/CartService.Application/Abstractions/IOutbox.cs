namespace CartService.Application.Abstractions;

using CartService.Application.Events;

/// <summary>
/// Records events that must be published after the current change is saved. The event is stored in the same
/// transaction as the change, so it is published if and only if the change is saved.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Adds the event to the current unit of work. It is written by <see cref="IUnitOfWork.SaveChangesAsync"/>.
    /// </summary>
    void Add(IntegrationEvent integrationEvent);
}
