namespace CartService.Application.Tests.Fakes;

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;

/// <summary>
/// Listens to the meter of the cart service and keeps what was measured. The meter is shared by the whole
/// process, so tests that use this recorder run in a collection that does not run in parallel with other tests.
/// </summary>
internal sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new MeterListener();
    private readonly List<Measurement> _measurements = new List<Measurement>();
    private readonly object _lock = new object();

    public MetricRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == CartTelemetry.Name)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(Record);
        _listener.SetMeasurementEventCallback<double>(Record);
        _listener.Start();
    }

    /// <summary>The sum of what an instrument measured, for measurements that carry all of the given tags.</summary>
    public double Sum(string instrument, params (string Key, string Value)[] tags)
    {
        lock (_lock)
        {
            return _measurements
                .Where(measurement => measurement.Instrument == instrument && tags.All(tag => measurement.Tags.TryGetValue(tag.Key, out string? value) && value == tag.Value))
                .Sum(measurement => measurement.Value);
        }
    }

    public int Count(string instrument)
    {
        lock (_lock)
        {
            return _measurements.Count(measurement => measurement.Instrument == instrument);
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void Record<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct
    {
        Dictionary<string, string?> tagValues = new Dictionary<string, string?>();

        foreach (KeyValuePair<string, object?> tag in tags)
        {
            tagValues[tag.Key] = tag.Value?.ToString();
        }

        lock (_lock)
        {
            _measurements.Add(new Measurement(instrument.Name, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), tagValues));
        }
    }

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, string?> Tags);
}
