namespace ActualChat.UI.Blazor.Services;

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

// Must be singleton!
public class MauiBackgroundStateTracker : BackgroundStateTracker, IDisposable
{
    private static bool _isBackground;
    private static event EventHandler IsBackgroundChanged = delegate { };

    private readonly MutableState<bool> _isBackgroundState;

    public override IState<bool> IsBackground => _isBackgroundState;

    public MauiBackgroundStateTracker(IServiceProvider services)
    {
        _isBackgroundState = services.StateFactory()
            .NewMutable(
                _isBackground,
                StateCategories.Get(GetType(), nameof(IsBackground)));
        IsBackgroundChanged += OnIsBackgroundChanged;
    }

    public void Dispose()
        => IsBackgroundChanged -= OnIsBackgroundChanged;

    public static void SetBackgroundState(bool isBackground)
    {
        if (_isBackground == isBackground)
            return;

        _isBackground = isBackground;
        IsBackgroundChanged(null, EventArgs.Empty);
    }

    private void OnIsBackgroundChanged(object? sender, EventArgs e)
        => _isBackgroundState.Value = _isBackground;
}
