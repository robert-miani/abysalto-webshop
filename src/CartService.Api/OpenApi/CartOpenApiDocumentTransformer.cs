namespace CartService.Api.OpenApi;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CartService.Api.Requesters;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

/// <summary>
/// Describes who may call what: a bearer token for customers and the cart token header for guests.
/// </summary>
internal sealed class CartOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string BearerScheme = "Bearer";
    private const string CartTokenScheme = "CartToken";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Cart API",
            Version = "v1",
            Description = "The shopping cart service of the retail platform. A signed-in customer sends a bearer token. "
                + "A guest creates a cart without a token, receives a secret cart token once, and sends it in the "
                + $"{RequesterResolver.GuestTokenHeader} header.",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "The access token of a signed-in customer. In development, POST /dev/token issues one.",
        };
        document.Components.SecuritySchemes[CartTokenScheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = RequesterResolver.GuestTokenHeader,
            Description = "The secret token of a guest cart, returned once when the guest cart is created.",
        };

        foreach (KeyValuePair<string, IOpenApiPathItem> path in document.Paths)
        {
            if (path.Value.Operations is null)
            {
                continue;
            }

            foreach (KeyValuePair<System.Net.Http.HttpMethod, OpenApiOperation> operation in path.Value.Operations)
            {
                AddSecurity(document, path.Key, operation.Key, operation.Value);
            }
        }

        return Task.CompletedTask;
    }

    private static void AddSecurity(OpenApiDocument document, string path, System.Net.Http.HttpMethod method, OpenApiOperation operation)
    {
        bool isCartEndpoint = path.StartsWith("/v1/carts", System.StringComparison.Ordinal);

        if (!isCartEndpoint || (method == System.Net.Http.HttpMethod.Post && path == "/v1/carts"))
        {
            // Creating a cart works with or without a token, and the token endpoint needs none.
            return;
        }

        operation.Security ??= new List<OpenApiSecurityRequirement>();

        if (path == "/v1/carts/me/merge")
        {
            // Merging needs both at once, in one requirement: the customer who receives the items, and the
            // secret token of the guest cart that gives them away.
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = new List<string>(),
                [new OpenApiSecuritySchemeReference(CartTokenScheme, document)] = new List<string>(),
            });

            return;
        }

        operation.Security.Add(Requirement(document, BearerScheme));

        if (path != "/v1/carts/me")
        {
            // Everything except "my cart" also accepts the token of a guest cart, as an alternative.
            operation.Security.Add(Requirement(document, CartTokenScheme));
        }
    }

    private static OpenApiSecurityRequirement Requirement(OpenApiDocument document, string scheme)
    {
        return new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(scheme, document)] = new List<string>(),
        };
    }
}
