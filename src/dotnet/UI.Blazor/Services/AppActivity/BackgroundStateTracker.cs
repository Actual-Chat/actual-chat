namespace ActualChat.UI.Blazor.Services;

public abstract class BackgroundStateTracker
{
    public IState<bool> IsBackground { get; protected init; } = null!;
}

// Must be scoped!
public class WebBackgroundStateTracker : BackgroundStateTracker
{
    public WebBackgroundStateTracker(IServiceProvider services)
    {
        var browserInfo = services.GetRequiredService<BrowserInfo>();
        IsBackground = services.StateFactory().NewComputed(
            new ComputedState<bool>.Options() {
                UpdateDelayer = FixedDelayer.NextTick,
                TryComputeSynchronously = false,
                Category = StateCategories.Get(GetType(), nameof(IsBackground)),
            },
            async (_, ct) => !await browserInfo.IsVisible.Use(ct).ConfigureAwait(false));
    }
}

// Must be singleton!
public class MauiBackgroundStateTracker : BackgroundStateTracker
{
    public new MutableState<bool> IsBackground { get; }

    public MauiBackgroundStateTracker(IServiceProvider services)
        => base.IsBackground = IsBackground = services.StateFactory().NewMutable(
            false,
            StateCategories.Get(GetType(), nameof(IsBackground)));
}
