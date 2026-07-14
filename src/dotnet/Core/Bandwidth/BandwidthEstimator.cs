using ActualChat.Rpc;

namespace ActualChat.Bandwidth;

public enum BandwidthVerdict { Bad, Neutral, Good }

public sealed record BandwidthEstimatorConfig(long InitialCeilingBps)
{
    public double BaseStep                { get; init; } = 0.20;
    public double MaxStep                 { get; init; } = 0.50;
    public double MaxStepUp               { get; init; } = 0.20;
    public double AsymRatio               { get; init; } = 0.30;
    public double NegStreakStep           { get; init; } = 0.15;
    public double PosStreakStep           { get; init; } = 0.05;
    public double GoodThreshold           { get; init; } = 0.95;
    public double BadThreshold            { get; init; } = 0.85;
    public double ConfirmRatio            { get; init; } = 0.85;
    public double TauSec                  { get; init; } = 30;
    public double MinAgeFactor            { get; init; } = 0.30;
    public long   FloorBps                { get; init; } = 50_000;
    public int    HistorySize             { get; init; } = 10;
    public double BaseProbeCooldownSec    { get; init; } = 5;
    public double ProbeCooldownGrowth     { get; init; } = 1.7;
    public double MaxProbeCooldownSec      { get; init; } = 120;
    public int    ProbeFailureResetStreak  { get; init; } = 20;
    public double MeasuredCapacityLagRatio { get; init; } = 0.9;
    public Func<double, BandwidthVerdict>? Classify { get; init; }
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct BandwidthEstimateRecord(
    Moment At,
    long TriedBps,
    double SignalLevel,
    long CeilingBefore,
    long CeilingAfter,
    BandwidthVerdict Verdict);

/// <summary>
/// Tracks one number — <see cref="CeilingBps"/>, the best estimate of the
/// available bandwidth on the current RPC connection. Driven by a single
/// fused <c>signalLevel</c> in [0, 1]: 1.0 = perfect, 0.5 = "real cap is
/// ~half of what I just used".
/// Algorithm and rationale: <c>docs/plans/video-quality-control-v2.md</c>.
/// </summary>
public sealed class BandwidthEstimator(BandwidthEstimatorConfig config)
{
    private readonly Queue<BandwidthEstimateRecord> _history = new (config.HistorySize);

    public BandwidthEstimatorConfig Config => config;

    public int     EpochIndex        { get; private set; }
    public long    CeilingBps        { get; private set; } = config.InitialCeilingBps;
    public long    LastCurrentBps    { get; private set; }
    public double  LastSignalLevel   { get; private set; } = 1.0;
    public BandwidthVerdict LastVerdict { get; private set; } = BandwidthVerdict.Neutral;
    public int     NegativeStreak    { get; private set; }
    public int     PositiveStreak    { get; private set; }
    public int     CalmTicks         { get; private set; }
    public int     ProbeFailures     { get; private set; }
    public Moment? LastCeilingDownAt { get; private set; }
    public bool    HasSeenBadSignal  { get; private set; }
    public IReadOnlyCollection<BandwidthEstimateRecord> History => _history;

    public void ResetStreaks()
    {
        // Clears streaks without touching the ceiling. Call on transient noise
        // windows (e.g. recorder pipeline restart) so pre-event streaks don't
        // trigger an immediate cap walk on the first eval after the noise clears.
        NegativeStreak = 0;
        PositiveStreak = 0;
        CalmTicks = 0;
    }

    public void Tick(RpcConnectionInfo? connection, Moment now, long currentBandwidthBps, double signalLevel)
    {
        if (connection is null)
            return;

        if (connection.Index != EpochIndex) {
            CeilingBps = config.InitialCeilingBps;
            LastCurrentBps = 0;
            LastSignalLevel = 1.0;
            LastVerdict = BandwidthVerdict.Neutral;
            NegativeStreak = 0;
            PositiveStreak = 0;
            CalmTicks = 0;
            ProbeFailures = 0;
            LastCeilingDownAt = null;
            HasSeenBadSignal = false;
            EpochIndex = connection.Index;
            _history.Clear();
        }

        signalLevel = Math.Clamp(signalLevel, 0.0, 1.0);
        var verdict = ClassifyVerdict(signalLevel);
        var ceilingBefore = CeilingBps;

        LastCurrentBps = currentBandwidthBps;
        LastSignalLevel = signalLevel;
        LastVerdict = verdict;

        var ageSec = Math.Max(0, (now - connection.ConnectedAt).TotalSeconds);
        var ageFactor = Math.Clamp(
            1.0 - Math.Log10(1.0 + ageSec / config.TauSec),
            config.MinAgeFactor,
            1.0);

        switch (verdict) {
        case BandwidthVerdict.Bad:
            ApplyBad(currentBandwidthBps, signalLevel, ageFactor, now);
            CalmTicks = 0;
            break;
        case BandwidthVerdict.Good when currentBandwidthBps >= CeilingBps * config.ConfirmRatio:
            ApplyGoodWithHeadroom(currentBandwidthBps, ageFactor, now);
            CalmTicks++;
            break;
        default:
            ApplyNeutralOrIdleGood(currentBandwidthBps);
            CalmTicks++;
            break;
        }

        if (CalmTicks >= config.ProbeFailureResetStreak) {
            ProbeFailures = 0;
            LastCeilingDownAt = null;
            // Re-anchor a ratcheted-down ceiling. When the cap is pinned low,
            // the encoder can't emit enough bytes to satisfy the headroom-confirm
            // gate (LastCurrentBps >= CeilingBps * ConfirmRatio), and the ceiling
            // never descends on non-Bad ticks — so the cap can never climb back.
            // After sustained calm, lower the ceiling to the confirm level of the
            // measured rate so the next Good tick confirms headroom and the cap
            // re-probes upward.
            if (LastCurrentBps > 0 && LastCurrentBps < CeilingBps * config.ConfirmRatio) {
                var reanchored = Math.Max(config.FloorBps, LastCurrentBps);
                if (reanchored < CeilingBps)
                    CeilingBps = reanchored;
            }
        }

        AppendHistory(now, currentBandwidthBps, signalLevel, ceilingBefore, verdict);
    }

