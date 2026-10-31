namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on a turn of the phone in its screen plane and back: the gyroscope's rotation around the
/// screen normal adds up to at least <see cref="TurnMinDegrees"/>, then returns within
/// <see cref="ReturnMaxDegrees"/> of the start, all inside <see cref="FlipWindow"/>. Hand motion doesn't
/// matter - only rotation does - and a phone lying flat is not flipped, merely spun.
/// </summary>
public sealed class FlipToTalkDetector
{
    public static readonly TimeSpan FlipWindow = TimeSpan.FromSeconds(2.5);
    public static readonly TimeSpan MinTurnedDuration = TimeSpan.FromMilliseconds(60);
    public const float TurnMinDegrees = 60f;
    public const float ReturnMaxDegrees = 25f;
    private static readonly TimeSpan IdleLeakTau = TimeSpan.FromSeconds(1);
    private const float StartDegrees = 15f;
    private const float MinInPlaneGravity = 0.5f;

    private State _state;
    private float _angle;
    private Moment _startedAt;
    private Moment _turnedAt;
    private Moment _lastAt;


    public bool Process(GyroSample sample, (float X, float Y, float Z)? gravity)
    {
        var dt = _lastAt == default ? 0f : (float)(sample.At - _lastAt).TotalSeconds;
        _lastAt = sample.At;
        _angle += sample.DeltaZ * (180f / MathF.PI);
        var absAngle = MathF.Abs(_angle);
        switch (_state) {
        case State.Idle:
            if (absAngle < StartDegrees) {
                // Bleeds off gyro drift and stray wobble, so every turn starts from zero
                _angle -= _angle * (dt <= 0f ? 0f : dt / ((float)IdleLeakTau.TotalSeconds + dt));
                return false;
            }
            if (gravity is not { } g || MathF.Sqrt((g.X * g.X) + (g.Y * g.Y)) < MinInPlaneGravity) {
                _angle = 0f;
                return false;
            }

            _state = State.Turning;
            _startedAt = sample.At;
            return false;
        case State.Turning:
            if (sample.At - _startedAt > FlipWindow || absAngle < StartDegrees / 2) {
                Reset();
                return false;
            }
            if (absAngle >= TurnMinDegrees) {
                _state = State.Turned;
                _turnedAt = sample.At;
            }
            return false;
        default:
            if (sample.At - _startedAt > FlipWindow) {
                // Held turned too long: that's the phone's new position, not half a flip
                Reset();
                return false;
            }
            if (absAngle > ReturnMaxDegrees)
                return false;

            // A wrist-shake twists back within ~50ms, a deliberate turn holds the turned position longer
            var isFlip = sample.At - _turnedAt >= MinTurnedDuration;
            Reset();
            return isFlip;
        }
    }

    public float GetProgress(Moment now)
    {
        // The turn covers the first half, the way back the second
        var turned = MathF.Min(1f, MathF.Abs(_angle) / 90f);
        return _state switch {
            State.Turning => 0.5f * turned,
            State.Turned when now - _startedAt <= FlipWindow => 0.5f + (0.5f * (1f - turned)),
            _ => 0f,
        };
    }

    public void Reset()
    {
        _state = State.Idle;
        _angle = 0f;
    }

    // Nested types

    private enum State
    {
        Idle,
        Turning,
        Turned,
    }
}
