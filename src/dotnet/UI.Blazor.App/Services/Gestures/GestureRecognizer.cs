
namespace ActualChat.UI.Blazor.App.Services.Gestures;

public sealed record GestureOptions(
    bool IsFlipToTalkEnabled,
    bool IsDoubleShakeEnabled,
    bool IsFaceDownEnabled,
    ShakeSensitivity ShakeSensitivity);

/// <summary>
/// Routes samples to the enabled detectors and emits a single gesture stream.
/// The stop gesture is evaluated first: on the mic, closing always beats opening.
/// Start gestures are suppressed while pocketed: carried upside-down, or proximity-covered
/// for long enough in an orientation that isn't "resting screen-up on a surface".
/// </summary>
public sealed class GestureRecognizer
{
    // A gap this long only happens when sensor delivery paused - e.g. the app was backgrounded -
    // never during ordinary sampling, so it's the signal to drop all in-progress gesture state
    // rather than let it look like a gesture that spans the pause.
    public static readonly TimeSpan SampleGap = TimeSpan.FromSeconds(2);

    private const float UpsideDownMinY = 0.5f;
    private const float MaxGravityDeviation = 0.3f;

    // Options is written from the worker's poll-loop thread and Process/Reset/SetProximityCovered
    // run on the sensor callback thread - the lock keeps the detectors' internal state (e.g.
    // ShakeDetector's reversal list) from being mutated from both threads at once.
    private readonly Lock _lock = new();
    private readonly FlipToTalkDetector _flip = new();
    private readonly FaceDownDetector _faceDown = new();
    private readonly ProximityGuard _proximity = new();
    private readonly ShakeDetector _shake;
    private GestureOptions _options;
    private Moment? _lastSampleAt;
    private bool _isProximitySuppressed;
    private bool _wasSuppressed;
    private bool _isUpsideDown;

    public GestureOptions Options {
        get {
            lock (_lock)
                return _options;
        }
        set {
            lock (_lock) {
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
    }

    public float ShakePeakDeviation {
        get {
            lock (_lock)
                return _shake.PeakDeviation;
        }
    }

    public string? FaceDownLastFireInfo {
        get {
            lock (_lock)
                return _faceDown.LastFireInfo;
        }
    }

    public string FaceDownStatus {
        get {
            lock (_lock)
                return _lastSampleAt is { } lastSampleAt ? _faceDown.FormatStatus(lastSampleAt) : "no samples";
        }
    }

    public string GuardStatus {
        get {
            lock (_lock)
                return (_isProximitySuppressed, _isUpsideDown) switch {
                    (true, true) => "covered+upside-down",
                    (true, false) => "covered",
                    (false, true) => "upside-down",
                    _ => "off",
                };
        }
    }

    public GestureRecognizer(GestureOptions options)
    {
        _options = options;
        _shake = new ShakeDetector(options.ShakeSensitivity);
    }

    public void SetProximityCovered(bool isCovered)
    {
        lock (_lock) {
            // The edge is only recorded here; whether it suppresses anything is decided per
            // sample, once it has held and against an orientation that can actually be pocketed.
            _proximity.SetCovered(isCovered, _lastSampleAt ?? default);
            _faceDown.SetProximityCovered(isCovered);
        }
    }

    public GestureEvent? Process(SensorSample sample)
    {
        lock (_lock) {
            if (_lastSampleAt is { } lastSampleAt && sample.At - lastSampleAt > SampleGap)
                ResetUnguarded();
            _lastSampleAt = sample.At;

            UpdateUpsideDownUnguarded(sample);
            if (_options.IsFaceDownEnabled && _faceDown.Process(sample))
                return new GestureEvent(GestureKind.FaceDown, sample.At);

            _isProximitySuppressed = _proximity.IsSuppressing(sample);
            var isSuppressed = _isProximitySuppressed || _isUpsideDown;
            if (isSuppressed) {
                // Reset on the way IN, so no half-built flip/shake state survives pocketing; on
                // the way out the detectors are already clean - they saw no samples while
                // suppressed. Driven by the debounced decision, so a sensor spike can't wipe a
                // shake that's already in progress.
                if (!_wasSuppressed) {
                    _flip.Reset();
                    _shake.Reset();
                }
                _wasSuppressed = true;
                return null;
            }

            _wasSuppressed = false;
            if (_options.IsFlipToTalkEnabled && _flip.Process(sample))
                return new GestureEvent(GestureKind.FlipToTalk, sample.At);
            if (_options.IsDoubleShakeEnabled && _shake.Process(sample))
                return new GestureEvent(GestureKind.DoubleShake, sample.At);

            return null;
        }
    }

    public void Reset()
    {
        lock (_lock)
            ResetUnguarded();
    }

    // Private methods

    private void UpdateUpsideDownUnguarded(SensorSample sample)
    {
        // Only a gravity-dominated reading says anything about carry orientation; mid-stride
        // bounce leaves the latch unchanged, which is what holds it through a walk.
        if (MathF.Abs(sample.Magnitude - 1f) > MaxGravityDeviation)
            return;

        var axis = sample.GetDominantAxis(UpsideDownMinY);
        if (axis == GravityAxis.None)
            return;

        // MAUI yields Y ≈ +1 for an UPRIGHT portrait on both platforms (verified on-device
        // 2026-08-11), so top-down pocket carry is the NEGATIVE end of Y.
        _isUpsideDown = axis == GravityAxis.Y && sample.Y <= -UpsideDownMinY;
    }

    private void ResetUnguarded()
    {
        // The proximity level is deliberately kept: it's edge-driven, so forgetting it would
        // leave us reading "uncovered" until the sensor next changes its mind.
        _isUpsideDown = false;
        _isProximitySuppressed = false;
        _wasSuppressed = false;
        _flip.Reset();
        _shake.Reset();
        _faceDown.Reset();
    }
}
