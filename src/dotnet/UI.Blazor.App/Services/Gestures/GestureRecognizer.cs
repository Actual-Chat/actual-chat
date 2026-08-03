using ActualChat.Users;

namespace ActualChat.UI.Blazor.App.Services.Gestures;

public sealed record GestureOptions(
    bool IsFlipToTalkEnabled,
    bool IsDoubleShakeEnabled,
    bool IsFaceDownEnabled,
    ShakeSensitivity ShakeSensitivity);

/// <summary>
/// Routes samples to the enabled detectors and emits a single gesture stream.
/// The stop gesture is evaluated first: on the mic, closing always beats opening.
/// </summary>
public sealed class GestureRecognizer
{
    private readonly FlipToTalkDetector _flip = new();
    private readonly FaceDownDetector _faceDown = new();
    private ShakeDetector _shake;
    private GestureOptions _options;

    public GestureOptions Options {
        get => _options;
        set {
            if (value.ShakeSensitivity != _options.ShakeSensitivity)
                _shake = new ShakeDetector(value.ShakeSensitivity);
            _options = value;
        }
    }

    public float ShakePeakDeviation => _shake.PeakDeviation;

    public GestureRecognizer(GestureOptions options)
    {
        _options = options;
        _shake = new ShakeDetector(options.ShakeSensitivity);
    }

    public void SetProximityCovered(bool isCovered)
        => _faceDown.SetProximityCovered(isCovered);

    public GestureEvent? Process(SensorSample sample)
    {
        if (_options.IsFaceDownEnabled && _faceDown.Process(sample))
            return new GestureEvent(GestureKind.FaceDown, sample.At);
        if (_options.IsFlipToTalkEnabled && _flip.Process(sample))
            return new GestureEvent(GestureKind.FlipToTalk, sample.At);
        if (_options.IsDoubleShakeEnabled && _shake.Process(sample))
            return new GestureEvent(GestureKind.DoubleShake, sample.At);

        return null;
    }

    public void Reset()
    {
        _flip.Reset();
        _shake.Reset();
        _faceDown.Reset();
    }
}
