namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Estimates gravity - i.e. how the phone is oriented - from every accelerometer reading, moving or
/// not. Each reading's pull on the estimate is capped at <see cref="MaxStep"/>, so a pat or a jolt
/// barely moves it, while a turn that lasts moves it all the way within ~<see cref="Tau"/>.
/// </summary>
public sealed class GravityEstimator
{
    public static readonly TimeSpan Tau = TimeSpan.FromMilliseconds(250);
    public const float MaxStep = 0.6f;

    private (float X, float Y, float Z)? _gravity;
    private Moment _lastAt;

    public (float X, float Y, float Z)? Gravity => _gravity;

    public (float X, float Y, float Z) Process(SensorSample sample)
    {
        if (_gravity is not { } g) {
            _gravity = (sample.X, sample.Y, sample.Z);
            _lastAt = sample.At;
            return _gravity.Value;
        }

        var dt = (float)(sample.At - _lastAt).TotalSeconds;
        _lastAt = sample.At;
        var (dx, dy, dz) = (sample.X - g.X, sample.Y - g.Y, sample.Z - g.Z);
        var length = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        if (length > MaxStep) {
            var scale = MaxStep / length;
            (dx, dy, dz) = (dx * scale, dy * scale, dz * scale);
        }
        var alpha = dt <= 0f ? 0f : dt / ((float)Tau.TotalSeconds + dt);
        var next = (g.X + (alpha * dx), g.Y + (alpha * dy), g.Z + (alpha * dz));
        _gravity = next;
        return next;
    }

    public void Reset()
        => _gravity = null;
}
