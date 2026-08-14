namespace ActualChat.UI.Blazor.Services;

// Must be scoped!
public sealed class WebBackgroundStateTracker : BackgroundStateTracker
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
