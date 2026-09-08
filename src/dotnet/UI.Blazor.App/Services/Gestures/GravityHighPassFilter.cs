namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Removes the slowly varying gravity component from accelerometer samples,
/// leaving the impulses a shake or a pat consists of.
/// </summary>
public sealed class GravityHighPassFilter
{
    // Time constant of the gravity low-pass: slow enough that a shake's zero-mean oscillation
    // barely moves the estimate, fast enough to re-settle within ~1s of an orientation change.
    private static readonly TimeSpan GravityTau = TimeSpan.FromMilliseconds(400);

    private (float X, float Y, float Z)? _gravity;
    private Moment _lastAt;

    public (float X, float Y, float Z) Process(SensorSample sample)
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
    public void Reset()
        => _gravity = null;
}
