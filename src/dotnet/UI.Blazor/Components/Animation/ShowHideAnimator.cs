namespace ActualChat.UI.Blazor.Components;

public class ShowHideAnimator : ComponentAnimator
{
    private bool _state;

    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMicroseconds(50);

    public bool State { get => _state; set => Transition(value); }
    public string Class { get; private set; }
    public bool MustHideComponent => OrdinalEquals(Class, "hidden");

    public ShowHideAnimator(ComponentBase component, TimeSpan duration, IMomentClock clock, bool state = false)
        : base(component, duration, clock)
    {
        State = state;
        Class = state ? "" : "hidden";
    }

    private void Transition(bool newState)
    {
        var remainingDuration = (AnimationEndsAt - Clock.Now).Positive();
        var isAnimating = remainingDuration == TimeSpan.Zero;
        var skipAnimation = TimeSpan.Zero;
        var (newClass, duration) = newState
            ? Class switch {
                "hidden" => ("off", MinDuration),
                "off" => ("off-to-on", Duration),
                "off-to-on" => (isAnimating ? Class : "", skipAnimation),
                "" => (Class, skipAnimation),
                "on-to-off" => ("off-to-on", Duration),
                _ => (Class, skipAnimation),
            }
            : Class switch {
                "hidden" => ("hidden", skipAnimation),
                "off" => ("hidden", skipAnimation),
                "off-to-on" => ("on-to-off", Duration),
                "" => ("on-to-off", Duration),
                "on-to-off" => (isAnimating ? Class : "hidden", skipAnimation),
                _ => (Class, default),
            };

        _state = newState;
        Class = newClass;
        if (duration != skipAnimation)
            BeginAnimation(duration);
    }
}
