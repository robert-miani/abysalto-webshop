namespace CartService.Api.Carts;

using System;
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
            .AllowAnonymous();

        carts.MapGet("/me", GetMyCart)
            .WithName("GetMyCart")
            .WithSummary("Returns the active cart of the signed-in customer.")
            .RequireAuthorization(AuthenticationRegistration.CustomerPolicy)
            .AddEndpointFilter<RequireRequesterFilter>();

        carts.MapGet("/{cartId:guid}", GetCart)
            .WithName("GetCart")
            .WithSummary("Returns a cart of the customer or the guest.")
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
}
