namespace CartService.Application.Abstractions;

/// <summary>
/// What happened when a request with an idempotency key asked to start.
/// </summary>
public enum IdempotencyOutcome
{
    /// <summary>The key is new. The caller owns it, runs the request, and then completes or releases the key.</summary>
    Started = 0,

    /// <summary>Another request with the same key is still running.</summary>
    InProgress = 1,

    /// <summary>The key was used before for a different request.</summary>
    KeyReused = 2,

    /// <summary>A request with this key finished before. Its stored response must be returned again.</summary>
    Completed = 3,
}
