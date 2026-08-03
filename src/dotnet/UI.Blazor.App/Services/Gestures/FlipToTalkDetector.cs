namespace ActualChat.UI.Blazor.App.Services.Gestures;

/// <summary>
/// Fires on portrait → landscape → portrait within <see cref="FlipWindow"/>.
/// Two rotations in sequence is what makes it deliberate enough to open the mic.
/// </summary>
public sealed class FlipToTalkDetector
{
    public static readonly TimeSpan FlipWindow = TimeSpan.FromSeconds(2);
    private const float MinDominance = 0.7f;

    private GravityAxis _lastAxis;
    private Moment _leftPortraitAt;
    private bool _hasLeftPortrait;

    public bool Process(SensorSample sample)
    {
        var axis = sample.GetDominantAxis(MinDominance);
        if (axis == GravityAxis.None || axis == _lastAxis)
            return false;

        var lastAxis = _lastAxis;
        _lastAxis = axis;
        if (axis == GravityAxis.X && lastAxis == GravityAxis.Y) {
            _leftPortraitAt = sample.At;
            _hasLeftPortrait = true;
            return false;
        }
        if (axis == GravityAxis.Y && _hasLeftPortrait && sample.At - _leftPortraitAt <= FlipWindow) {
            Reset();
            _lastAxis = axis;
            return true;
        }

        _hasLeftPortrait = false;
        return false;
    }

    public void Reset()
    {
        _lastAxis = GravityAxis.None;
        _hasLeftPortrait = false;
        _leftPortraitAt = default;
    }
}
