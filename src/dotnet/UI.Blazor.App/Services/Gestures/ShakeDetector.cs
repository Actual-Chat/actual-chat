using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the high-passed acceleration along one latched axis reverses across
/// ±threshold enough times inside <see cref="ReversalWindow"/>.
/// </summary>
public sealed class ShakeDetector(ShakeSensitivity sensitivity)
{
    public static readonly TimeSpan ReversalWindow = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);
    // Time constant of the gravity low-pass: slow enough that a shake's zero-mean oscillation
    // barely moves the estimate, fast enough to re-settle within ~1s of an orientation change.
    private static readonly TimeSpan GravityTau = TimeSpan.FromMilliseconds(400);

    private readonly List<Moment> _reversals = new();
    private (float X, float Y, float Z)? _gravity;
    private Moment _lastAt;
    private GravityAxis _axis;
    private int _lastSign;
    private Moment _lastExtremeAt;
    private Moment _debouncedUntil;

    public ShakeSensitivity Sensitivity { get; private set; } = sensitivity;
    public float PeakDeviation { get; private set; }

    // Lower sensitivity demands a harder shake, so the firing sets nest: Low ⊆ Medium ⊆ High.
    // The threshold applies to the gravity-free (high-passed) signal, so it is symmetric and
    // direction-independent - a horizontal shake is as detectable as a vertical one, and there
    // is no unreachable "dip below 1g" band.
    public static float GetDeviationThreshold(ShakeSensitivity sensitivity)
        => sensitivity switch {
            ShakeSensitivity.Low => 1.2f,
            ShakeSensitivity.High => 0.5f,
            _ => 0.8f,
        };

    // Process counts alternations inside a sliding ReversalWindow, not over the whole stream, so
    // equal counts at neighboring sensitivities don't nest: a sample that's sub-threshold at one
    // sensitivity but not the other can absorb a transition and push the next reversal out of the
    // window for only one of the two detectors. Strictly decreasing counts give the weaker
    // sensitivity slack the stronger one lacks, which is what keeps Low ⊆ Medium ⊆ High.
    public static int GetReversalCount(ShakeSensitivity sensitivity)
        => sensitivity switch {
            ShakeSensitivity.Low => 4,
            ShakeSensitivity.High => 2,
            _ => 3,
        };

    public bool Process(SensorSample sample)
    {
        var (fx, fy, fz) = Filter(sample);
        var deviation = MathF.Sqrt((fx * fx) + (fy * fy) + (fz * fz));
        PeakDeviation = MathF.Max(PeakDeviation * 0.9f, deviation);
        if (sample.At < _debouncedUntil)
            return false;

        // An alternation chain whose last extreme fell out of the window can never complete -
        // and its latched axis must not hijack a later shake along a different axis.
        if (_lastSign != 0 && sample.At - _lastExtremeAt > ReversalWindow) {
            _axis = GravityAxis.None;
            _lastSign = 0;
            _reversals.Clear();
        }

        var threshold = GetDeviationThreshold(Sensitivity);
        if (_axis == GravityAxis.None) {
            var (ax, ay, az) = (MathF.Abs(fx), MathF.Abs(fy), MathF.Abs(fz));
            var max = MathF.Max(ax, MathF.Max(ay, az));
            if (max <= threshold)
                return false;

            _axis = max == ax ? GravityAxis.X
                : max == ay ? GravityAxis.Y
                : GravityAxis.Z;
        }

        var value = _axis switch {
            GravityAxis.X => fx,
            GravityAxis.Y => fy,
            _ => fz,
        };
        var sign = value > threshold ? 1
            : value < -threshold ? -1
            : 0;
        if (sign == 0 || sign == _lastSign)
            return false;

        var hadSign = _lastSign != 0;
        _lastSign = sign;
        _lastExtremeAt = sample.At;
        if (!hadSign)
            return false;

        _reversals.Add(sample.At);
        _reversals.RemoveAll(at => sample.At - at > ReversalWindow);
        if (_reversals.Count < GetReversalCount(Sensitivity))
            return false;

        _debouncedUntil = sample.At + Debounce;
        _reversals.Clear();
        _lastSign = 0;
        _axis = GravityAxis.None;
        return true;
    }

    public void Reset()
    {
        _reversals.Clear();
        _gravity = null;
        _axis = GravityAxis.None;
        _lastSign = 0;
        _debouncedUntil = default;
        PeakDeviation = 0f;
    }

    public void ChangeSensitivity(ShakeSensitivity newSensitivity)
    {
        // Only the sensitivity-relative state (in-progress reversals, current sign) is stale after
        // a sensitivity change; _debouncedUntil, PeakDeviation, and the gravity estimate still
        // describe real, recent motion and must survive, or a settings push right after a fire
        // would let the same gesture fire twice, and the practice panel's meter would blank on
        // every slider drag.
        Sensitivity = newSensitivity;
        _reversals.Clear();
        _axis = GravityAxis.None;
        _lastSign = 0;
    }

    // Private methods

    private (float X, float Y, float Z) Filter(SensorSample sample)
    {
        // The first sample seeds the estimate, so a detector born mid-motion starts neutral
        // instead of reading its own seed as a spike.
        if (_gravity is not { } g) {
            _gravity = (sample.X, sample.Y, sample.Z);
            _lastAt = sample.At;
            return (0f, 0f, 0f);
        }

        var dt = (float)(sample.At - _lastAt).TotalSeconds;
        _lastAt = sample.At;
        var alpha = dt <= 0f ? 0f : dt / ((float)GravityTau.TotalSeconds + dt);
        var gx = g.X + (alpha * (sample.X - g.X));
        var gy = g.Y + (alpha * (sample.Y - g.Y));
        var gz = g.Z + (alpha * (sample.Z - g.Z));
        _gravity = (gx, gy, gz);
        return (sample.X - gx, sample.Y - gy, sample.Z - gz);
    }
}
