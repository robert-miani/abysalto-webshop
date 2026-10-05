namespace CartService.Api.Carts;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.Authentication;
using CartService.Api.Requesters;
using CartService.Application.Carts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

internal static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder carts = endpoints.MapGroup("/v1/carts").WithTags("Carts");

        // A cart is personal data and must never be stored by a cache.
        carts.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";

            return await next(context);
        });

        carts.MapPost("/", CreateCart)
            .WithName("CreateCart")
            .WithSummary("Creates a cart. A customer gets their active cart; without a token a guest cart is created.")
            .WithDescription("A signed-in customer gets their one active cart: 201 with the cart when it was created, 200 when it already existed. "
                + "Without a token, a guest cart is created and 201 returns the cart together with its secret guestToken, which is shown only once.")
            .Produces<GuestCartCreatedResponse>(StatusCodes.Status201Created)
            .Produces<CartDto>(StatusCodes.Status200OK)
            .AllowAnonymous();

        carts.MapGet("/me", GetMyCart)
            .WithName("GetMyCart")
            .WithSummary("Returns the active cart of the signed-in customer.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthenticationRegistration.CustomerPolicy)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapGet("/{cartId:guid}", GetCart)
            .WithName("GetCart")
            .WithSummary("Returns a cart of the customer or the guest.")
            .WithDescription("A cart that does not exist and a cart of somebody else both answer 404, so nobody can find out which cart ids exist.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesProblems(StatusCodes.Status401Unauthorized, StatusCodes.Status404NotFound)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapPost("/{cartId:guid}/items", AddItem)
            .WithName("AddItem")
            .WithSummary("Adds a product to the cart. The price comes from the catalog.")
            .WithDescription("Adding a product that is already in the cart adds the quantities and takes the latest catalog name and price.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapPut("/{cartId:guid}/items/{productId}", ChangeQuantity)
            .WithName("ChangeItemQuantity")
            .WithSummary("Sets the quantity of a product in the cart.")
            .Produces<CartDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapDelete("/{cartId:guid}/items/{productId}", RemoveItem)
            .WithName("RemoveItem")
            .WithSummary("Removes a product from the cart. Removing a product that is not there is not an error.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblems(
                StatusCodes.Status401Unauthorized,
                StatusCodes.Status404NotFound,
                StatusCodes.Status409Conflict,
                StatusCodes.Status422UnprocessableEntity)
            .AddEndpointFilter<RequireRequesterFilter>();

        return endpoints;
    }

    private static async Task<IResult> CreateCart(
        HttpContext httpContext,
        RequesterResolver requesters,
        CreateCartHandler handler,
        CancellationToken cancellationToken)
    {
        Guid? customerId = requesters.Resolve(httpContext)?.CustomerId;

        CreateCartResult result = await handler.HandleAsync(customerId, cancellationToken);
        string location = $"/v1/carts/{result.Cart.Id}";

        if (result.GuestToken is not null)
        {
            return TypedResults.Created(location, new GuestCartCreatedResponse { GuestToken = result.GuestToken, Cart = result.Cart });
        }

        return result.Created ? TypedResults.Created(location, result.Cart) : TypedResults.Ok(result.Cart);
    }

    private static async Task<IResult> GetMyCart(
        HttpContext httpContext,
        GetMyCartHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(requester.CustomerId!.Value, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> GetCart(
        Guid cartId,
        HttpContext httpContext,
        GetCartHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> AddItem(
        Guid cartId,
        AddItemRequest request,
        HttpContext httpContext,
        AddItemHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, request.ProductId, request.Quantity, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> ChangeQuantity(
        Guid cartId,
        [StringLength(64, MinimumLength = 1)] string productId,
        ChangeQuantityRequest request,
        HttpContext httpContext,
        ChangeItemQuantityHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        CartDto cart = await handler.HandleAsync(cartId, requester, productId, request.Quantity, cancellationToken);

        return TypedResults.Ok(cart);
    }

    private static async Task<IResult> RemoveItem(
        Guid cartId,
        [StringLength(64, MinimumLength = 1)] string productId,
        HttpContext httpContext,
        RemoveItemHandler handler,
        CancellationToken cancellationToken)
    {
        Requester requester = RequireRequesterFilter.GetRequester(httpContext);
        await handler.HandleAsync(cartId, requester, productId, cancellationToken);

        return TypedResults.NoContent();
    }

    private static RouteHandlerBuilder ProducesProblems(this RouteHandlerBuilder builder, params int[] statusCodes)
    {
        foreach (int statusCode in statusCodes)
        {
            builder.ProducesProblem(statusCode);
        }

        return builder;
    }
}
