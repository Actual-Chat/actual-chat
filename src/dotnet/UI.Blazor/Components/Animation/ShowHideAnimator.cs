namespace ActualChat.UI.Blazor.Components;

public class ShowHideAnimator : ComponentAnimator
{
    public string Class { get; set; } = "hidden";
    public TimeSpan MinDuration { get; init; } = TimeSpan.FromMicroseconds(50);
    public bool IsVisible => !OrdinalEquals(Class, "hidden");

    public ShowHideAnimator(ComponentBase component, TimeSpan duration, IMomentClock clock)
        : base(component, duration, clock)
    { }

    public void BeginTransition(bool isOn)
    {
        var remainingDuration = (AnimationEndsAt - Clock.Now).Positive();
        var isAnimating = remainingDuration == TimeSpan.Zero;
        var skipAnimation = TimeSpan.Zero;
        var (newClass, duration) = isOn
            ? Class switch {
                "hidden" => ("off", MinDuration),
                "off" => ("off-to-on", Duration),
                "off-to-on" => (isAnimating ? Class : "", isAnimating ? skipAnimation : MinDuration),
                "on-to-off" => ("off-to-on", Duration),
                _ => (Class, skipAnimation),
            }
            : Class switch {
                "hidden" => ("hidden", skipAnimation),
                "off" => ("hidden", skipAnimation),
                "off-to-on" => ("on-to-off", Duration),
                "on-to-off" => (isAnimating ? Class : "hidden", isAnimating ? skipAnimation : MinDuration),
                _ => (Class, default),
            };
        Class = newClass;
        if (duration != skipAnimation)
            BeginAnimation(duration);
    }
}
