namespace ActualChat.UI.Blazor.App.Services;

public sealed record EncodingCapConfig(
    // Thresholds match the THROUGHPUT-DEFICIT semantic of
    // `RecorderStats.EncodeDeficitEma` (range 0..1, deficit = 1 -
    // bundlesEncoded/framesOffered per-second EMA). Bad = sustained
    // encoder underrun, Good = within a few-% margin of source rate.
    // Demote layers when underrun persists `BadStreak` ticks.
    double EncodeDeficitBad = 0.20,
    double EncodeDeficitGood = 0.05,
    int BadStreak = 2,
    int GoodStreak = 5)
{
    public static EncodingCapConfig Default { get; } = new();
}

/// <summary>
/// Sender-only encoding-pressure cap on camera/screencast spatial layers.
/// Walks <see cref="LayerCap"/> down on sustained encode-ratio overrun,
/// back up on sustained underrun.
/// </summary>
public sealed class EncodingCap
{
    private readonly EncodingCapConfig _config;
    private int _badStreak;
    private int _goodStreak;

    public LayerCap Layers { get; }
    public EncodingCapConfig Config => _config;
    public int BadStreak => _badStreak;
    public int GoodStreak => _goodStreak;

    public EncodingCap(LayerCap layers, EncodingCapConfig config)
    {
        Layers = layers;
        _config = config;
    }

    /// <summary>
    /// Zeroes the bad/good streak counters. Use after a recorder pipeline
    /// restart so a residual half-bad streak doesn't carry over the
    /// cooldown gap and trigger an immediate demote when fresh stats
    /// resume.
    /// </summary>
    public void ResetStreaks()
    {
        _badStreak = 0;
        _goodStreak = 0;
    }

    public void Tick(double encodeDeficitEma, IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        if (encodeDeficitEma > _config.EncodeDeficitBad) {
            _badStreak++;
            _goodStreak = 0;
            if (_badStreak >= _config.BadStreak) {
                Layers.Reduce(activeKinds);
                _badStreak = 0;
            }
            return;
        }

        if (encodeDeficitEma < _config.EncodeDeficitGood) {
            _goodStreak++;
            _badStreak = 0;
            if (_goodStreak >= _config.GoodStreak) {
                Layers.Increase(activeKinds);
                _goodStreak = 0;
            }
            return;
        }

        // Dead band: not bad enough to demote, not good enough to credit a ramp
        // tick. Clear the bad streak but only DECAY the good streak so a single
        // transient excursion doesn't wipe ramp progress earned by good ticks —
        // a hard reset here pins a mostly-healthy encoder at 1 layer forever.
        _badStreak = 0;
        _goodStreak = Math.Max(0, _goodStreak - 1);
    }
}
