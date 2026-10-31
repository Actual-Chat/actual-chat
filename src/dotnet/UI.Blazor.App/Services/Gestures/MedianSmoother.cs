namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Per-axis median of the readings from the last <see cref="Window"/>: a single spike can't move it,
/// while a held orientation shows through within half the window - what posture-based gestures
/// classify instead of raw readings. At 50Hz the window holds 5 readings.
/// </summary>
public sealed class MedianSmoother
{
    public static readonly TimeSpan Window = TimeSpan.FromMilliseconds(100);
    private const int MaxCount = 8;

    private readonly SensorSample[] _window = new SensorSample[MaxCount];
    private readonly float[] _values = new float[MaxCount];
    private int _count;

    public SensorSample Process(SensorSample sample)
    {
        var kept = 0;
        for (var i = 0; i < _count; i++) {
            var s = _window[i];
            if (sample.At - s.At < Window)
                _window[kept++] = s;
        }
        if (kept == MaxCount) {
            Array.Copy(_window, 1, _window, 0, MaxCount - 1);
            kept--;
        }
        _window[kept] = sample;
        _count = kept + 1;
        // Two readings have no median to speak of - and a sparse feed is not smoothed at all
        return _count < 3 ? sample : new SensorSample(sample.At, Median(0), Median(1), Median(2));
    }

    public void Reset()
        => _count = 0;

    // Private methods

    private float Median(int axis)
    {
        var values = _values.AsSpan(0, _count);
        for (var i = 0; i < _count; i++) {
            var s = _window[i];
            values[i] = axis switch {
                0 => s.X,
                1 => s.Y,
                _ => s.Z,
            };
        }
        values.Sort();
        return values[_count / 2];
    }
}
