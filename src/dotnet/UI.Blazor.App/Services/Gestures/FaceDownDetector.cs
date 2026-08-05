namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the device is held face-down, or covered and near-vertical (pocket),
/// for <see cref="Dwell"/> — the stop gesture, so twitchy signals are acceptable here.
/// </summary>
public sealed class FaceDownDetector
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(700);
    // MAUI reports Z ≈ +1 face-up on both platforms, so face-down is the negative end.
    private const float FaceDownZ = -0.85f;
    private const float PocketMaxZ = 0.5f;

    private Moment? _heldSince;
    private bool _isCovered;
    private bool _hasFired;

    public void SetProximityCovered(bool isCovered)
    {
        _isCovered = isCovered;
        if (!isCovered)
            _heldSince = null;
    }

    public bool Process(SensorSample sample)
    {
        var isFaceDown = sample.Z <= FaceDownZ;
        var isPocketed = _isCovered && MathF.Abs(sample.Z) <= PocketMaxZ;
        if (!isFaceDown && !isPocketed) {
            _heldSince = null;
            _hasFired = false;
            return false;
        }

        _heldSince ??= sample.At;
        if (_hasFired || sample.At - _heldSince.Value < Dwell)
            return false;

        _hasFired = true;
        return true;
    }

    public void Reset()
    {
        _heldSince = null;
        _hasFired = false;
        _isCovered = false;
    }
}
