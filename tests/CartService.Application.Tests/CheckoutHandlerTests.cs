namespace CartService.Application.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using CartService.Application.Events;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class CheckoutHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();
    private readonly Guid _customerId = Guid.NewGuid();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Requester Customer => Requester.ForCustomer(_customerId);

    [Fact]
    public async Task CheckoutMakesTheCartReadOnlyAndStoresOneEventWithTheCartContent()
    {
        Cart cart = SeedCartWithItems();

        CheckoutResult result = await CreateHandler().HandleAsync(cart.Id, Customer, Token);

        cart.Status.ShouldBe(CartStatus.CheckoutPending);
        cart.CheckoutId.ShouldBe(result.CheckoutId);
        CartCheckedOut stored = _environment.Outbox.StoredEvents.ShouldHaveSingleItem().ShouldBeOfType<CartCheckedOut>();
        stored.CheckoutId.ShouldBe(result.CheckoutId);
        stored.CartId.ShouldBe(cart.Id);
        stored.CustomerId.ShouldBe(_customerId);
        stored.Currency.ShouldBe("EUR");
        stored.IndicativeTotal.ShouldBe(49.30m);
        stored.OccurredAt.ShouldBe(HandlerEnvironment.Start);
        stored.EventId.ShouldNotBe(Guid.Empty);
        stored.Type.ShouldBe("CartCheckedOut");
        stored.OrderingKey.ShouldBe(result.CheckoutId.ToString());
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task TheEventCarriesTheLinesWithTheirPriceSnapshotInAStableOrder()
    {
        Cart cart = SeedCartWithItems();

        await CreateHandler().HandleAsync(cart.Id, Customer, Token);

        CartCheckedOut stored = _environment.Outbox.StoredEvents.ShouldHaveSingleItem().ShouldBeOfType<CartCheckedOut>();
        stored.Items.Count.ShouldBe(2);
        stored.Items[0].ProductId.ShouldBe("cap");
        stored.Items[0].ProductName.ShouldBe("Red cap");
        stored.Items[0].Quantity.ShouldBe(1);
        stored.Items[0].UnitPrice.ShouldBe(9.50m);
        stored.Items[1].ProductId.ShouldBe("tee");
        stored.Items[1].Quantity.ShouldBe(2);
        stored.Items[1].UnitPrice.ShouldBe(19.90m);
    }

    [Fact]
    public async Task RepeatingCheckoutAnswersWithTheSameCheckoutAndPublishesNothingNew()
    {
        Cart cart = SeedCartWithItems();
        CheckoutResult first = await CreateHandler().HandleAsync(cart.Id, Customer, Token);

        CheckoutResult second = await CreateHandler().HandleAsync(cart.Id, Customer, Token);

        second.CheckoutId.ShouldBe(first.CheckoutId);
        _environment.Outbox.StoredEvents.ShouldHaveSingleItem();
        _environment.UnitOfWork.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task AnEmptyCartCannotBeCheckedOutAndNothingIsStored()
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateHandler().HandleAsync(cart.Id, Customer, Token));

        error.Code.ShouldBe(CartErrorCodes.Empty);
        _environment.Outbox.StoredEvents.ShouldBeEmpty();
        _environment.UnitOfWork.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task AGuestCannotCheckOut()
    {
        string hash = _environment.GuestTokens.Hash("secret");
        Cart cart = Cart.CreateForGuest(hash, HandlerEnvironment.Start);
        cart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 1, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        CartRuleViolationException error = await Should.ThrowAsync<CartRuleViolationException>(
            () => CreateHandler().HandleAsync(cart.Id, Requester.ForGuest(hash), Token));

        error.Code.ShouldBe(CartErrorCodes.CheckoutRequiresCustomer);
        _environment.Outbox.StoredEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnotherCustomerCannotCheckOutTheCart()
    {
        Cart cart = SeedCartWithItems();

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), Token));

        cart.Status.ShouldBe(CartStatus.Active);
        _environment.Outbox.StoredEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenTheCartCannotBeSavedNoEventIsStored()
    {
        Cart cart = SeedCartWithItems();
        _environment.UnitOfWork.FailWith = new ConcurrencyConflictException(new InvalidOperationException("stale"));

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => CreateHandler().HandleAsync(cart.Id, Customer, Token));

        _environment.Outbox.StoredEvents.ShouldBeEmpty();
    }

    private Cart SeedCartWithItems()
    {
        Cart cart = Cart.CreateForCustomer(_customerId, HandlerEnvironment.Start);
        cart.AddItem("tee", "T-shirt", Money.Eur(19.90m), 2, HandlerEnvironment.Start);
        cart.AddItem("cap", "Red cap", Money.Eur(9.50m), 1, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        return cart;
    }

    private CheckoutHandler CreateHandler()
    {
        return new CheckoutHandler(_environment.Carts, _environment.UnitOfWork, _environment.Outbox, _environment.Time, _environment.Cache);
    }
}
