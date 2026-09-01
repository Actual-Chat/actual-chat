namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on portrait → landscape → portrait, where landscape must be held for at least
/// <see cref="LandscapeDwell"/> and at most <see cref="FlipWindow"/>, dated by the return to
/// portrait, and only gravity-dominated samples classify.
/// Two rotations in sequence is what makes it deliberate enough to open the mic.
/// </summary>
public sealed class FlipToTalkDetector
{
    public static readonly TimeSpan FlipWindow = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LandscapeDwell = TimeSpan.FromMilliseconds(120);
    private const float MinDominance = 0.7f;
    private const float MaxGravityDeviation = 0.3f;

    private GravityAxis _lastAxis;
    private Moment _lastAxisSince;
    private bool _isLandscapeFromPortrait;

    public bool Process(SensorSample sample)
    {
        // Only a reading dominated by gravity says anything about orientation: in motion the
        // accelerometer reads gravity + hand acceleration, so a lateral jerk mimics landscape.
        if (MathF.Abs(sample.Magnitude - 1f) > MaxGravityDeviation)
            return false;

        var axis = sample.GetDominantAxis(MinDominance);
        if (axis == GravityAxis.None)
            return false;

        if (axis == _lastAxis)
            return false;

        var lastAxis = _lastAxis;
        // Captured before the overwrite: the return to portrait is what dates the landscape hold.
        // Measuring it on an intervening landscape sample instead would need one to land past the
        // dwell, which at the ~68ms sample cadence a real quick flip (~135ms) never provides.
        var lastAxisSince = _lastAxisSince;
        _lastAxis = axis;
        _lastAxisSince = sample.At;
        if (axis == GravityAxis.X) {
            _isLandscapeFromPortrait = lastAxis == GravityAxis.Y;
            return false;
        }
        if (axis == GravityAxis.Y && lastAxis == GravityAxis.X && _isLandscapeFromPortrait) {
            var dwell = sample.At - lastAxisSince;
            if (dwell >= LandscapeDwell && dwell <= FlipWindow) {
                Reset();
                _lastAxis = axis;
                _lastAxisSince = sample.At;
                return true;
            }
        }

        _isLandscapeFromPortrait = false;
        return false;
    }

    public void Reset()
    {
        _lastAxis = GravityAxis.None;
        _lastAxisSince = default;
        _isLandscapeFromPortrait = false;
    }
}
