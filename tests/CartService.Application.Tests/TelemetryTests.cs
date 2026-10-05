namespace CartService.Application.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Collection(TelemetryCollection.Name)]
[Trait("Category", "Unit")]
public sealed class TelemetryTests : IDisposable
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();
    private readonly MetricRecorder _metrics = new MetricRecorder();
    private readonly Guid _customerId = Guid.NewGuid();

    public TelemetryTests()
    {
        _environment.Catalog.Add("cap", "Red cap", 9.50m);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _metrics.Dispose();
    }

    [Fact]
    public async Task CreatingCartsIsCountedByOwnerKind()
    {
        CreateCartHandler handler = new CreateCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.GuestTokens, _environment.Time);

        await handler.HandleAsync(_customerId, Token);
        await handler.HandleAsync(null, Token);
        await handler.HandleAsync(null, Token);

        _metrics.Sum("cartservice.carts.created", ("kind", "customer")).ShouldBe(1);
        _metrics.Sum("cartservice.carts.created", ("kind", "guest")).ShouldBe(2);
    }

    [Fact]
    public async Task AskingForTheExistingCartIsNotACreation()
    {
        Cart existing = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(existing);

        await new CreateCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.GuestTokens, _environment.Time).HandleAsync(_customerId, Token);

        _metrics.Count("cartservice.carts.created").ShouldBe(0);
    }

    [Fact]
    public async Task AddedUnitsAreCounted()
    {
        Cart cart = SeedCart();
        AddItemHandler handler = new AddItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Catalog, _environment.Time, _environment.Cache);

        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), "cap", 2, Token);
        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), "cap", 3, Token);

        _metrics.Sum("cartservice.items.added").ShouldBe(5);
    }

    [Fact]
    public async Task ARejectedAddIsNotCounted()
    {
        Cart cart = SeedCart();
        AddItemHandler handler = new AddItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Catalog, _environment.Time, _environment.Cache);

        await Should.ThrowAsync<CartRuleViolationException>(
            () => handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), "unknown", 1, Token));

        _metrics.Count("cartservice.items.added").ShouldBe(0);
    }

    [Fact]
    public async Task ACheckoutIsCountedOnceEvenWhenRepeated()
    {
        Cart cart = SeedCart("cap");
        CheckoutHandler handler = new CheckoutHandler(_environment.Carts, _environment.UnitOfWork, _environment.Outbox, _environment.Time, _environment.Cache);

        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), Token);
        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), Token);

        _metrics.Sum("cartservice.checkouts").ShouldBe(1);
    }

    [Fact]
    public async Task AMergeIsCountedOnceEvenWhenRepeated()
    {
        string hash = _environment.GuestTokens.Hash("secret");
        Cart guest = Cart.CreateForGuest(hash, HandlerEnvironment.Start);
        guest.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        _environment.Carts.Seed(guest);
        MergeGuestCartHandler handler = new MergeGuestCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time, _environment.Cache);

        await handler.HandleAsync(_customerId, hash, Token);
        await handler.HandleAsync(_customerId, hash, Token);

        _metrics.Sum("cartservice.merges").ShouldBe(1);
    }

    [Fact]
    public async Task ReadsAreCountedAsHitsAndMisses()
    {
        Cart cart = SeedCart();
        GetCartHandler handler = new GetCartHandler(_environment.Carts, _environment.Cache);

        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), Token);
        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), Token);
        await handler.HandleAsync(cart.Id, Requester.ForCustomer(_customerId), Token);

        _metrics.Sum("cartservice.cache.lookups", ("result", "miss")).ShouldBe(1);
        _metrics.Sum("cartservice.cache.lookups", ("result", "hit")).ShouldBe(2);
    }

    private Cart SeedCart(params string[] productIds)
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);

        foreach (string productId in productIds)
        {
            cart.AddItem(productId, "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        }

        _environment.Carts.Seed(cart);

        return cart;
    }
}
