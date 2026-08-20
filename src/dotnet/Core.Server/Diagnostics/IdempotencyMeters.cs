using System.Diagnostics.Metrics;

namespace ActualChat.Diagnostics;

/// <summary>
/// OpenTelemetry instruments for <c>ApiCommand</c> deduplication, on the already-registered
/// <see cref="CoreServerInstruments.Meter"/>.
/// </summary>
public static class IdempotencyMeters
{
    public static readonly Counter<long> Outcome;
    public static readonly Counter<long> Reclaim;
    public static readonly Counter<long> Overrun;
    public static readonly Counter<long> Release;
    public static readonly Histogram<int> ResultSize;
    public static readonly Histogram<double> StoreDuration;

    static IdempotencyMeters()
    {
        var m = CoreServerInstruments.Meter;
        Outcome = m.CreateCounter<long>("command.dedup.outcome", null,
            "ApiCommand dedup terminal outcomes, tagged by 'outcome' (executed/replayed/waited)");
        Reclaim = m.CreateCounter<long>("command.dedup.reclaim", null,
            "ApiCommand dedup claims reclaimed from a dead owner node");
        Overrun = m.CreateCounter<long>("command.dedup.overrun", null,
            "In-progress marker expired without a result (WaitForResult timed out) — possible double run");
        Release = m.CreateCounter<long>("command.dedup.release", null,
            "ApiCommand dedup claims released after a failed command");
        ResultSize = m.CreateHistogram<int>("command.dedup.result_size", "By",
            "Serialized ApiCommand result size");
        StoreDuration = m.CreateHistogram<double>("command.dedup.store.duration", "ms",
            "Idempotency store op latency, tagged by 'op' (claim/complete)");
    }
}
