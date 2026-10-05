namespace CartService.Api.Authentication;

using System;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

internal static class AuthenticationRegistration
{
    /// <summary>The name of the policy for endpoints that only a signed-in customer may call.</summary>
    public const string CustomerPolicy = "Customer";

    public static IServiceCollection AddCartAuthentication(this IServiceCollection services)
    {
        services.AddOptions<CartAuthenticationOptions>()
            .BindConfiguration(CartAuthenticationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CartAuthenticationOptions>, CartAuthenticationOptionsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<CartAuthenticationOptions>>(ConfigureJwtBearer);

        services.AddAuthorization();
        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<CartAuthenticationOptions>>(ConfigureAuthorization);

        services.AddSingleton<DevelopmentTokenService>();

        return services;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, IOptions<CartAuthenticationOptions> settings)
    {
        CartAuthenticationOptions options = settings.Value;

        // Keep the claim names of the token as they are, so "oid" stays "oid".
        jwt.MapInboundClaims = false;
        jwt.Audience = options.Audience;

        if (!string.IsNullOrWhiteSpace(options.Authority))
        {
            // The signing keys are downloaded from the identity provider.
            jwt.Authority = options.Authority;
            return;
        }

        jwt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = options.DevelopmentIssuer,
            ValidateAudience = true,
            ValidAudience = options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.DevelopmentSigningKey!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    }

    private static void ConfigureAuthorization(AuthorizationOptions authorization, IOptions<CartAuthenticationOptions> settings)
    {
        authorization.AddPolicy(CustomerPolicy, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(settings.Value.CustomerIdClaim));
    }
}
