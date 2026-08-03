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
    // A gap this long only happens when sensor delivery paused - e.g. the app was backgrounded -
    // never during ordinary sampling, so it's the signal to drop all in-progress gesture state
    // rather than let it look like a gesture that spans the pause.
    public static readonly TimeSpan SampleGap = TimeSpan.FromSeconds(2);

    private readonly FlipToTalkDetector _flip = new();
    private readonly FaceDownDetector _faceDown = new();
    private readonly ShakeDetector _shake;
    private GestureOptions _options;
    private Moment? _lastSampleAt;

    public GestureOptions Options {
        get => _options;
        set {
            if (value.IsFlipToTalkEnabled != _options.IsFlipToTalkEnabled)
                _flip.Reset();
            if (value.IsFaceDownEnabled != _options.IsFaceDownEnabled)
                _faceDown.Reset();
            if (value.ShakeSensitivity != _options.ShakeSensitivity)
                _shake.ChangeSensitivity(value.ShakeSensitivity);
            if (value.IsDoubleShakeEnabled != _options.IsDoubleShakeEnabled)
                _shake.Reset();
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
        if (_lastSampleAt is { } lastSampleAt && sample.At - lastSampleAt > SampleGap)
            Reset();
        _lastSampleAt = sample.At;

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
