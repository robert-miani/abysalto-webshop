namespace CartService.Api.Authentication;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Stops the service from starting with an unsafe or incomplete authentication setup. Production must trust a
/// real identity provider and must never accept tokens signed with a development key.
/// </summary>
internal sealed class CartAuthenticationOptionsValidator : IValidateOptions<CartAuthenticationOptions>
{
    internal const int MinimumSigningKeyLength = 32;

    private readonly IHostEnvironment _environment;

    public CartAuthenticationOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, CartAuthenticationOptions options)
    {
        bool hasAuthority = !string.IsNullOrWhiteSpace(options.Authority);
        bool hasDevelopmentKey = !string.IsNullOrWhiteSpace(options.DevelopmentSigningKey);

        if (_environment.IsProduction())
        {
            if (!hasAuthority)
            {
                return ValidateOptionsResult.Fail("Authentication:Authority is required in production.");
            }

            if (hasDevelopmentKey)
            {
                return ValidateOptionsResult.Fail("Authentication:DevelopmentSigningKey must not be set in production.");
            }

            return ValidateOptionsResult.Success;
        }

        if (!hasAuthority && !hasDevelopmentKey)
        {
            return ValidateOptionsResult.Fail("Set Authentication:Authority, or Authentication:DevelopmentSigningKey for local use.");
        }

        if (!hasAuthority && options.DevelopmentSigningKey!.Length < MinimumSigningKeyLength)
        {
            return ValidateOptionsResult.Fail(
                $"Authentication:DevelopmentSigningKey must have at least {MinimumSigningKeyLength} characters.");
        }

        return ValidateOptionsResult.Success;
    }
}
