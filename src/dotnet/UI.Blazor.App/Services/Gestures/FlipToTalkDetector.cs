namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on portrait → landscape → portrait within <see cref="FlipWindow"/>, where landscape
/// must be held for <see cref="LandscapeDwell"/> and only gravity-dominated samples classify.
/// Two rotations in sequence is what makes it deliberate enough to open the mic.
/// </summary>
public sealed class FlipToTalkDetector
{
    public static readonly TimeSpan FlipWindow = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan LandscapeDwell = TimeSpan.FromMilliseconds(200);
    private const float MinDominance = 0.7f;
    private const float MaxGravityDeviation = 0.3f;

    private GravityAxis _lastAxis;
    private Moment _lastAxisSince;
    private bool _isLandscapeFromPortrait;
    private Moment _leftPortraitAt;
    private bool _hasLeftPortrait;

    public bool Process(SensorSample sample)
    {
        // Only a reading dominated by gravity says anything about orientation: in motion the
        // accelerometer reads gravity + hand acceleration, so a lateral jerk mimics landscape.
        if (MathF.Abs(sample.Magnitude - 1f) > MaxGravityDeviation)
            return false;

        var axis = sample.GetDominantAxis(MinDominance);
        if (axis == GravityAxis.None)
            return false;

        if (axis == _lastAxis) {
            if (axis == GravityAxis.X && _isLandscapeFromPortrait && !_hasLeftPortrait
                && sample.At - _lastAxisSince >= LandscapeDwell) {
                _hasLeftPortrait = true;
                _leftPortraitAt = _lastAxisSince;
            }

            return false;
        }

        var lastAxis = _lastAxis;
        _lastAxis = axis;
        _lastAxisSince = sample.At;
        if (axis == GravityAxis.X) {
            _isLandscapeFromPortrait = lastAxis == GravityAxis.Y;
            return false;
        }
        if (axis == GravityAxis.Y && _hasLeftPortrait && sample.At - _leftPortraitAt <= FlipWindow) {
            Reset();
            _lastAxis = axis;
            _lastAxisSince = sample.At;
            return true;
        }

        _isLandscapeFromPortrait = false;
        _hasLeftPortrait = false;
        return false;
    }

    public void Reset()
    {
        _lastAxis = GravityAxis.None;
        _lastAxisSince = default;
        _isLandscapeFromPortrait = false;
        _hasLeftPortrait = false;
        _leftPortraitAt = default;
    }
}
