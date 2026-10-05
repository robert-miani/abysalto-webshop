namespace CartService.Application.Tests.Fakes;

using CartService.Application.Abstractions;

internal sealed class FakeGuestTokenService : IGuestTokenService
{
    private int _counter;

    public GuestToken Create()
    {
        _counter++;
        string token = $"guest-token-{_counter}";

        return new GuestToken { Value = token, Hash = Hash(token) };
    }

    public string Hash(string token)
    {
        return $"hash:{token}";
    }
}
