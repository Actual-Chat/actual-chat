namespace ActualChat.UI.Blazor.Components;

public class ComponentAnimator : IDisposable
{
    private CancellationTokenSource? _lastAnimateCts;

    public ComponentBase Component { get; }
    public TimeSpan Duration { get; }
    public IMomentClock Clock { get; }
    public Moment AnimationEndsAt { get; private set; }
    public bool IsAnimating => AnimationEndsAt > Clock.Now;

    public ComponentAnimator(ComponentBase component, TimeSpan duration, IMomentClock? clock = null)
    {
        Component = component;
        Duration = duration;
        Clock = clock ?? MomentClockSet.Default.CpuClock;
    }

    public void Dispose()
    {
        AnimationEndsAt = default;
        _lastAnimateCts?.CancelAndDisposeSilently();
    }

    public ComponentAnimator BeginAnimation(TimeSpan? duration = null)
    {
        _lastAnimateCts?.CancelAndDisposeSilently();
        _lastAnimateCts = new CancellationTokenSource();
        var cancellationToken = _lastAnimateCts.Token;
        AnimationEndsAt = Clock.Now + (duration ?? Duration);
        _ = Clock.Delay(AnimationEndsAt, cancellationToken).ContinueWith(_ => {
            if (cancellationToken.IsCancellationRequested)
                return;

            AnimationEndsAt = default;
            Component.NotifyStateHasChanged();
        }, TaskScheduler.Current);
        return this;
    }

    public ComponentAnimator EndAnimation()
    {
        _lastAnimateCts?.CancelAndDisposeSilently();
        _lastAnimateCts = null;
        AnimationEndsAt = default;
        Component.NotifyStateHasChanged();
        return this;
    }
}
