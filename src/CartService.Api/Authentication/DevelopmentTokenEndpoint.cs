namespace CartService.Api.Authentication;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

/// <summary>
/// A development convenience: it issues a token for any customer id, so a reviewer can try the API without an
/// identity provider. It is only mapped in the Development environment.
/// </summary>
internal static class DevelopmentTokenEndpoint
{
    public static IEndpointRouteBuilder MapDevelopmentTokenEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/dev/token", IssueToken)
            .WithName("IssueDevelopmentToken")
            .WithSummary("Issues a bearer token for a customer id. Development only.")
            .WithTags("Development")
            .AllowAnonymous();

        return endpoints;
    }

    private static IResult IssueToken(DevelopmentTokenRequest request, DevelopmentTokenService tokens)
    {
        if (request.CustomerId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(DevelopmentTokenRequest.CustomerId)] = new[] { "The customer id must not be empty." },
            });
        }

        DevelopmentToken token = tokens.Create(request.CustomerId);

        return Results.Ok(new DevelopmentTokenResponse
        {
            AccessToken = token.AccessToken,
            TokenType = "Bearer",
            ExpiresAt = token.ExpiresAt,
            CustomerId = request.CustomerId,
        });
    }
}
