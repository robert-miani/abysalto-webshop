namespace CartService.Api.Requesters;

using System.Threading.Tasks;
using CartService.Application.Carts;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

/// <summary>
/// Answers 401 when a request proves neither a customer nor a guest cart. Otherwise it makes the requester
/// available to the endpoint, so the endpoint never has to deal with a missing requester.
/// </summary>
internal sealed class RequireRequesterFilter : IEndpointFilter
{
    private const string RequesterKey = "CartService.Requester";

    private readonly RequesterResolver _resolver;

    public RequireRequesterFilter(RequesterResolver resolver)
    {
        _resolver = resolver;
    }

    public static Requester GetRequester(HttpContext httpContext)
    {
        return (Requester)httpContext.Items[RequesterKey]!;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        Requester? requester = _resolver.Resolve(context.HttpContext);

        if (requester is null)
        {
            context.HttpContext.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";

            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.",
                detail: $"Send a bearer token, or the {RequesterResolver.GuestTokenHeader} header of a guest cart.");
        }

        context.HttpContext.Items[RequesterKey] = requester;

        return await next(context);
    }
}