    public bool ApplyMeasuredCapacity(long measuredBps, long producedBps, Moment now)
    {
        // Anchors the ceiling to the backlog-drain rate (hard evidence, overrides
        // the verdict-based estimate) and returns whether it did. Downward only
        // when drain genuinely lags production — a backlog draining at the produce
        // rate is a credit-window (RTT) stall, so its drain rate reflects the
        // window, not the link.
        if (measuredBps <= 0)
            return false;
        if (measuredBps < CeilingBps && measuredBps >= producedBps * config.MeasuredCapacityLagRatio)
            return false;

        LastCurrentBps = measuredBps;
        CeilingBps = Math.Max(config.FloorBps, measuredBps);
        // A real measurement isn't a failed probe — clear the back-off anchor so
        // the next good tick can lift immediately.
        LastCeilingDownAt = null;
        return true;
    }

    // Private methods

    private BandwidthVerdict ClassifyVerdict(double signalLevel)
    {
        if (config.Classify is { } classify)
            return classify(signalLevel);

        if (signalLevel >= config.GoodThreshold)
            return BandwidthVerdict.Good;
        if (signalLevel < config.BadThreshold)
            return BandwidthVerdict.Bad;
        return BandwidthVerdict.Neutral;
    }

    private void ApplyBad(long currentBandwidthBps, double signalLevel, double ageFactor, Moment now)
    {
        HasSeenBadSignal = true;
        var suggestedCap = Math.Max(config.FloorBps, (long)Math.Round(currentBandwidthBps * signalLevel));
        var α = Math.Clamp(
            config.BaseStep
                * ageFactor
                * (1.0 - signalLevel)
                * (1.0 + NegativeStreak * config.NegStreakStep),
            0.0,
            config.MaxStep);
        var newCeiling = Math.Max(
            config.FloorBps,
            (long)Math.Round(CeilingBps + α * (suggestedCap - CeilingBps)));

        if (newCeiling < CeilingBps) {
            if (PositiveStreak > 0)
                ProbeFailures++;
            LastCeilingDownAt = now;
        }

        CeilingBps = newCeiling;
        NegativeStreak++;
        PositiveStreak = 0;
    }

    private void ApplyGoodWithHeadroom(long currentBandwidthBps, double ageFactor, Moment now)
    {
        if (LastCeilingDownAt is { } downAt) {
            var cooldownSec = Math.Min(
                config.MaxProbeCooldownSec,
                config.BaseProbeCooldownSec * Math.Pow(config.ProbeCooldownGrowth, ProbeFailures));
            if ((now - downAt).TotalSeconds < cooldownSec) {
                PositiveStreak = Math.Max(0, PositiveStreak - 1);
                return;
            }
        }

        var α = Math.Clamp(
            config.BaseStep
                * ageFactor
                * config.AsymRatio
                * (1.0 + PositiveStreak * config.PosStreakStep),
            0.0,
            config.MaxStepUp);
        var lift = Math.Max(currentBandwidthBps, (long)Math.Round(CeilingBps * (1.0 + α)));
        CeilingBps = lift;
        PositiveStreak++;
        NegativeStreak = 0;
    }

    private void ApplyNeutralOrIdleGood(long currentBandwidthBps)
    {
        NegativeStreak = Math.Max(0, NegativeStreak - 1);
        PositiveStreak = Math.Max(0, PositiveStreak - 1);
        if (!HasSeenBadSignal && currentBandwidthBps > CeilingBps)
            CeilingBps = currentBandwidthBps;
    }

    private void AppendHistory(
        Moment now,
        long currentBandwidthBps,
        double signalLevel,
        long ceilingBefore,
        BandwidthVerdict verdict)
    {
        _history.Enqueue(new BandwidthEstimateRecord(
            now, currentBandwidthBps, signalLevel, ceilingBefore, CeilingBps, verdict));
        while (_history.Count > config.HistorySize)
            _history.Dequeue();
    }
}
