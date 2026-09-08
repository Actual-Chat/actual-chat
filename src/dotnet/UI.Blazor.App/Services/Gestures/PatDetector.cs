namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on two sharp impulses in a pat rhythm after a quiet spell - a double-pat on a pocket.
/// A third impulse inside the window cancels the pair, and a step train never gets the quiet.
/// </summary>
public sealed class PatDetector
{
    public static readonly TimeSpan QuietBefore = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan MaxGap = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    // To be settled on device: a pat through denim measured against the 0.8g medium shake threshold.
    public const float Threshold = 1.2f;

    private readonly GravityHighPassFilter _filter = new();
    private Moment? _lastImpulseAt;
    private Moment? _firstPatAt;
    private Moment? _secondPatAt;
    private Moment _debouncedUntil;

    public float PeakDeviation { get; private set; }

    public bool Process(SensorSample sample)
    {
        var (fx, fy, fz) = _filter.Process(sample);
        var deviation = MathF.Sqrt((fx * fx) + (fy * fy) + (fz * fz));
        PeakDeviation = MathF.Max(PeakDeviation * 0.9f, deviation);
        var at = sample.At;
        if (at < _debouncedUntil)
            return false;

        var isImpulse = deviation > Threshold;
        // A completed pair fires only once MaxGap has passed without a third impulse.
        if (_secondPatAt is { } second) {
            if (isImpulse) {
                // The same tap spread over two samples (or its rebound) is not a third pat.
                if (at - second < MinGap)
                    return false;

                Clear();
                _lastImpulseAt = at;
                return false;
            }

            if (at - second < MaxGap)
                return false;

            Clear();
            _debouncedUntil = at + Debounce;
            return true;
        }

        if (!isImpulse)
            return false;

        var sinceLast = _lastImpulseAt is { } last ? at - last : TimeSpan.MaxValue;
        _lastImpulseAt = at;
        if (_firstPatAt is { } first) {
            var gap = at - first;
            if (gap < MinGap)
                return false; // the same tap, spread over two samples

            if (gap <= MaxGap)
                _secondPatAt = at;
            else
                _firstPatAt = sinceLast >= QuietBefore ? at : null;
            return false;
        }

        if (sinceLast >= QuietBefore)
            _firstPatAt = at;
        return false;
    }
    public void Reset()
    {
        _filter.Reset();
        Clear();
        _lastImpulseAt = null;
        _debouncedUntil = default;
        PeakDeviation = 0f;
    }

    // Private methods

    private void Clear()
    {
        _firstPatAt = null;
        _secondPatAt = null;
    }
}
