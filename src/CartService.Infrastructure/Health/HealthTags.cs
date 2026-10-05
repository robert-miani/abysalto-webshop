namespace CartService.Infrastructure.Health;

public static class HealthTags
{
    /// <summary>Marks the checks that decide whether the service is ready to receive traffic.</summary>
    public const string Ready = "ready";
}
