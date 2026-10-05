namespace CartService.Application;

using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;

/// <summary>
/// The traces and metrics of the cart service. They use only the .NET diagnostics APIs, so the code has no
/// dependency on a vendor; the host decides where they go (see OpenTelemetry in the API project).
/// </summary>
public static class CartTelemetry
{
    /// <summary>The name of the activity source and of the meter. The host subscribes to it by this name.</summary>
    public const string Name = "CartService";

    public static readonly ActivitySource ActivitySource = new ActivitySource(Name);

    private static readonly Meter Meter = new Meter(Name);

    /// <summary>A tag for a measurement, for example <c>Tag("result", "hit")</c>.</summary>
    public static KeyValuePair<string, object?> Tag(string key, string value)
    {
        return new KeyValuePair<string, object?>(key, value);
    }

    public static readonly Counter<long> CartsCreated = Meter.CreateCounter<long>(
        "cartservice.carts.created",
        unit: "{cart}",
        description: "Carts that were created, by owner kind (customer or guest).");

    public static readonly Counter<long> ItemsAdded = Meter.CreateCounter<long>(
        "cartservice.items.added",
        unit: "{item}",
        description: "Units that were added to carts.");

    public static readonly Counter<long> Checkouts = Meter.CreateCounter<long>(
        "cartservice.checkouts",
        unit: "{checkout}",
        description: "Checkouts that were started. A repeated request for the same checkout is not counted.");

    public static readonly Counter<long> Merges = Meter.CreateCounter<long>(
        "cartservice.merges",
        unit: "{merge}",
        description: "Guest carts that were merged into a customer cart.");

    public static readonly Counter<long> CacheLookups = Meter.CreateCounter<long>(
        "cartservice.cache.lookups",
        unit: "{lookup}",
        description: "Cart reads, by result (hit or miss). Without a cache every read is a miss.");

    public static readonly Counter<long> IdempotencyRequests = Meter.CreateCounter<long>(
        "cartservice.idempotency.requests",
        unit: "{request}",
        description: "Requests with an Idempotency-Key, by outcome (started, replayed, in_progress, key_reused).");

    public static readonly Counter<long> OutboxPublished = Meter.CreateCounter<long>(
        "cartservice.outbox.published",
        unit: "{message}",
        description: "Outbox messages the relay tried to publish, by result (success or failure).");

    public static readonly Histogram<double> OutboxPublishDuration = Meter.CreateHistogram<double>(
        "cartservice.outbox.publish.duration",
        unit: "s",
        description: "How long publishing one outbox message to the broker took.");

    public static readonly Counter<long> CleanupDeleted = Meter.CreateCounter<long>(
        "cartservice.cleanup.deleted",
        unit: "{row}",
        description: "Rows the cleanup job deleted, by kind (carts, outbox_messages, idempotency_records).");
}
