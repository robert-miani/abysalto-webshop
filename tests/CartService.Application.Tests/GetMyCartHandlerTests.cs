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
public sealed class GetMyCartHandlerTests
{
    private readonly HandlerEnvironment _environment = new HandlerEnvironment();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACustomerGetsTheirActiveCart()
    {
        Guid customerId = Guid.NewGuid();
        Cart cart = Cart.CreateForCustomer(customerId, HandlerEnvironment.Start);
        _environment.Carts.Seed(cart);

        CartDto dto = await new GetMyCartHandler(_environment.Carts, _environment.Cache).HandleAsync(customerId, Token);

        dto.Id.ShouldBe(cart.Id);
    }

    [Fact]
    public async Task ACustomerWithoutAnActiveCartGetsNotFound()
    {
        await Should.ThrowAsync<CartNotFoundException>(
            () => new GetMyCartHandler(_environment.Carts, _environment.Cache).HandleAsync(Guid.NewGuid(), Token));
    }
}
