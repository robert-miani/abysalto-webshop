namespace CartService.Api.Requesters;

using System;
using System.Security.Claims;
using CartService.Api.Authentication;
using CartService.Application.Abstractions;
using CartService.Application.Carts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

/// <summary>
/// Decides who is asking. A signed-in customer is identified by the customer id claim of the validated token.
/// A guest is identified by the secret token of the guest cart in the <c>X-Cart-Token</c> header. If both are
/// present, the customer wins.
/// </summary>
internal sealed class RequesterResolver
{
    public const string GuestTokenHeader = "X-Cart-Token";

    private readonly IGuestTokenService _guestTokens;
    private readonly IOptions<CartAuthenticationOptions> _options;

    public RequesterResolver(IGuestTokenService guestTokens, IOptions<CartAuthenticationOptions> options)
    {
        _guestTokens = guestTokens;
        _options = options;
    }

    /// <summary>
    /// Returns the requester, or null when the request proves neither a customer nor a guest cart.
    /// </summary>
    public Requester? Resolve(HttpContext httpContext)
    {
        ClaimsPrincipal user = httpContext.User;

        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(_options.Value.CustomerIdClaim), out Guid customerId)
            && customerId != Guid.Empty)
        {
            return Requester.ForCustomer(customerId);
        }

        string? guestToken = httpContext.Request.Headers[GuestTokenHeader].ToString();

        if (!string.IsNullOrWhiteSpace(guestToken))
        {
            return Requester.ForGuest(_guestTokens.Hash(guestToken));
        }

        return null;
    }
}
