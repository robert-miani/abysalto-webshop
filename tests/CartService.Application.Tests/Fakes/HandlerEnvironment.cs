namespace CartService.Application.Tests.Fakes;

using System;
using Microsoft.Extensions.Time.Testing;

/// <summary>
/// The fakes that every handler test needs, wired the way the real services are.
/// </summary>
internal sealed class HandlerEnvironment
{
    public static readonly DateTimeOffset Start = new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero);

    public HandlerEnvironment()
    {
        Carts = new InMemoryCartRepository();
        Outbox = new FakeOutbox();
        UnitOfWork = new FakeUnitOfWork(Carts, Outbox);
        Catalog = new FakeProductCatalog();
        GuestTokens = new FakeGuestTokenService();
        Time = new FakeTimeProvider(Start);
    }

    public InMemoryCartRepository Carts { get; }

    public FakeOutbox Outbox { get; }

    public FakeUnitOfWork UnitOfWork { get; }

    public FakeProductCatalog Catalog { get; }

    public FakeGuestTokenService GuestTokens { get; }

    public FakeTimeProvider Time { get; }
}
