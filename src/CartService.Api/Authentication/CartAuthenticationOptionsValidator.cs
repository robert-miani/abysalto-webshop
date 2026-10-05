namespace CartService.Api.Authentication;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

/// <summary>
/// Stops the service from starting with an unsafe or incomplete authentication setup. Only a local environment
/// (Development, and Testing for the automated tests) may use a development signing key. Every other environment,
/// whatever its name (Staging, UAT, Production), must trust a real identity provider and must never accept tokens
/// signed with a development key, so a new environment name can never switch the protection off.
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

        if (!IsLocalEnvironment())
        {
            if (!hasAuthority)
            {
                return ValidateOptionsResult.Fail($"Authentication:Authority is required in the {_environment.EnvironmentName} environment.");
            }

            if (hasDevelopmentKey)
            {
                return ValidateOptionsResult.Fail($"Authentication:DevelopmentSigningKey must not be set in the {_environment.EnvironmentName} environment.");
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

    private bool IsLocalEnvironment()
    {
        return _environment.IsDevelopment() || _environment.IsEnvironment("Testing");
    }
}
