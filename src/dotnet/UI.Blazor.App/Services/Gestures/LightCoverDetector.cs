namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Reads an ambient light sensor as a cover signal: covered once the light holds below
/// <see cref="CoveredMaxLux"/> for <see cref="CoverDelay"/>, uncovered once it holds above
/// <see cref="UncoveredMinLux"/> for <see cref="UncoverDelay"/>. The first reading is split at one boundary.
/// </summary>
public sealed class LightCoverDetector
{
    public static readonly TimeSpan CoverDelay = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan UncoverDelay = TimeSpan.FromSeconds(1);
    public const float CoveredMaxLux = 0.5f;
    public const float UncoveredMinLux = 1.5f;
    public const float InitialCoveredMaxLux = 1f;
    public const float PulledOutMinLux = 5f;

    private float _lux;
    private bool _hasReading;
    private Moment? _darkSince;
    private Moment? _litSince;

    public bool IsCovered { get; private set; }
    public bool IsPending => (_darkSince is not null && !IsCovered) || (_litSince is not null && IsCovered);

    public bool Process(float lux, Moment at)
    {
        if (!_hasReading) {
            // The first reading has no prior state to hold, so it's split at a single boundary
            _hasReading = true;
            _lux = lux;
            IsCovered = lux < InitialCoveredMaxLux;
            return IsCovered;
        }

        _lux = lux;
        return Tick(at);
    }

    public bool Tick(Moment now)
    {
        // A hand shading the sensor in a dim room reads 0 for a moment, a pocket stays dark, and moving
        // in a pocket flickers up to 5 lux for ~0.5s - so either edge counts only once it has held.
        if (!_hasReading)
            return IsCovered;

        if (_lux < CoveredMaxLux) {
            _litSince = null;
            _darkSince ??= now;
            if (now - _darkSince.Value >= CoverDelay)
                IsCovered = true;
        }
        else if (_lux > UncoveredMinLux) {
            _darkSince = null;
            _litSince ??= now;
            if (now - _litSince.Value >= UncoverDelay)
                IsCovered = false;
        }
        else {
            _darkSince = null;
            _litSince = null;
        }
        return IsCovered;
    }

    public void Reset()
    {
        _hasReading = false;
        _darkSince = null;
        _litSince = null;
        IsCovered = false;
    }
}
