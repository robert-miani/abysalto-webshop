namespace CartService.Application.Tests.Fakes;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly InMemoryCartRepository _repository;

    public FakeUnitOfWork(InMemoryCartRepository repository)
    {
        _repository = repository;
    }

    public int SaveCount { get; private set; }

    /// <summary>When set, the next save fails with this exception after running <see cref="BeforeFailure"/>.</summary>
    public Exception? FailWith { get; set; }

    /// <summary>Simulates what another request did in the meantime.</summary>
    public Action? BeforeFailure { get; set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        if (FailWith is not null)
        {
            BeforeFailure?.Invoke();
            _repository.Discard();
            Exception failure = FailWith;
            FailWith = null;

            return Task.FromException(failure);
        }

        _repository.Commit();
        SaveCount++;

        return Task.CompletedTask;
    }
}
