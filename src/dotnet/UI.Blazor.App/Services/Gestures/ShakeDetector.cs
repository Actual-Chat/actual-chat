using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the acceleration magnitude reverses across ±threshold around 1g
/// enough times inside <see cref="ReversalWindow"/>.
/// </summary>
public sealed class ShakeDetector(ShakeSensitivity sensitivity)
{
    public static readonly TimeSpan ReversalWindow = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);

    private readonly List<Moment> _reversals = new();
    private int _lastSign;
    private Moment _debouncedUntil;

    public ShakeSensitivity Sensitivity { get; } = sensitivity;
    public float PeakDeviation { get; private set; }

    // Lower sensitivity demands a harder shake, so the firing sets nest: Low ⊆ Medium ⊆ High.
    public static float GetMagnitudeThreshold(ShakeSensitivity sensitivity)
        => sensitivity switch {
            ShakeSensitivity.Low => 1.2f,
            ShakeSensitivity.High => 0.5f,
            _ => 0.8f,
        };

    // |a| has a hard floor at 0, so the dip below 1g can never exceed 1g however hard the shake.
    // The dip side therefore needs its own, reachable threshold - and both sides stay monotone,
    // so the firing sets still nest.
    public static float GetDipThreshold(ShakeSensitivity sensitivity)
        => sensitivity switch {
            ShakeSensitivity.Low => 0.6f,
            ShakeSensitivity.High => 0.4f,
            _ => 0.55f,
        };

    public static int GetReversalCount(ShakeSensitivity sensitivity)
        => sensitivity == ShakeSensitivity.Low ? 4 : 3;

    public bool Process(SensorSample sample)
    {
        var deviation = sample.Magnitude - 1f;
        PeakDeviation = MathF.Max(PeakDeviation * 0.9f, MathF.Abs(deviation));
        if (sample.At < _debouncedUntil)
            return false;

        var sign = deviation > GetMagnitudeThreshold(Sensitivity) ? 1
            : deviation < -GetDipThreshold(Sensitivity) ? -1
            : 0;
        if (sign == 0 || sign == _lastSign)
            return false;

        var hadSign = _lastSign != 0;
        _lastSign = sign;
        if (!hadSign)
            return false;

        _reversals.Add(sample.At);
        _reversals.RemoveAll(at => sample.At - at > ReversalWindow);
        if (_reversals.Count < GetReversalCount(Sensitivity))
            return false;

        _debouncedUntil = sample.At + Debounce;
        _reversals.Clear();
        _lastSign = 0;
        return true;
    }

    public void Reset()
    {
        _reversals.Clear();
        _lastSign = 0;
        _debouncedUntil = default;
        PeakDeviation = 0f;
    }
}
