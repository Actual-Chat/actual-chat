namespace ActualChat.UI.Blazor.Components;

public class OnOffAnimator : ComponentAnimator
{
    private bool _state;

    public bool State { get => _state; set => ChangeState(value); }
    public string Class { get; private set; }

    public OnOffAnimator(ComponentBase component, TimeSpan duration, bool state = false)
        : base(component, duration)
    {
        _state = state;
        Class = state ? "on" : "off";
    }

    private void ChangeState(bool newState)
    {
        var remainingDuration = (AnimationEndsAt - Clock.Now).Positive();
        var isAnimating = remainingDuration != TimeSpan.Zero;
        var skipAnimation = TimeSpan.Zero;
        var (newClass, duration) = newState
            ? Class switch {
                "off" => ("off-to-on", Duration),
                "off-to-on" => (isAnimating ? Class : "on", remainingDuration),
                "on-to-off" => ("off-to-on", Duration),
                "on" => (Class, skipAnimation),
                _ => throw StandardError.Internal($"Invalid Class: '{Class}'."),
            }
            : Class switch {
                "off" => (Class, skipAnimation),
                "off-to-on" => ("on-to-off", Duration),
                "on-to-off" => (isAnimating ? Class : "off", remainingDuration),
                "on" => ("on-to-off", Duration),
                _ => throw StandardError.Internal($"Invalid Class: '{Class}'."),
            };

        _state = newState;
        Class = newClass;
        if (duration != skipAnimation && (!isAnimating || duration != remainingDuration))
            BeginAnimation(duration);
    }
}
