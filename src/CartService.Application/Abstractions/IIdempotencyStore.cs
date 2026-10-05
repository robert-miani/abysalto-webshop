namespace CartService.Application.Abstractions;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Remembers requests by their idempotency key, so a request that a client repeats, for example because it did not
/// get the answer, is not carried out twice. Keys are scoped: two requesters can use the same key without
/// affecting each other.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Asks to start a request. Exactly one of several requests with the same scope and key gets
    /// <see cref="IdempotencyOutcome.Started"/>; the others learn that it is running or what it answered. The
    /// fingerprint identifies the content of the request: the same key with another fingerprint is a mistake of
    /// the client.
    /// </summary>
    Task<IdempotencyClaim> BeginAsync(string scope, string key, string fingerprint, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the response of a request that succeeded, so repeating the request returns it again.
    /// </summary>
    Task CompleteAsync(string scope, string key, int responseStatusCode, string? responseBody, CancellationToken cancellationToken);

    /// <summary>
    /// Gives the key back after a request failed. A failed request changed nothing, so the client may use the same
    /// key again.
    /// </summary>
    Task ReleaseAsync(string scope, string key, CancellationToken cancellationToken);
}
