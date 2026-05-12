using ActualChat.Bandwidth;

namespace ActualChat.UI.Blazor.App.Services;

public sealed record BandwidthCapConfig(
    int BadStreak = 2,
    int GoodStreak = 5);

/// <summary>
/// Walks a <see cref="LayerCap"/> based on a <see cref="BandwidthEstimator"/>'s
/// streak signals. Bad streak ⇒ Reduce, good streak with confirmed headroom
/// ⇒ Increase.
/// </summary>
public sealed class BandwidthCap
{
    private readonly BandwidthCapConfig _config;
    private int _consumedBadStreak;
    private int _consumedGoodStreak;

    public LayerCap Layers { get; }
    public BandwidthCapConfig Config => _config;

    public BandwidthCap(LayerCap layers, BandwidthCapConfig config)
    {
        Layers = layers;
        _config = config;
    }

    public void Tick(BandwidthEstimator estimator, IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        if (estimator.NegativeStreak >= _config.BadStreak
            && estimator.NegativeStreak > _consumedBadStreak) {
            Layers.Reduce(activeKinds);
            _consumedBadStreak = estimator.NegativeStreak;
            _consumedGoodStreak = 0;
            return;
        }

        var confirmedHeadroom = estimator.LastCurrentBps
            >= estimator.CeilingBps * estimator.Config.ConfirmRatio;
        if (estimator.PositiveStreak >= _config.GoodStreak
            && estimator.PositiveStreak > _consumedGoodStreak
            && confirmedHeadroom) {
            Layers.Increase(activeKinds);
            _consumedGoodStreak = estimator.PositiveStreak;
            _consumedBadStreak = 0;
            return;
        }

        if (estimator.NegativeStreak == 0)
            _consumedBadStreak = 0;
        if (estimator.PositiveStreak == 0)
            _consumedGoodStreak = 0;
    }
}
