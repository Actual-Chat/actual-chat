using ActualChat.Hosting;
using Stl.Interception;

namespace ActualChat.UI.Blazor.Services;

// ReSharper disable once ClassWithVirtualMembersNeverInherited.Global
public partial class BackgroundUI(IServiceProvider services) : WorkerBase,
    IComputeService,
    INotifyInitialized,
    IBackgroundStateHandler
{
    private volatile int _isBackground;

    private readonly IMutableState<BackgroundState> _state = services
        .StateFactory()
        .NewMutable(
            BackgroundState.Foreground,
            StateCategories.Get(typeof(BackgroundUI), nameof(State)));

    private IBackgroundActivities? _backgroundActivities;

    private IBackgroundActivities BackgroundActivities
        => _backgroundActivities ??= services.GetRequiredService<IBackgroundActivities>();
    private BrowserInfo? BrowserInfo // Intended: null on mobile
        => services.GetRequiredService<HostInfo>().ClientKind.IsMobile() ? null
            : services.GetRequiredService<BrowserInfo>();
    private ILogger Log { get; } = services.LogFor(typeof(BackgroundUI));

    public IState<BackgroundState> State => _state;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    protected virtual async Task<bool> IsBackground(CancellationToken cancellationToken)
    {
        var isBackground = BrowserInfo != null
            ? !await BrowserInfo.IsVisible.Use(cancellationToken).ConfigureAwait(false)
            : _isBackground != 0;
        Log.LogDebug("IsBackground: {IsBackground}", isBackground);
        return isBackground;
    }

    void IBackgroundStateHandler.SetIsBackground(bool value)
    {
        if (BrowserInfo != null)
            return; // Ignored in browser

        Log.LogDebug("SetIsBackground: {IsBackground}", value);
        var newValue = value ? 1 : 0;
        var oldValue = 1 - newValue;
        if (Interlocked.CompareExchange(ref _isBackground, newValue, oldValue) != oldValue)
            return;

        using (Computed.Invalidate())
            _ = IsBackground(default);
    }
}
