namespace CartService.Application.Abstractions;

public sealed class IdempotencyClaim
{
    public required IdempotencyOutcome Outcome { get; init; }

    /// <summary>The status code of the stored response. Only set when <see cref="Outcome"/> is Completed.</summary>
    public int? ResponseStatusCode { get; init; }

    /// <summary>The body of the stored response, if it had one. Only set when <see cref="Outcome"/> is Completed.</summary>
    public string? ResponseBody { get; init; }
}
