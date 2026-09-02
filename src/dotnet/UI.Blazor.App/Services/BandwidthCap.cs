using ActualChat.Streaming;
using ActualChat.Bandwidth;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record BandwidthCapConfig(
    int BadStreak = 2,
    int GoodStreak = 5,
    // Climbing on an idle wire is slower than climbing on confirmed throughput:
    // an idle wire proves headroom at the current rate, not at the next tier's.
    int IdleProbeStreak = 10,
    // What counts as an idle wire. Well inside UplinkHealth's own Good bands, so
    // "idle" means nothing is queueing at all, not merely "not yet bad".
    double IdleAckAgeMs = 250,
    double IdleQueueDepth = 0.5,
    double IdleDropRatio = 0.001);

/// <summary>
/// Walks a <see cref="LayerCap"/> based on a <see cref="BandwidthEstimator"/>'s
/// streak signals. Bad streak ⇒ Reduce; good streak ⇒ Increase, on either
/// confirmed throughput or an idle wire.
/// </summary>
public sealed class BandwidthCap(LayerCap layers, BandwidthCapConfig config)
{
    private readonly BandwidthCapConfig _config = config;
    private int _consumedBadStreak;
    private int _consumedGoodStreak;
    private int _idleStreak;

    public LayerCap Layers { get; } = layers;
    public BandwidthCapConfig Config => _config;

    /// <summary>
    /// Clears the consumed-streak watermarks so the next cap walk fires on the
    /// configured streak rather than on growth past the previously consumed value.
    /// </summary>
    public void ResetStreaks()
    {
        _consumedBadStreak = 0;
        _consumedGoodStreak = 0;
        _idleStreak = 0;
    }

    public void Tick(
        BandwidthEstimator estimator,
        IReadOnlyCollection<VideoSourceKind>? activeKinds = null,
        UplinkHealth? uplink = null)
    {
        // `uplink` separates a demand-limited sender from a capacity-limited one;
        // omit it and only the throughput-confirmed climb is available.
        if (estimator.NegativeStreak >= _config.BadStreak
            && estimator.NegativeStreak > _consumedBadStreak) {
            Layers.Reduce(activeKinds);
            _consumedBadStreak = estimator.NegativeStreak;
            _consumedGoodStreak = 0;
            _idleStreak = 0;
            return;
        }

        // Throughput is evidence about the LINK only when the link was what limited
        // it: a sender that emptied its queue instantly measured its own demand.
        var confirmedHeadroom = estimator.LastCurrentBps
            >= estimator.CeilingBps * estimator.Config.ConfirmRatio;
        if (estimator.PositiveStreak >= _config.GoodStreak
            && estimator.PositiveStreak > _consumedGoodStreak
            && confirmedHeadroom) {
            Layers.Increase(activeKinds);
            _consumedGoodStreak = estimator.PositiveStreak;
            _consumedBadStreak = 0;
            _idleStreak = 0;
            return;
        }

        // Nothing queued, nothing late, nothing dropped: everything produced went
        // out as produced, which is headroom evidence throughput cannot supply.
        var isIdleWire = uplink is not null
            && uplink.WireLastAckAgeMs <= _config.IdleAckAgeMs
            && uplink.WireQueueDepthEma <= _config.IdleQueueDepth
            && uplink.SenderWirePathDropRatio <= _config.IdleDropRatio;
        // Counted here rather than read off the estimator: its PositiveStreak only
        // advances on the confirm gate above, so an efficient sender never builds one.
        _idleStreak = isIdleWire && estimator.NegativeStreak == 0 ? _idleStreak + 1 : 0;
        // An idle wire says nothing about the NEXT tier, which can cost several times
        // the current rate — so climb one step per streak and let the Bad path undo it.
        if (_idleStreak >= _config.IdleProbeStreak) {
            Layers.Increase(activeKinds);
            _idleStreak = 0;
            _consumedBadStreak = 0;
            return;
        }

        if (estimator.NegativeStreak == 0)
            _consumedBadStreak = 0;
        if (estimator.PositiveStreak == 0)
            _consumedGoodStreak = 0;
    }
}
