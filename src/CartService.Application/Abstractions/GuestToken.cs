namespace CartService.Application.Abstractions;

public sealed class GuestToken
{
    /// <summary>The secret. It is shown to the guest once and never stored.</summary>
    public required string Value { get; init; }

    /// <summary>The hash that is stored with the guest cart.</summary>
    public required string Hash { get; init; }
}
