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

    private IBackgroundActivities? _backgroundActivityProvider;
    private BrowserInfo? _browserInfo;

    private ILogger Log { get; } = services.LogFor(typeof(BackgroundUI));
    private HostInfo HostInfo { get; } = services.GetRequiredService<HostInfo>();
    private BrowserInfo BrowserInfo => _browserInfo ??= services.GetRequiredService<BrowserInfo>();

    private IBackgroundActivities BackgroundActivities => _backgroundActivityProvider
        ??= services.GetRequiredService<IBackgroundActivities>();

    public IState<BackgroundState> State => _state;

    void INotifyInitialized.Initialized()
        => this.Start();

    [ComputeMethod]
    protected virtual async Task<bool> GetIsBackground(CancellationToken cancellationToken)
    {
        var isBackground = _isBackground != 0;
        if (HostInfo.ClientKind.IsMobile()) {
            Log.LogDebug("GetIsBackground(Mobile): {IsBackground}", isBackground);
            return isBackground;
        }

        var isVisible = await BrowserInfo.IsVisible.Use(cancellationToken).ConfigureAwait(false);
        isBackground = !isVisible;
        Log.LogDebug("GetIsBackground(Browser): {IsBackground}", isBackground);
        return isBackground;
    }

    void IBackgroundStateHandler.SetBackgroundState(bool isBackground)
    {
        Log.LogDebug("SetBackgroundState: {IsBackground}", isBackground);

        var newIsBackground = isBackground ? 1 : 0;
        var oldIsBackground = Interlocked.Exchange(ref _isBackground, newIsBackground);
        if (newIsBackground == oldIsBackground)
            return;

        using (Computed.Invalidate())
            _ = GetIsBackground(default);
    }
}
