namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Detects "the phone was put away" - held face-down, or covered and near-vertical (pocket) -
/// for <see cref="Dwell"/>, and reports which of the two fired.
/// A slow drift into face-down (reclining with the phone overhead) must also rest still for
/// <see cref="StillDwell"/>; only a fast flip fires on dwell alone. Either way the proximity
/// sensor must be covered: a phone put down or pocketed covers it, one waved about doesn't.
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
    public bool HasEntered => _lastNotFaceDownAt is not null;

    public void SetProximityCovered(bool isCovered)
    {
        _isCovered = isCovered;
        if (!isCovered)
            _heldSince = null;
    }

    public GestureKind Process(SensorSample sample)
    {
        if (sample.Z > NotFaceDownZ)
            _lastNotFaceDownAt = sample.At;

        var isFaceDown = sample.Z <= FaceDownZ;
        var isPocketed = _isCovered && MathF.Abs(sample.Z) <= PocketMaxZ;
        if (!isFaceDown && !isPocketed) {
            _heldSince = null;
            _stillSince = null;
            _hasFired = false;
            return GestureKind.None;
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
            return GestureKind.None;

        // The hold keeps running while uncovered, so a phone held face-down and then set down
        // fires as soon as it lands rather than restarting its dwell on the surface.
        if (!_isCovered)
            return GestureKind.None;

        // A slow entry fires only when the phone rests on something: still AND covering the
        // proximity sensor. A braced arm in bed holds the phone desk-still, but nothing
        // touches the glass, so stillness alone is not enough.
        var isStillLongEnough = _stillSince is { } stillSince && sample.At - stillSince >= StillDwell;
        if (!_isFastEntry && !isStillLongEnough)
            return GestureKind.None;

        _hasFired = true;
        var kind = isPocketed ? GestureKind.Pocket : GestureKind.FaceDown;
        var stillMs = _stillSince is { } stillAt ? (sample.At - stillAt).TotalMilliseconds : 0;
        var dwellMs = (sample.At - _heldSince.Value).TotalMilliseconds;
        LastFireInfo = $"{kind} entry={(_isFastEntry ? "fast" : "slow")}({_entryDurationMs:F0}ms)"
            + $" dwell={dwellMs:F0}ms still={stillMs:F0}ms covered={_isCovered}"
            + $" z={sample.Z:F2} |a|={sample.Magnitude:F3}";
        return kind;
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
