namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Decides whether a proximity reading means "pocketed", i.e. that start gestures must be
/// suppressed. Raw "near" is untrustworthy: it spikes while the phone lies still, so a cover
/// counts only once it has held, and never against a phone resting screen-up.
/// </summary>
public sealed class ProximityGuard
{
    private const float FlatMinZ = 0.7f;
    private const float MaxGravityDeviation = 0.3f;

    private bool _isCovered;
    private bool _isRestingFaceUp;
    private Moment _coveredSince;

    public void SetCovered(bool isCovered, Moment at)
    {
        if (isCovered == _isCovered)
            return;

        _isCovered = isCovered;
        _coveredSince = at;
    }

    public bool IsSuppressing(SensorSample sample)
    {
        UpdateRestingFaceUp(sample);
        if (!_isCovered)
            return false;
        // The edge can land before the first sample, leaving no timestamp to date it from - and an
        // unset one reads as covered forever, which skips the dwell the guard exists for.
        if (_coveredSince == default)
            _coveredSince = sample.At;
        if (sample.At - _coveredSince < Constants.Audio.ProximitySuppressionDelay)
            return false;

        return !_isRestingFaceUp;
    }

    // Private methods

    private void UpdateRestingFaceUp(SensorSample sample)
    {
        // Latched, and only gravity-dominated readings move it: shaking a phone is exactly when
        // the veto has to hold, and mid-shake the accelerometer reads gravity plus hand motion.
        if (MathF.Abs(sample.Magnitude - 1f) > MaxGravityDeviation)
            return;

        _isRestingFaceUp = sample.Z >= FlatMinZ;
    }
}
