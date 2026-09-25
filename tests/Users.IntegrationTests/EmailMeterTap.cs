using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ActualChat.Diagnostics;

namespace ActualChat.Users.IntegrationTests;

/// <summary>
/// Records every <c>emails.digest.*</c> measurement taken while it is alive, so tests can count
/// what the digest counters saw. Measurements of every app host in the process land here.
/// </summary>
internal sealed class EmailMeterTap : IDisposable
{
    private readonly ConcurrentQueue<(Instrument Instrument, KeyValuePair<string, object?>[] Tags)> _measurements
        = new();
    private readonly MeterListener _listener;

    public EmailMeterTap()
    {
        _listener = new MeterListener {
            InstrumentPublished = (instrument, listener) => {
                if (instrument.Meter == CoreServerInstruments.Meter && instrument.Name.StartsWith("emails.digest."))
                    listener.EnableMeasurementEvents(instrument);
            },
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) => _measurements.Enqueue((instrument, tags.ToArray())));
        _listener.Start();
    }

    public void Dispose()
        => _listener.Dispose();

    public int Count(Instrument instrument, params (string Key, string Value)[] tags)
        => _measurements.Count(m => m.Instrument == instrument
            && tags.All(t => m.Tags.Any(x => x.Key == t.Key && Equals(x.Value, t.Value))));
}
