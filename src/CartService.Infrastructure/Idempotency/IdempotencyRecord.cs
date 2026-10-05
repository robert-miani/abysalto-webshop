namespace CartService.Infrastructure.Idempotency;

using System;

/// <summary>
/// One request that was seen with an idempotency key. While it runs it is "in progress"; when it succeeds it keeps
/// its response, so a repeated request can be answered without running again.
/// </summary>
internal sealed class IdempotencyRecord
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";

    /// <summary>Who sent the request, so two requesters can use the same key independently.</summary>
    public required string Scope { get; init; }

    public required string Key { get; init; }

    /// <summary>A hash of what the request asked for. Another request with the same key must have the same one.</summary>
    public required string Fingerprint { get; init; }

    public required string Status { get; init; }

    public int? ResponseStatusCode { get; init; }

    public string? ResponseBody { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the record may be forgotten. For a running request this is the end of its lease, so a request that died
    /// does not block its key forever.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }
}
