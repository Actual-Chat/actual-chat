namespace ActualChat.UI.Blazor.Components;

public class ShowHideAnimator : ComponentAnimator
{
    private bool _state;

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMilliseconds(50);

    public bool State { get => _state; set => ChangeState(value); }
    public string Class { get; private set; }
    public bool MustHideComponent => OrdinalEquals(Class, "hidden");

    public ShowHideAnimator(ComponentBase component, TimeSpan duration, bool state = false)
        : base(component, duration)
    {
        _state = state;
        Class = state ? "" : "hidden";
    }

    private void ChangeState(bool newState)
    {
        var remainingDuration = (AnimationEndsAt - Clock.Now).Positive();
        var isAnimating = remainingDuration != TimeSpan.Zero;
        var skipAnimation = TimeSpan.Zero;
        var (newClass, duration) = newState
            ? Class switch {
                "hidden" => ("off", MinDuration),
                "off" => ("off-to-on", Duration),
                "off-to-on" => (isAnimating ? Class : "", remainingDuration),
                "" => (Class, skipAnimation),
                "on-to-off" => ("off-to-on", Duration),
                _ => throw StandardError.Internal($"Invalid Class: '{Class}'."),
            }
            : Class switch {
                "hidden" => (Class, skipAnimation),
                "off" => ("hidden", skipAnimation),
                "off-to-on" => ("on-to-off", Duration),
                "" => ("on-to-off", Duration),
                "on-to-off" => (isAnimating ? Class : "hidden", remainingDuration),
                _ => throw StandardError.Internal($"Invalid Class: '{Class}'."),
            };

        _state = newState;
        Class = newClass;
        if (duration != skipAnimation && (!isAnimating || duration != remainingDuration))
            BeginAnimation(duration);
    }
}
