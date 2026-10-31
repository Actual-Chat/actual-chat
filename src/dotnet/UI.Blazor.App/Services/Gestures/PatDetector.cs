namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on two sharp impulses in a pat rhythm after a quiet spell - a double-pat on a pocket.
/// A third impulse inside the window cancels the pair, and a step train never gets the quiet.
/// </summary>
public sealed class PatDetector
{
    public static readonly TimeSpan QuietBefore = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MinGap = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan ReboundWindow = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan MaxGap = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan QuietAfter = TimeSpan.FromMilliseconds(300);
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    public const float Threshold = 0.35f;
    public const float ScreenNormalMinShare = 0.6f;
    // g per sample (20ms): a hit jumps this much at once, a twist or a shake slides there gradually
    public const float MinPairJerk = 0.75f;
    // cos(20 deg): a pat leaves a pocketed phone where it was, pulling it out turns it
    private const float MinGravityCos = 0.94f;

    private readonly GravityHighPassFilter _filter = new();
    private readonly GravityEstimator _orientation = new();
    private Moment? _lastImpulseAt;
    private Moment? _firstPatAt;
    private Moment? _secondPatAt;
    private Moment _debouncedUntil;
    private bool _isAboveThreshold;
    private (float X, float Y, float Z) _firstPatGravity;
    private TimeSpan _quietAfter;
    private Moment? _litAt;
    private SensorSample _lastSample;
    private float _recentJerk;
    private float _pairJerk;

    public float PeakDeviation { get; private set; }

    public bool Process(SensorSample sample)
    {
        var gravityBefore = _orientation.Gravity;
        var jerk = _lastSample.At == default ? 0f : MathF.Sqrt(
            Sq(sample.X - _lastSample.X) + Sq(sample.Y - _lastSample.Y) + Sq(sample.Z - _lastSample.Z));
        _lastSample = sample;
        _recentJerk = MathF.Max(jerk, _recentJerk * 0.5f);
        if (_firstPatAt is not null)
            _pairJerk = MathF.Max(_pairJerk, jerk);
        _orientation.Process(sample);
        var (fx, fy, fz) = _filter.Process(sample);
        var deviation = MathF.Sqrt((fx * fx) + (fy * fy) + (fz * fz));
        PeakDeviation = MathF.Max(PeakDeviation * 0.9f, deviation);
        var at = sample.At;
        // Only the rise counts: at 50Hz one pat stays above the threshold for up to ~200ms.
        // Tracked through the debounce too, or a signal still high when it ends reads as a new rise.
        var isAboveThreshold = deviation > Threshold;
        var isRise = isAboveThreshold && !_isAboveThreshold;
        _isAboveThreshold = isAboveThreshold;
        if (at < _debouncedUntil)
            return false;

        // A pat pushes the phone through its screen; a step jolts it along its length
        var isImpulse = isRise && MathF.Abs(fz) >= ScreenNormalMinShare * deviation;
        // A completed pair fires once ConfirmWindow has passed, unless a third impulse lands within
        // its quiet, or the phone turns or meets light meanwhile - both mean it's being pulled out.
        if (_secondPatAt is { } second) {
            var sinceSecond = at - second;
            if (isImpulse && sinceSecond < _quietAfter) {
                // The pat's rebound (120-150ms on a pocket, measured) is not a third pat.
                if (sinceSecond < ReboundWindow)
                    return false;

                Clear();
                _lastImpulseAt = at;
                return false;
            }

            if (!IsSameOrientation(_firstPatGravity, _orientation.Gravity) || _litAt >= _firstPatAt) {
                Clear();
                return false;
            }

            if (sinceSecond < GetHold())
                return false;

            Clear();
            if (_pairJerk < MinPairJerk)
                return false;

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

            if (gap <= MaxGap) {
                // Tap-tap-silence, not a train: the silence must outlast the pair's own gap, or
                // steps 500ms apart pair up before the next step arrives to cancel them.
                _secondPatAt = at;
                _quietAfter = TimeSpan.FromTicks(Math.Max(QuietAfter.Ticks, (long)(gap.Ticks * 1.25)));
            }
            else if (sinceLast >= QuietBefore)
                StartPair(at, gravityBefore);
            else
                _firstPatAt = null;
            return false;
        }

        if (sinceLast >= QuietBefore)
            StartPair(at, gravityBefore);
        return false;
    }

    public float GetProgress(Moment now)
    {
        // A pair still has to sit out its quiet with no third pat, so it stays short of 1 until it fires
        if (now < _debouncedUntil)
            return 0f;
        if (_secondPatAt is { } second)
            return (2f + (0.9f * MathF.Min(1f, (float)((now - second) / GetHold())))) / 3f;
        if (_firstPatAt is { } first && now - first <= MaxGap)
            return 1f / 3f;

        return 0f;
    }

    public void SetLit(Moment at)
        => _litAt = at;

    public void Reset()
    {
        _filter.Reset();
        Clear();
        _lastImpulseAt = null;
        _debouncedUntil = default;
        _isAboveThreshold = false;
        _orientation.Reset();
        _litAt = null;
        _lastSample = default;
        _recentJerk = 0f;
        _pairJerk = 0f;
        PeakDeviation = 0f;
    }

    // Private methods

    private void StartPair(Moment at, (float X, float Y, float Z)? gravity)
    {
        _firstPatAt = at;
        _firstPatGravity = gravity ?? default;
        _pairJerk = _recentJerk;
    }

    private TimeSpan GetHold()
        => TimeSpanExt.Max(_quietAfter, ConfirmWindow);

    private void Clear()
    {
        _firstPatAt = null;
        _secondPatAt = null;
    }

    private static bool IsSameOrientation((float X, float Y, float Z) a, (float X, float Y, float Z)? b)
    {
        if (b is not { } vb)
            return true;

        var dot = (a.X * vb.X) + (a.Y * vb.Y) + (a.Z * vb.Z);
        var aNorm = MathF.Sqrt((a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z));
        var bNorm = MathF.Sqrt((vb.X * vb.X) + (vb.Y * vb.Y) + (vb.Z * vb.Z));
        return aNorm * bNorm <= 0f || dot / (aNorm * bNorm) >= MinGravityCos;
    }

    private static float Sq(float x)
        => x * x;
}
