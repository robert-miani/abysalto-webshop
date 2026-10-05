namespace CartService.Application.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CartCachingTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();
    private readonly Guid _customerId = Guid.NewGuid();

    public CartCachingTests()
    {
        _environment.Catalog.Add("cap", "Red cap", 9.50m);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Requester Customer => Requester.ForCustomer(_customerId);

    [Fact]
    public async Task ReadingACartFillsTheCacheAndTheNextReadComesFromIt()
    {
        Cart cart = SeedCustomerCart();

        CartDto first = await GetCart().HandleAsync(cart.Id, Customer, Token);

        _environment.Cache.Contains(cart.Id).ShouldBeTrue();
        _environment.Cache.Hits.ShouldBe(0);

        // Change the database behind the cache's back: only a read from the cache still shows the old cart.
        cart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        CartDto second = await GetCart().HandleAsync(cart.Id, Customer, Token);

        _environment.Cache.Hits.ShouldBe(1);
        second.ShouldBeSameAs(first);
        second.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACacheHitStillChecksWhoOwnsTheCart()
    {
        Cart cart = SeedCustomerCart();
        await GetCart().HandleAsync(cart.Id, Customer, Token);

        await Should.ThrowAsync<CartNotFoundException>(
            () => GetCart().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), Token));
        await Should.ThrowAsync<CartNotFoundException>(
            () => GetCart().HandleAsync(cart.Id, Requester.ForGuest("some-hash"), Token));

        _environment.Cache.Hits.ShouldBe(2);
    }

    [Fact]
    public async Task ARequestForSomebodyElsesCartDoesNotFillTheCache()
    {
        Cart cart = SeedCustomerCart();

        await Should.ThrowAsync<CartNotFoundException>(
            () => GetCart().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), Token));

        _environment.Cache.Contains(cart.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task AnUnknownCartIsNotCached()
    {
        await Should.ThrowAsync<CartNotFoundException>(() => GetCart().HandleAsync(Guid.NewGuid(), Customer, Token));

        _environment.Cache.Hits.ShouldBe(0);
        _environment.Cache.Misses.ShouldBe(1);
    }

    [Fact]
    public async Task TheActiveCartOfACustomerIsCachedUnderTheCustomer()
    {
        Cart cart = SeedCustomerCart();

        CartDto first = await GetMyCart().HandleAsync(_customerId, Token);
        CartDto second = await GetMyCart().HandleAsync(_customerId, Token);

        _environment.Cache.ContainsActiveCartOf(_customerId).ShouldBeTrue();
        _environment.Cache.Hits.ShouldBe(1);
        second.ShouldBeSameAs(first);
        first.Id.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task ACustomerWithoutAnActiveCartCachesNothing()
    {
        await Should.ThrowAsync<CartNotFoundException>(() => GetMyCart().HandleAsync(_customerId, Token));

        _environment.Cache.ContainsActiveCartOf(_customerId).ShouldBeFalse();
    }

    [Fact]
    public async Task AddingAProductDropsTheCachedCartSoTheNextReadSeesIt()
    {
        Cart cart = SeedCustomerCart();
        await GetCart().HandleAsync(cart.Id, Customer, Token);
        await GetMyCart().HandleAsync(_customerId, Token);

        await new AddItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Catalog, _environment.Time, _environment.Cache)
            .HandleAsync(cart.Id, Customer, "cap", 1, Token);

        _environment.Cache.Contains(cart.Id).ShouldBeFalse();
        _environment.Cache.ContainsActiveCartOf(_customerId).ShouldBeFalse();
        (await GetCart().HandleAsync(cart.Id, Customer, Token)).Items.Select(item => item.ProductId).ShouldBe(new[] { "cap" });
    }

    [Fact]
    public async Task ChangingTheQuantityDropsTheCachedCart()
    {
        Cart cart = SeedCustomerCart("cap");
        await GetCart().HandleAsync(cart.Id, Customer, Token);

        await new ChangeItemQuantityHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time, _environment.Cache)
            .HandleAsync(cart.Id, Customer, "cap", 5, Token);

        _environment.Cache.Contains(cart.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task RemovingAProductDropsTheCachedCart()
    {
        Cart cart = SeedCustomerCart("cap");
        await GetCart().HandleAsync(cart.Id, Customer, Token);

        await new RemoveItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time, _environment.Cache)
            .HandleAsync(cart.Id, Customer, "cap", Token);

        _environment.Cache.Contains(cart.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task CheckoutDropsTheCachedCartAndTheActiveCartOfTheCustomer()
    {
        Cart cart = SeedCustomerCart("cap");
        await GetCart().HandleAsync(cart.Id, Customer, Token);
        await GetMyCart().HandleAsync(_customerId, Token);

        await new CheckoutHandler(_environment.Carts, _environment.UnitOfWork, _environment.Outbox, _environment.Time, _environment.Cache)
            .HandleAsync(cart.Id, Customer, Token);

        _environment.Cache.Contains(cart.Id).ShouldBeFalse();
        _environment.Cache.ContainsActiveCartOf(_customerId).ShouldBeFalse();
        await Should.ThrowAsync<CartNotFoundException>(() => GetMyCart().HandleAsync(_customerId, Token));
        (await GetCart().HandleAsync(cart.Id, Customer, Token)).Status.ShouldBe(CartStatus.CheckoutPending);
    }

    [Fact]
    public async Task MergingDropsTheCachedGuestCartAndTheCachedCustomerCart()
    {
        string guestHash = _environment.GuestTokens.Hash("secret");
        Cart guestCart = Cart.CreateForGuest(guestHash, HandlerEnvironment.Start);
        guestCart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        _environment.Carts.Seed(guestCart);
        Cart customerCart = SeedCustomerCart();
        await GetCart().HandleAsync(guestCart.Id, Requester.ForGuest(guestHash), Token);
        await GetCart().HandleAsync(customerCart.Id, Customer, Token);

        await new MergeGuestCartHandler(_environment.Carts, _environment.UnitOfWork, _environment.Time, _environment.Cache)
            .HandleAsync(_customerId, guestHash, Token);

        _environment.Cache.Contains(guestCart.Id).ShouldBeFalse();
        _environment.Cache.Contains(customerCart.Id).ShouldBeFalse();
        (await GetCart().HandleAsync(guestCart.Id, Requester.ForGuest(guestHash), Token)).Status.ShouldBe(CartStatus.Merged);
        (await GetCart().HandleAsync(customerCart.Id, Customer, Token)).Items.ShouldHaveSingleItem().ProductId.ShouldBe("cap");
    }

    [Fact]
    public async Task AChangeThatCouldNotBeSavedKeepsTheCachedCart()
    {
        Cart cart = SeedCustomerCart();
        await GetCart().HandleAsync(cart.Id, Customer, Token);
        _environment.UnitOfWork.FailWith = new ConcurrencyConflictException(new InvalidOperationException("stale"));

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => new AddItemHandler(_environment.Carts, _environment.UnitOfWork, _environment.Catalog, _environment.Time, _environment.Cache)
                .HandleAsync(cart.Id, Customer, "cap", 1, Token));

        // Nothing changed in the database, so what the cache holds is still right.
        _environment.Cache.Contains(cart.Id).ShouldBeTrue();
        _environment.Cache.Removals.ShouldBe(0);
    }

    private Cart SeedCustomerCart(params string[] productIds)
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);

        foreach (string productId in productIds)
        {
            cart.AddItem(productId, "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        }

        _environment.Carts.Seed(cart);

        return cart;
    }

    private GetCartHandler GetCart()
    {
        return new GetCartHandler(_environment.Carts, _environment.Cache);
    }

    private GetMyCartHandler GetMyCart()
    {
        return new GetMyCartHandler(_environment.Carts, _environment.Cache);
    }
}
