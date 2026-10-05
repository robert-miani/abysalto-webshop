namespace CartService.Application.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Carts;
using CartService.Application.Tests.Fakes;
using CartService.Domain;
using Shouldly;
using Xunit;

[Trait("Category", "Unit")]
public sealed class GetCartHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCustomerWhoOwnsACartCanReadIt()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = SeedCustomerCartWithATee(customerId);

        CartDto dto = await CreateHandler().HandleAsync(cart.Id, Requester.ForCustomer(customerId), Token);

        dto.Id.ShouldBe(cart.Id);
        dto.Status.ShouldBe(CartStatus.Active);
        dto.Currency.ShouldBe("EUR");
        dto.Total.ShouldBe(20m);
        CartItemDto item = dto.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe("tee");
        item.ProductName.ShouldBe("T-shirt");
        item.UnitPrice.ShouldBe(10m);
        item.Quantity.ShouldBe(2);
        item.LineTotal.ShouldBe(20m);
    }

    [Fact]
    public async Task TheGuestWithTheRightTokenCanReadTheGuestCart()
    {
        Cart cart = Cart.CreateForGuest(_environment.GuestTokens.Hash("secret"), HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        CartDto dto = await CreateHandler().HandleAsync(cart.Id, Requester.ForGuest(_environment.GuestTokens.Hash("secret")), Token);

        dto.Id.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task AnotherCustomerGetsNotFound()
    {
        Cart cart = SeedCustomerCartWithATee(Guid.NewGuid());

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(cart.Id, Requester.ForCustomer(Guid.NewGuid()), Token));
    }

    [Fact]
    public async Task AGuestWithTheWrongTokenGetsNotFound()
    {
        Cart cart = Cart.CreateForGuest(_environment.GuestTokens.Hash("secret"), HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(cart.Id, Requester.ForGuest(_environment.GuestTokens.Hash("other")), Token));
    }

    [Fact]
    public async Task AGuestCannotReadACustomerCart()
    {
        Cart cart = SeedCustomerCartWithATee(Guid.NewGuid());

        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(cart.Id, Requester.ForGuest(_environment.GuestTokens.Hash("secret")), Token));
    }

    [Fact]
    public async Task AnUnknownCartGetsTheSameNotFoundAsAnotherCustomersCart()
    {
        await Should.ThrowAsync<CartNotFoundException>(
            () => CreateHandler().HandleAsync(Guid.NewGuid(), Requester.ForCustomer(Guid.NewGuid()), Token));
    }

    private Cart SeedCustomerCartWithATee(Guid customerId)
    {
        Cart cart = Cart.CreateForCustomer(customerId, HandlerEnvironment.Start);
        cart.AddItem("tee", "T-shirt", Money.Eur(10m), 2, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        return cart;
    }

    private GetCartHandler CreateHandler()
    {
        return new GetCartHandler(_environment.Carts, _environment.Cache);
    }
}
