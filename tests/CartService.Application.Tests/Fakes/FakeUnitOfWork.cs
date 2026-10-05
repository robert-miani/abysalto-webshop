namespace CartService.Application.Tests.Fakes;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly InMemoryCartRepository _repository;
    private readonly FakeOutbox _outbox;

    public FakeUnitOfWork(InMemoryCartRepository repository, FakeOutbox outbox)
    {
        _repository = repository;
        _outbox = outbox;
    }

    public int SaveCount { get; private set; }

    /// <summary>When set, the next save fails with this exception after running <see cref="BeforeFailure"/>.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Simulates what another request did in the meantime.</summary>
    public Action? BeforeFailure { get; set; }

    /// <summary>Runs after a save has succeeded, for example to simulate a client that disconnects right then.</summary>
    public Action? AfterSave { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            BeforeFailure?.Invoke();
            _repository.Discard();
            _outbox.Discard();
            Exception failure = FailWith;
            FailWith = null;

            return Task.FromException(failure);
        }

        _repository.Commit();
        _outbox.Commit();
        SaveCount++;
        AfterSave?.Invoke();

        return Task.CompletedTask;
    }
}
