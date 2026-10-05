namespace CartService.Api.Idempotency;

/// <summary>
/// Marks a request body that can be repeated safely with an <c>Idempotency-Key</c>. The idempotency filter
/// fingerprints it, so the same key with another body is recognised as a mistake of the client.
/// </summary>
/// <remarks>
/// This type must be public because the request types that implement it must be public, see
/// <see cref="Carts.AddItemRequest"/>.
/// </remarks>
public interface IIdempotentRequest
{
}
