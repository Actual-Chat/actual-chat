using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

/// <summary>
/// OpenTelemetry instruments for <c>ApiCommand</c> deduplication, on the already-registered
/// <see cref="CoreServerInstruments.Meter"/>.
/// </summary>
public static class IdempotencyMeters
{
    public static readonly Counter<long> Outcome;
    public static readonly Counter<long> Overrun;
    public static readonly Counter<long> Release;
    public static readonly Histogram<int> ResultSize;

    static IdempotencyMeters()
    {
        var m = CoreServerInstruments.Meter;
        Outcome = m.CreateCounter<long>("command.dedup.outcome", null,
            "ApiCommand dedup terminal outcomes, tagged by 'outcome' (executed/replayed/waited)");
        Overrun = m.CreateCounter<long>("command.dedup.overrun", null,
            "A claim outlived its in-progress TTL without a result — possible double run");
        Release = m.CreateCounter<long>("command.dedup.release", null,
            "ApiCommand dedup claims released after a failed command");
        ResultSize = m.CreateHistogram<int>("command.dedup.result_size", "By",
            "Serialized ApiCommand result size");
    }
}
