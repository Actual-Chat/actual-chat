namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires when the device is held face-down, or covered and near-vertical (pocket),
/// for <see cref="Dwell"/> — the stop gesture, so twitchy signals are acceptable here.
/// A slow drift into face-down (reclining with the phone overhead) must also rest still on a
/// proximity-covering surface for <see cref="StillDwell"/>; only a fast flip fires on dwell alone.
/// </summary>
public sealed class FaceDownDetector
{
    public static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(700);
    // On-device (2026-08-11, CPH2747): deliberate flips cross the band in 67-136ms, lay-down
    // reclines in 407-475ms - 250ms splits the classes with ~2x margin on both sides.
    public static readonly TimeSpan FastEntryWindow = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan StillDwell = TimeSpan.FromMilliseconds(1200);
    // MAUI reports Z ≈ +1 face-up on both platforms, so face-down is the negative end.
    private const float FaceDownZ = -0.85f;
    private const float PocketMaxZ = 0.5f;
    private const float NotFaceDownZ = -0.3f;
    private const float StillTolerance = 0.04f;

    private Moment? _heldSince;
    private Moment? _lastNotFaceDownAt;
    private Moment? _stillSince;
    private double _entryDurationMs;
    private bool _isFastEntry;
    private bool _isCovered;
    private bool _hasFired;

    public string? LastFireInfo { get; private set; }

    public void SetProximityCovered(bool isCovered)
    {
        _isCovered = isCovered;
        if (!isCovered)
            _heldSince = null;
    }

    public bool Process(SensorSample sample)
    {
        if (sample.Z > NotFaceDownZ)
            _lastNotFaceDownAt = sample.At;

        var isFaceDown = sample.Z <= FaceDownZ;
        var isPocketed = _isCovered && MathF.Abs(sample.Z) <= PocketMaxZ;
        if (!isFaceDown && !isPocketed) {
            _heldSince = null;
            _stillSince = null;
            _hasFired = false;
            return false;
        }

        if (_heldSince is null) {
            _heldSince = sample.At;
            // A deliberate flip crosses from clearly-not-face-down in a fraction of a second;
            // a recline drifts in slowly. No recorded not-face-down sample (armed while
            // already face-down) also counts as slow.
            _isFastEntry = isPocketed
                || (_lastNotFaceDownAt is { } lastNotFaceDownAt
                    && sample.At - lastNotFaceDownAt <= FastEntryWindow);
            _entryDurationMs = _lastNotFaceDownAt is { } lastAt
                ? (sample.At - lastAt).TotalMilliseconds
                : -1;
        }

        if (MathF.Abs(sample.Magnitude - 1f) <= StillTolerance)
            _stillSince ??= sample.At;
        else
            _stillSince = null;

        if (_hasFired || sample.At - _heldSince.Value < Dwell)
            return false;

        // A slow entry fires only when the phone rests on something: still AND covering the
        // proximity sensor. A braced arm in bed holds the phone desk-still, but nothing
        // touches the glass, so stillness alone is not enough.
        var isStillLongEnough = _stillSince is { } stillSince && sample.At - stillSince >= StillDwell;
        if (!_isFastEntry && !(isStillLongEnough && _isCovered))
            return false;

        _hasFired = true;
        var stillMs = _stillSince is { } stillAt ? (sample.At - stillAt).TotalMilliseconds : 0;
        var dwellMs = (sample.At - _heldSince.Value).TotalMilliseconds;
        LastFireInfo = $"entry={(_isFastEntry ? "fast" : "slow")}({_entryDurationMs:F0}ms)"
            + $" dwell={dwellMs:F0}ms still={stillMs:F0}ms covered={_isCovered}"
            + $" z={sample.Z:F2} |a|={sample.Magnitude:F3}";
        return true;
    }

    public string FormatStatus(Moment now)
    {
        if (_heldSince is not { } heldSince)
            return "idle";

        var stillMs = _stillSince is { } stillAt ? (now - stillAt).TotalMilliseconds : 0;
        return $"held={(_isFastEntry ? "fast" : "slow")}({_entryDurationMs:F0}ms)"
            + $" {(now - heldSince).TotalMilliseconds:F0}ms still={stillMs:F0}ms covered={_isCovered}"
            + (_hasFired ? " FIRED" : "");
    }

    public void Reset()
    {
        // _isCovered survives: it mirrors the physical proximity sensor, not gesture progress,
        // and no fresh reading arrives after a reset - SensorFeed pushes false on sensor stop.
        _heldSince = null;
        _lastNotFaceDownAt = null;
        _stillSince = null;
        _isFastEntry = false;
        _hasFired = false;
    }
}
