namespace CartService.Infrastructure.Cleanup;

/// <summary>How many rows one run of the cleanup deleted.</summary>
internal sealed class CleanupResult
{
    public int Carts { get; init; }

    public int OutboxMessages { get; init; }

    public int IdempotencyRecords { get; init; }

    public int Total => Carts + OutboxMessages + IdempotencyRecords;
}
