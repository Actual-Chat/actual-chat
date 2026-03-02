namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// Normally you shouldn't use this class directly, use <see cref="BackgroundActivityUI"/> instead.
/// </summary>
public abstract class BackgroundStateTracker
{
    public abstract IState<bool> IsBackground { get; }
}

// Must be scoped!
public class WebBackgroundStateTracker : BackgroundStateTracker
{
    private readonly ComputedState<bool> _isBackgroundState;

    public override IState<bool> IsBackground => _isBackgroundState;

    public WebBackgroundStateTracker(IServiceProvider services)
    {
        var browserInfo = services.GetRequiredService<BrowserInfo>();
        _isBackgroundState = services.StateFactory().NewComputed(
            new ComputedState<bool>.Options() {
                UpdateDelayer = FixedDelayer.NextTick,
                TryComputeSynchronously = false,
                Category = StateCategories.Get(GetType(), nameof(IsBackground)),
            },
            async (_, ct) => !await browserInfo.IsVisible.Use(ct).ConfigureAwait(false));
    }
}
