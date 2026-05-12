namespace ActualChat.UI.Blazor.App.Services;

public sealed record EncodingCapConfig(
    double EncodeRatioBad = 2.0,
    double EncodeRatioGood = 1.2,
    int BadStreak = 2,
    int GoodStreak = 5);

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

    public void Tick(double encodeRatioEma, IReadOnlyCollection<VideoSourceKind>? activeKinds = null)
    {
        if (encodeRatioEma > _config.EncodeRatioBad) {
            _badStreak++;
            _goodStreak = 0;
            if (_badStreak >= _config.BadStreak) {
                Layers.Reduce(activeKinds);
                _badStreak = 0;
            }
            return;
        }

        if (encodeRatioEma < _config.EncodeRatioGood) {
            _goodStreak++;
            _badStreak = 0;
            if (_goodStreak >= _config.GoodStreak) {
                Layers.Increase(activeKinds);
                _goodStreak = 0;
            }
            return;
        }

        _badStreak = 0;
        _goodStreak = 0;
    }
}
