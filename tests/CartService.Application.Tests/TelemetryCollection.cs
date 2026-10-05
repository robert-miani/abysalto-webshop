namespace CartService.Application.Tests;

using Xunit;

/// <summary>
/// The meter of the service is shared by the whole process, so a test that counts measurements must not run
/// at the same time as another test that makes some.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryCollection
{
    public const string Name = "Telemetry";
}
