namespace CartService.Api.Authentication;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// How the API checks the bearer tokens of signed-in customers.
/// </summary>
public sealed class CartAuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>The audience that tokens must have, so a token issued for another API is rejected.</summary>
    [Required]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// The address of the identity provider, which is Microsoft Entra External ID in the platform. The API
    /// downloads the signing keys from there. Required in production.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>
    /// The claim that holds the customer id. It is <c>oid</c> (the object id), not <c>sub</c>, because Entra
    /// gives one customer a different <c>sub</c> in every app, which would split a customer's cart across
    /// devices.
    /// </summary>
    [Required]
    public string CustomerIdClaim { get; init; } = "oid";

    /// <summary>
    /// A local signing key for development and tests, used when no authority is configured. It is refused in
    /// production.
    /// </summary>
    public string? DevelopmentSigningKey { get; init; }

    public string DevelopmentIssuer { get; init; } = "cartservice-dev";
}
